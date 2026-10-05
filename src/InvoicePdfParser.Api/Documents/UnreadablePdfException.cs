namespace InvoicePdfParser.Api.Documents;

public sealed class UnreadablePdfException(string message, Exception? inner = null) : Exception(message, inner);
