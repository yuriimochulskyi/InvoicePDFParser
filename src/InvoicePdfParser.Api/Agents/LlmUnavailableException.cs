namespace InvoicePdfParser.Api.Agents;

/// <summary>The model endpoint could not be reached or returned an error; nothing was extracted.</summary>
public sealed class LlmUnavailableException(string provider, string model, Exception inner)
    : Exception($"LLM provider {provider} (model {model}) is unavailable: {inner.Message}", inner);
