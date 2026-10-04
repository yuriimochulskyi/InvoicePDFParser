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
