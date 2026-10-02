using InvoiceAgent.Api.Agents;
using System.Text.Json.Serialization;
using InvoiceAgent.Api.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/invoice-agent-.log", rollingInterval: RollingInterval.Day));

var aiOptions = builder.Configuration.GetSection(AiOptions.Section).Get<AiOptions>() ?? new();
builder.Services.AddInvoiceAgent(aiOptions, Path.Combine(builder.Environment.ContentRootPath, "uploads"));
builder.Services.AddDbContext<InvoiceDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Invoices")));

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Demo project: create the schema on startup instead of shipping migrations.
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<InvoiceDbContext>().Database.EnsureCreated();

app.UseSerilogRequestLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

app.Run();
