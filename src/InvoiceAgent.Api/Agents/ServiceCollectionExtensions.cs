using InvoiceAgent.Api.Tools;

namespace InvoiceAgent.Api.Agents;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the extraction pipeline. The API and the evals both call this, so the
    /// evals exercise exactly the wiring that runs in production. The caller registers
    /// <see cref="Data.InvoiceDbContext"/> and logging.
    /// </summary>
    public static IServiceCollection AddInvoiceAgent(this IServiceCollection services, AiOptions options, string uploadsPath)
    {
        var (chatClient, modelInfo) = ChatClientFactory.Create(options);
        services.AddSingleton(options);
        services.AddSingleton(chatClient);
        services.AddSingleton(modelInfo);
        services.AddSingleton(new PdfFileStore(uploadsPath));
        services.AddSingleton<PdfTextExtractor>();
        services.AddScoped<InvoiceExtractionAgent>();
        services.AddScoped<InvoiceProcessingService>();
        return services;
    }
}

/// <summary>The model endpoint could not be reached or returned an error; nothing was extracted.</summary>
public sealed class LlmUnavailableException(string provider, string model, Exception inner)
    : Exception($"LLM provider {provider} (model {model}) is unavailable: {inner.Message}", inner);
