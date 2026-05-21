using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Api.Features.Members;

public static class AddMember
{
    public record Request(Guid UserId, ProjectRole Role);

    public record Response(Guid UserId, ProjectRole Role, DateTime JoinedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/members", HandleAsync)
            .RequireAuthorization(Policies.RequireOwner)
            .WithTags("Members")
            ;

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        [FromBody] Request request,
        [FromHeader(Name = "X-Idempotency-Key")] string? idempotencyKey,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IIdempotencyService idempotency,
        AppDbContext db,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Owner, ct);

        if (idempotencyKey is not null)
        {
            var cached = await idempotency.GetCachedResponseAsync<Response>(idempotencyKey, ct);
            if (cached is not null)
                return Results.Created($"/api/v1/projects/{projectId}/members/{cached.UserId}", cached);
        }

        var targetUserExists = await db.Users.AnyAsync(u => u.Id == request.UserId && u.IsActive, ct);
        if (!targetUserExists)
            throw new NotFoundException("User", request.UserId);

        var existingMember = await db.ProjectMembers
            .AnyAsync(m => m.ProjectId == projectId && m.UserId == request.UserId, ct);

        if (existingMember)
            throw new AlreadyExistsException("Member", $"User {request.UserId} is already a member of this project.");

        var member = new ProjectMember
        {
            ProjectId = projectId,
            UserId    = request.UserId,
            Role      = request.Role
        };

        db.ProjectMembers.Add(member);
        await db.SaveChangesAsync(ct);

        var response = new Response(member.UserId, member.Role, member.CreatedAt);

        if (idempotencyKey is not null)
            await idempotency.CacheResponseAsync(idempotencyKey, response, ct);

        return Results.Created($"/api/v1/projects/{projectId}/members/{member.UserId}", response);
    }
}
