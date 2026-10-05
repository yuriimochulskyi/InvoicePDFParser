using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using InvoiceAgent.Api.Agents;
using InvoiceAgent.Api.Data;
using InvoiceAgent.Api.Models;
using InvoiceAgent.Api.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InvoiceAgent.Evals;

/// <summary>
/// The HTTP contract over the real ASP.NET Core pipeline: status codes, ProblemDetails,
/// Location header and response shape. The model is a scripted IChatClient; the database
/// is in-memory SQLite; everything else is the production wiring.
/// </summary>
public sealed class ApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly string _uploads = Path.Combine(Path.GetTempPath(), "invoice-agent-tests", Guid.NewGuid().ToString("N"));

    public ApiTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private HttpClient CreateClient(params Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatMessage>[] script)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                // Swap the three things that touch the outside world; keep the rest.
                services.RemoveAll<IChatClient>();
                services.AddSingleton<IChatClient>(sp => new ScriptedChatClient(script).AsBuilder()
                    .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = ServiceCollectionExtensions.MaxToolIterations)
                    .Build());
                services.RemoveAll<PdfFileStore>();
                services.AddSingleton(new PdfFileStore(_uploads));
                services.RemoveAll<DbContextOptions<InvoiceDbContext>>();
                services.AddDbContext<InvoiceDbContext>(o => o.UseSqlite(_connection));
            });
        });
        return factory.CreateClient();
    }

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName = "invoice.pdf")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private string[] StoredUploads() => Directory.Exists(_uploads) ? Directory.GetFiles(_uploads) : [];

    private static byte[] Sample(string name) => File.ReadAllBytes(Samples.All().Single(s => s.Name == name).PdfPath);

    [Fact]
    public async Task Post_ValidInvoice_Returns201_WithLocation_AndGetReturnsTheSameRecord()
    {
        var client = CreateClient(ScriptedChatClient.ReadPdf, ScriptedChatClient.Text("done"), ScriptedChatClient.Text(ScriptedChatClient.GermanInvoiceJson));
        var ct = TestContext.Current.CancellationToken;

        var post = await client.PostAsync("/api/invoices", Upload(Sample("02-de-rechnung"), "02-de-rechnung.pdf"), ct);

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var created = (await post.Content.ReadFromJsonAsync<InvoiceResponse>(Json, ct))!;
        Assert.Equal(InvoiceStatus.Parsed, created.Status);
        Assert.Equal(1558.78m, created.Invoice!.Total);
        Assert.Equal("02-de-rechnung.pdf", created.FileName);
        Assert.Equal(["ExtractPdfText"], created.Telemetry.ToolCalls);
        Assert.Null(created.RawText); // not in the default payload
        Assert.EndsWith($"/api/invoices/{created.Id}", post.Headers.Location!.AbsolutePath);

        var get = await client.GetFromJsonAsync<InvoiceResponse>(post.Headers.Location, Json, ct);
        Assert.Equal(created.Id, get!.Id);
        Assert.Null(get.RawText);

        var withText = await client.GetFromJsonAsync<InvoiceResponse>(post.Headers.Location + "?includeText=true", Json, ct);
        Assert.Contains("Rechnungsbetrag", withText!.RawText);
    }

    [Fact]
    public async Task Post_Scan_Returns201_NeedsReview_WithoutCallingTheModel()
    {
        var client = CreateClient(); // empty script: any model call would throw
        var post = await client.PostAsync("/api/invoices", Upload(Sample("09-de-rechnung-scan")), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var body = (await post.Content.ReadFromJsonAsync<InvoiceResponse>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal(InvoiceStatus.NeedsReview, body.Status);
        Assert.Contains("no text layer", body.ReviewReason);
        Assert.Equal(0, body.Telemetry.InputTokens);
        Assert.Empty(body.Telemetry.ToolCalls);
    }

    [Theory]
    [InlineData("not a pdf at all", "Only PDF files are supported.")]
    [InlineData("%PDF-1.7 header but garbage", "not a readable PDF")]
    public async Task Post_InvalidFile_Returns400ProblemDetails(string content, string expectedDetail)
    {
        var client = CreateClient();
        var post = await client.PostAsync("/api/invoices", Upload(System.Text.Encoding.ASCII.GetBytes(content)), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        Assert.Equal("application/problem+json", post.Content.Headers.ContentType!.MediaType);
        var problem = (await post.Content.ReadFromJsonAsync<ProblemDetails>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal("Invalid upload", problem.Title);
        Assert.Contains(expectedDetail, problem.Detail);
        Assert.Empty(StoredUploads()); // a rejected file is not kept
    }

    [Fact]
    public async Task Post_WhenTheModelIsUnreachable_Returns503ProblemDetails_AndStoresNothing()
    {
        var client = CreateClient(ScriptedChatClient.Throw(new HttpRequestException("connection refused")));
        var post = await client.PostAsync("/api/invoices", Upload(Sample("02-de-rechnung")), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, post.StatusCode);
        var problem = (await post.Content.ReadFromJsonAsync<ProblemDetails>(Json, TestContext.Current.CancellationToken))!;
        Assert.Equal("LLM provider unavailable", problem.Title);

        await using var db = new InvoiceDbContext(new DbContextOptionsBuilder<InvoiceDbContext>().UseSqlite(_connection).Options);
        Assert.Empty(db.Invoices);
        Assert.Empty(StoredUploads());
    }

    [Fact]
    public async Task Get_UnknownId_Returns404ProblemDetails()
    {
        var client = CreateClient();
        var get = await client.GetAsync($"/api/invoices/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal("application/problem+json", get.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Swagger_DescribesTheTypedContract()
    {
        var client = CreateClient();
        var doc = await client.GetStringAsync("/swagger/v1/swagger.json", TestContext.Current.CancellationToken);

        Assert.Contains("InvoiceResponse", doc);
        Assert.Contains("RunTelemetry", doc);
        Assert.Contains("ProblemDetails", doc);
    }
}
