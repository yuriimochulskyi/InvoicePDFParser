using System.Text.Json.Serialization;
using InvoicePdfParser.Api.Configuration;
using InvoicePdfParser.Api.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/invoice-pdf-parser-.log", rollingInterval: RollingInterval.Day));

var aiOptions = builder.Configuration.GetSection(AiOptions.Section).Get<AiOptions>() ?? new();
builder.Services.AddInvoiceParser(aiOptions, Path.Combine(builder.Environment.ContentRootPath, "uploads"));
builder.Services.AddDbContext<InvoiceDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Invoices")));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Unhandled exceptions become RFC 9457 problem responses (500) instead of empty bodies.
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// The schema is created on startup; EF migrations are on the roadmap.
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<InvoiceDbContext>().Database.EnsureCreated();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

app.Run();

/// <summary>Exposed so WebApplicationFactory can host the API in tests.</summary>
public partial class Program;
