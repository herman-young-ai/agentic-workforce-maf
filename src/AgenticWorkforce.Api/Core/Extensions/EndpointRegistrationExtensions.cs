using AgenticWorkforce.Api.Features.Admin.Catalog;
using AgenticWorkforce.Api.Features.Admin.Dashboard;
using AgenticWorkforce.Api.Features.Admin.Knowledge;
using AgenticWorkforce.Api.Features.Artifacts;
using AgenticWorkforce.Api.Features.Auth;
using AgenticWorkforce.Api.Features.Catalog;
using AgenticWorkforce.Api.Features.Context;
using AgenticWorkforce.Api.Features.Costs;
using AgenticWorkforce.Api.Features.Decisions;
using AgenticWorkforce.Api.Features.Documents;
using AgenticWorkforce.Api.Features.Events;
using AgenticWorkforce.Api.Features.Executions;
using AgenticWorkforce.Api.Features.HumanInput;
using AgenticWorkforce.Api.Features.Intent;
using AgenticWorkforce.Api.Features.Learnings;
using AgenticWorkforce.Api.Features.Members;
using AgenticWorkforce.Api.Features.Milestones;
using AgenticWorkforce.Api.Features.Projects;
using AgenticWorkforce.Api.Features.Schedules;
using AgenticWorkforce.Api.Features.Sessions;
using AgenticWorkforce.Api.Features.Tasks;
using AgenticWorkforce.Api.Features.Team;
using AgenticWorkforce.Api.Features.Workflows;
using AgenticWorkforce.Api.Features.WorkflowRuns;

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

    public static void MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        ListWorkflows.MapEndpoints(app);
        GetWorkflow.MapEndpoints(app);
        CreateWorkflow.MapEndpoints(app);
        UpdateWorkflow.MapEndpoints(app);
        DeleteWorkflow.MapEndpoints(app);
        ValidateWorkflow.MapEndpoints(app);
        SaveCanvas.MapEndpoints(app);
        RunWorkflow.MapEndpoints(app);
        ListWorkflowRuns.MapEndpoints(app);
    }

    public static void MapWorkflowRunEndpoints(this IEndpointRouteBuilder app)
    {
        ListAllRuns.MapEndpoints(app);
        GetRun.MapEndpoints(app);
    }

    public static void MapScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        ListSchedules.MapEndpoints(app);
        CreateSchedule.MapEndpoints(app);
        UpdateSchedule.MapEndpoints(app);
        DeleteSchedule.MapEndpoints(app);
        ListUpcoming.MapEndpoints(app);
    }

    public static void MapHumanInputEndpoints(this IEndpointRouteBuilder app)
    {
        ListPending.MapEndpoints(app);
        Respond.MapEndpoints(app);
        GetAudit.MapEndpoints(app);
    }

    public static void MapContextEndpoints(this IEndpointRouteBuilder app)
    {
        GetContext.MapEndpoints(app);
        GetContextHistory.MapEndpoints(app);
        AddPrinciple.MapEndpoints(app);
        AddGuardrail.MapEndpoints(app);
        RemovePrinciple.MapEndpoints(app);
        RemoveGuardrail.MapEndpoints(app);
    }

    public static void MapLearningEndpoints(this IEndpointRouteBuilder app)
    {
        ListLearnings.MapEndpoints(app);
        GetLearning.MapEndpoints(app);
        RetractLearning.MapEndpoints(app);
        EditLearning.MapEndpoints(app);
        SupersedeLearning.MapEndpoints(app);
        PromoteLearning.MapEndpoints(app);
        SearchLearnings.MapEndpoints(app);
        FindSimilar.MapEndpoints(app);
    }

    public static void MapMilestoneEndpoints(this IEndpointRouteBuilder app)
    {
        ListMilestones.MapEndpoints(app);
        GetMilestone.MapEndpoints(app);
        CreateMilestone.MapEndpoints(app);
    }

    public static void MapDecisionEndpoints(this IEndpointRouteBuilder app)
    {
        ListDecisions.MapEndpoints(app);
        GetDecision.MapEndpoints(app);
        CreateDecision.MapEndpoints(app);
    }

    public static void MapIntentEndpoints(this IEndpointRouteBuilder app)
    {
        GetIntent.MapEndpoints(app);
        GetIntentHistory.MapEndpoints(app);
        CreateIntent.MapEndpoints(app);
    }

    public static void MapArtifactEndpoints(this IEndpointRouteBuilder app)
    {
        ListArtifacts.MapEndpoints(app);
        GetArtifact.MapEndpoints(app);
        GetArtifactContent.MapEndpoints(app);
        RetractArtifact.MapEndpoints(app);
    }

    public static void MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        UploadDocument.MapEndpoints(app);
        ListDocuments.MapEndpoints(app);
        GetDocument.MapEndpoints(app);
        GetDocumentText.MapEndpoints(app);
        RetractDocument.MapEndpoints(app);
        SearchDocuments.MapEndpoints(app);
    }

    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        ListEvents.MapEndpoints(app);
    }

    public static void MapCostEndpoints(this IEndpointRouteBuilder app)
    {
        GetCostSummary.MapEndpoints(app);
        GetCostTimeline.MapEndpoints(app);
        GetTokenEconomics.MapEndpoints(app);
    }

    public static void MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        DispatchTasks.MapEndpoints(app);
        RunAdHoc.MapEndpoints(app);
        GetExecution.MapEndpoints(app);
    }

    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        ListCatalog.MapEndpoints(app);
        GetCatalogAgent.MapEndpoints(app);
    }

    public static void MapAdminDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        GetHealth.MapEndpoints(app);
        GetOverview.MapEndpoints(app);
        GetAdminCosts.MapEndpoints(app);
        GetAdminCostTimeline.MapEndpoints(app);
    }

    public static void MapAdminCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        AdminListCatalog.MapEndpoints(app);
        AdminCreateAgent.MapEndpoints(app);
        AdminGetAgent.MapEndpoints(app);
        AdminUpdateAgent.MapEndpoints(app);
        AdminDeleteAgent.MapEndpoints(app);
        AdminUpdatePrompt.MapEndpoints(app);
        AdminPromptHistory.MapEndpoints(app);
        AdminEnableAgent.MapEndpoints(app);
        AdminDisableAgent.MapEndpoints(app);
        AdminSeedCatalog.MapEndpoints(app);
    }

    public static void MapAdminKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        ListPlatformLearnings.MapEndpoints(app);
        ListPendingPromotions.MapEndpoints(app);
        ApprovePromotion.MapEndpoints(app);
        RejectPromotion.MapEndpoints(app);
        EditPlatformLearning.MapEndpoints(app);
        RetractPlatformLearning.MapEndpoints(app);
    }
}
