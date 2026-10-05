using Microsoft.Extensions.AI;

namespace InvoiceAgent.Evals;

/// <summary>
/// An <see cref="IChatClient"/> that answers from a script instead of a model. Each entry
/// produces one assistant message; a <see cref="FunctionCallContent"/> entry makes the real
/// <see cref="FunctionInvokingChatClient"/> execute the real tool. Lets the agent loop,
/// the tools and the review policy run in CI with no GPU and no Ollama.
/// </summary>
public sealed class ScriptedChatClient(params Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatMessage>[] script) : IChatClient
{
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatMessage>> _script = new(script);

    /// <summary>Every request the agent made, in order: messages and options.</summary>
    public List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = [];

    public static Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatMessage> ToolCall(string name, object args) =>
        (_, _) => new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), name,
            args.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(args)))]);

    public static Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatMessage> Text(string text) =>
        (_, _) => new ChatMessage(ChatRole.Assistant, text);

    public static Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatMessage> Throw(Exception ex) =>
        (_, _) => throw ex;

    /// <summary>Calls ExtractPdfText with the fileId quoted in the user's request, as a real model would.</summary>
    public static Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatMessage> ReadPdf { get; } = (messages, options) =>
    {
        var text = messages.Last(m => m.Role == ChatRole.User).Text;
        var start = text.IndexOf('"') + 1;
        return ToolCall("ExtractPdfText", new { fileId = text[start..text.IndexOf('"', start)] })(messages, options);
    };

    /// <summary>The correct extraction of samples/invoices/02-de-rechnung.pdf.</summary>
    public const string GermanInvoiceJson = """
        {"vendorName":"Müller Webdesign GmbH","vendorTaxId":"DE287654321","invoiceNumber":"RE-2026-0147",
         "invoiceDate":"2026-08-15","dueDate":"2026-09-14","currency":"EUR",
         "lineItems":[{"description":"Webentwicklung (Stunden)","quantity":12,"unitPrice":85.00,"amount":1020.00},
                      {"description":"Hosting-Paket Business (12 Monate)","quantity":1,"unitPrice":240.00,"amount":240.00},
                      {"description":"SSL-Zertifikat","quantity":1,"unitPrice":49.90,"amount":49.90}],
         "subtotal":1309.90,"taxAmount":248.88,"discountAmount":null,"total":1558.78}
        """;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Requests.Add((list, options));
        if (_script.Count == 0)
            throw new InvalidOperationException($"Script exhausted after {Requests.Count} requests.");

        var message = _script.Dequeue()(list, options);
        return Task.FromResult(new ChatResponse(message)
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 },
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The agent uses non-streaming calls.");

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }
}
