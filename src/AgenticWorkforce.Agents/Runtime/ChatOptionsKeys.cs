namespace AgenticWorkforce.Agents.Runtime;

/// <summary>
/// Well-known keys for per-call context tagged onto
/// <c>ChatOptions.AdditionalProperties</c>. Middleware reads via these keys;
/// only <see cref="ChatOptionsTagger"/> and <c>ChatClientFactory</c> write
/// them (asserted by an architecture test).
/// </summary>
internal static class ChatOptionsKeys
{
    public const string ProjectId = "awp.projectId";
    public const string SessionId = "awp.sessionId";
    public const string TaskId    = "awp.taskId";
    public const string AgentName = "awp.agentName";
    public const string AgentRole = "awp.agentRole";
    public const string Provider  = "awp.provider";
    public const string RequestId = "awp.requestId";
}
