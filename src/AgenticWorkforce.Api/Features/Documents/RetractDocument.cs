using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;

namespace AgenticWorkforce.Api.Features.Documents;

public static class RetractDocument
{
    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/documents/{documentId:guid}/retract", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Documents");

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        Guid documentId,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IDocumentRepository repo,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        var d = await repo.GetByIdAsync(documentId, ct)
            ?? throw new NotFoundException("Document", documentId);
        if (d.ProjectId != projectId)
            throw new NotFoundException("Document", documentId);

        await repo.RetractAsync(documentId, user.Email, ct);
        return Results.NoContent();
    }
}
