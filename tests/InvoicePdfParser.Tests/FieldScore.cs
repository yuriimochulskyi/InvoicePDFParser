namespace InvoicePdfParser.Tests;

public sealed record FieldScore(int Correct, int Total, IReadOnlyList<string> Mismatched);
