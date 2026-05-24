using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Runtime;

/// <summary>
/// Canned-response IChatClient used until real Foundry / Azure OpenAI credentials
/// are provisioned. Returns a deterministic completion + token usage so the rest
/// of the pipeline (middleware, cost tracking, budget enforcement) can be
/// exercised end-to-end.
/// </summary>
internal sealed class StubChatClient : IChatClient
{
    private const string StubText =
        "This is a stub response from StubChatClient. Connect a real LLM provider to enable agent execution.";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, StubText))
        {
            ModelId = options?.ModelId,
            Usage = new UsageDetails
            {
                InputTokenCount = 100,
                OutputTokenCount = 50,
                TotalTokenCount = 150
            }
        };
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, StubText)
        {
            ModelId = options?.ModelId
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(StubChatClient) ? this : null;

    public void Dispose() { }
}
