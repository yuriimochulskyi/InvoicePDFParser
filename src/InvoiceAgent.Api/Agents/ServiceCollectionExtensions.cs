using Azure;
using Azure.AI.DocumentIntelligence;
using InvoiceAgent.Api.Tools;
using Microsoft.Extensions.AI;

namespace InvoiceAgent.Api.Agents;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the extraction pipeline against the configured provider. The API and
    /// the evals both call this, so the evals exercise exactly the production wiring.
    /// The caller registers <see cref="Data.InvoiceDbContext"/> and logging.
    /// </summary>
    public static IServiceCollection AddInvoiceAgent(this IServiceCollection services, AiOptions options, string uploadsPath)
    {
        var errors = options.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException("Invalid AI configuration: " + string.Join(" ", errors));

        var (chatClient, modelInfo) = ChatClientFactory.Create(options);
        return services.AddInvoiceAgent(options, uploadsPath, chatClient, modelInfo);
    }

    /// <summary>
    /// Same pipeline over an arbitrary <see cref="IChatClient"/>. Tests pass a scripted
    /// client here, so the agent loop, the tools and the review policy run without a model.
    /// </summary>
    public static IServiceCollection AddInvoiceAgent(this IServiceCollection services, AiOptions options, string uploadsPath,
        IChatClient chatClient, ChatClientFactory.ModelInfo modelInfo)
    {
        services.AddSingleton(options);
        services.AddSingleton(modelInfo);
        services.AddSingleton<IChatClient>(sp => chatClient.AsBuilder()
            // The agent loop: executes tool calls and feeds results back until the model
            // answers with text. Hard caps so a model that keeps calling tools cannot run
            // until the timeout; the tools' own 3-check budget is the soft limit.
            .UseFunctionInvocation(sp.GetRequiredService<ILoggerFactory>(), f =>
            {
                f.MaximumIterationsPerRequest = MaxToolIterations;
                f.MaximumConsecutiveErrorsPerRequest = 1;
            })
            .Build());
        services.AddSingleton(new PdfFileStore(uploadsPath));
        services.AddSingleton<PdfTextExtractor>();
        if (options.DocumentIntelligence.IsConfigured)
            services.AddSingleton<IOcrEngine>(new AzureDocumentIntelligenceOcr(new DocumentIntelligenceClient(
                new Uri(options.DocumentIntelligence.Endpoint), new AzureKeyCredential(options.DocumentIntelligence.ApiKey))));
        // Text layer first, OCR for scans when an engine is registered.
        services.AddSingleton<IPdfTextSource>(sp => new OcrFallbackTextSource(
            sp.GetRequiredService<PdfTextExtractor>(), sp.GetRequiredService<PdfFileStore>(),
            sp.GetRequiredService<ILogger<OcrFallbackTextSource>>(), sp.GetService<IOcrEngine>()));
        services.AddScoped<InvoiceExtractionAgent>();
        services.AddScoped<InvoiceProcessingService>();
        return services;
    }

    /// <summary>1 ExtractPdfText + up to 3 ValidateTotals + headroom for retries after a mismatch.</summary>
    public const int MaxToolIterations = 8;
}

/// <summary>The model endpoint could not be reached or returned an error; nothing was extracted.</summary>
public sealed class LlmUnavailableException(string provider, string model, Exception inner)
    : Exception($"LLM provider {provider} (model {model}) is unavailable: {inner.Message}", inner);
