using AgenticWorkforce.Domain.Exceptions;
using Microsoft.Extensions.AI;

namespace AgenticWorkforce.Agents.Runtime;

/// <summary>
/// Writes / reads the AWP per-call tags on <c>ChatOptions.AdditionalProperties</c>.
/// </summary>
internal static class ChatOptionsTagger
{
    public static ChatOptions Apply(
        ChatOptions options,
        AgentExecutionContext ctx,
        string? agentRole,
        string provider,
        string? requestId)
    {
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();
        options.AdditionalProperties[ChatOptionsKeys.ProjectId] = ctx.ProjectId;
        options.AdditionalProperties[ChatOptionsKeys.TaskId]    = ctx.TaskId;
        if (ctx.SessionId is { } sid) options.AdditionalProperties[ChatOptionsKeys.SessionId] = sid;
        options.AdditionalProperties[ChatOptionsKeys.AgentName] = ctx.AgentName;
        if (agentRole is not null) options.AdditionalProperties[ChatOptionsKeys.AgentRole] = agentRole;
        options.AdditionalProperties[ChatOptionsKeys.Provider]  = provider;
        if (requestId is not null) options.AdditionalProperties[ChatOptionsKeys.RequestId] = requestId;
        return options;
    }

    public static TaggedContext Read(ChatOptions? options)
    {
        var props = options?.AdditionalProperties
            ?? throw new InvalidStateException("ChatOptions.AdditionalProperties is missing AWP tags.");
        return new TaggedContext(
            ProjectId: GetRequired<Guid>(props, ChatOptionsKeys.ProjectId),
            TaskId:    GetOptional<Guid?>(props, ChatOptionsKeys.TaskId),
            SessionId: GetOptional<Guid?>(props, ChatOptionsKeys.SessionId),
            AgentName: GetOptional<string?>(props, ChatOptionsKeys.AgentName),
            AgentRole: GetOptional<string?>(props, ChatOptionsKeys.AgentRole),
            Provider:  GetRequired<string>(props, ChatOptionsKeys.Provider),
            RequestId: GetOptional<string?>(props, ChatOptionsKeys.RequestId));
    }

    private static T GetRequired<T>(AdditionalPropertiesDictionary props, string key)
    {
        if (props.TryGetValue(key, out var raw) && raw is T typed) return typed;
        throw new InvalidStateException($"Required ChatOptions tag '{key}' missing or wrong type (expected {typeof(T).Name}).");
    }

    private static T? GetOptional<T>(AdditionalPropertiesDictionary props, string key)
    {
        if (props.TryGetValue(key, out var raw) && raw is T typed) return typed;
        return default;
    }
}

internal readonly record struct TaggedContext(
    Guid ProjectId,
    Guid? TaskId,
    Guid? SessionId,
    string? AgentName,
    string? AgentRole,
    string Provider,
    string? RequestId);
