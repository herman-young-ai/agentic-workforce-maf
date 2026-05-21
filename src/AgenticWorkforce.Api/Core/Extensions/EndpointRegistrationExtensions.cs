using AgenticWorkforce.Api.Features.Auth;
using AgenticWorkforce.Api.Features.Members;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Api.Features.Sessions;
using AgenticWorkforce.Api.Features.Tasks;
using AgenticWorkforce.Api.Features.Team;

namespace AgenticWorkforce.Api.Core.Extensions;

public static class EndpointRegistrationExtensions
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        CreateProject.MapEndpoints(app);
        GetProject.MapEndpoints(app);
        ListProjects.MapEndpoints(app);
        UpdateProject.MapEndpoints(app);
        DeleteProject.MapEndpoints(app);
        PauseProject.MapEndpoints(app);
        ResumeProject.MapEndpoints(app);
        ArchiveProject.MapEndpoints(app);
    }

    public static void MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        ListTasks.MapEndpoints(app);
        GetTask.MapEndpoints(app);
        CreateTask.MapEndpoints(app);
        UpdateTask.MapEndpoints(app);
        ApproveTask.MapEndpoints(app);
        RejectTask.MapEndpoints(app);
        RetryTask.MapEndpoints(app);
        CancelTask.MapEndpoints(app);
        GetBoard.MapEndpoints(app);
        BulkApproveTask.MapEndpoints(app);
    }

    public static void MapSessionEndpoints(this IEndpointRouteBuilder app)
    {
        CreateSession.MapEndpoints(app);
        GetSession.MapEndpoints(app);
        ListSessions.MapEndpoints(app);
        ListMessages.MapEndpoints(app);
        SuspendSession.MapEndpoints(app);
        ResumeSession.MapEndpoints(app);
        CompleteSession.MapEndpoints(app);
    }

    public static void MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        ListMembers.MapEndpoints(app);
        AddMember.MapEndpoints(app);
        UpdateMember.MapEndpoints(app);
        RemoveMember.MapEndpoints(app);
        TransferOwnership.MapEndpoints(app);
    }

    public static void MapTeamEndpoints(this IEndpointRouteBuilder app)
    {
        ListTeam.MapEndpoints(app);
        AddAgent.MapEndpoints(app);
        RemoveAgent.MapEndpoints(app);
        UpdateAgentPrompt.MapEndpoints(app);
        SeedTeam.MapEndpoints(app);
    }

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        GetMe.MapEndpoints(app);
        UpdateMe.MapEndpoints(app);
        CreateApiKey.MapEndpoints(app);
        ListApiKeys.MapEndpoints(app);
        RevokeApiKey.MapEndpoints(app);
    }
}
