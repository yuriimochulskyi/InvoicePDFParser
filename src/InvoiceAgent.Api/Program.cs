using InvoiceAgent.Api.Agents;
using InvoiceAgent.Api.Tools;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/invoice-agent-.log", rollingInterval: RollingInterval.Day));

var aiOptions = builder.Configuration.GetSection(AiOptions.Section).Get<AiOptions>() ?? new();
var (chatClient, modelInfo) = ChatClientFactory.Create(aiOptions);
builder.Services.AddSingleton(chatClient);
builder.Services.AddSingleton(modelInfo);
builder.Services.AddSingleton(new PdfFileStore(Path.Combine(builder.Environment.ContentRootPath, "uploads")));
builder.Services.AddSingleton<PdfTextExtractor>();
builder.Services.AddScoped<InvoiceExtractionAgent>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

app.Run();
