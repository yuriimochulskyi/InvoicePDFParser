namespace InvoicePdfParser.Api.Documents;

/// <summary>A positioned word, in a coordinate system where Y grows downwards.</summary>
public readonly record struct LayoutWord(string Text, double Left, double Right, double CenterY, double Height);
