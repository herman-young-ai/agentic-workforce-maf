namespace AgenticWorkforce.Api.Hubs;

/// <summary>
/// SignalR group-name builders. The hub adds connections to these groups,
/// the <see cref="Services.SignalREventRelay"/> fans out to them — the
/// two sides must agree on the format, so the format lives in one place.
/// Group keys use <c>{aggregate}:{id:N}</c> matching the convention used
/// by the Redis channel namespace.
/// </summary>
internal static class HubGroups
{
    public static string Project(Guid projectId) => $"project:{projectId:N}";
    public static string Project(string projectIdSegment) => $"project:{projectIdSegment}";
    public static string Session(Guid sessionId) => $"session:{sessionId:N}";
    public static string User(Guid userId) => $"user:{userId:N}";
}
