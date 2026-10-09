namespace InvoicePdfParser.Api.Rag;

public static class RagServiceCollectionExtensions
{
    /// <summary>Registers the knowledge base over the markdown docs; the chat client comes from the invoice pipeline.</summary>
    public static IServiceCollection AddKnowledgeBase(this IServiceCollection services, RagOptions options, string contentRootPath)
    {
        services.AddSingleton(options);
        services.AddHttpClient<OllamaEmbeddingClient>(c => c.BaseAddress = new Uri(options.EmbeddingEndpoint));
        services.AddSingleton<VectorIndex>();
        services.AddHostedService(sp => new KnowledgeBaseIndexer(options, contentRootPath,
            sp.GetRequiredService<OllamaEmbeddingClient>(), sp.GetRequiredService<VectorIndex>(),
            sp.GetRequiredService<ILogger<KnowledgeBaseIndexer>>()));
        services.AddScoped<AskService>();
        return services;
    }
}
