using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Middleware;

/// <summary>
/// Phase 6 stub. Pass-through. Replaced with real Azure AI Content Safety
/// integration when credentials are provisioned. Keeps the pipeline shape
/// stable so downstream middlewares don't move.
/// </summary>
internal sealed class ContentSafetyChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
}
