using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Api.Features.Documents;

public static class UploadDocument
{
    private const long MaxUploadBytes = 50L * 1024 * 1024; // 50 MB per Principle 19

    public record Response(
        Guid Id,
        string FileName,
        string ContentType,
        long FileSizeBytes,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/projects/{projectId:guid}/documents", HandleAsync)
            .RequireAuthorization(Policies.RequireOperator)
            .DisableAntiforgery()
            .WithTags("Documents");
    // Kestrel-level MaxRequestBodySize is raised to 50 MB in Program.cs.
    // The handler also enforces the same limit on file.Length so over-sized
    // uploads return a typed ValidationException rather than a 413.

    private static async Task<IResult> HandleAsync(
        Guid projectId,
        HttpRequest httpRequest,
        ICurrentUserAccessor userAccessor,
        IProjectAuthorizationService authz,
        IDocumentRepository repo,
        IDocumentStore store,
        CancellationToken ct)
    {
        var user = userAccessor.User;
        await authz.EnsureRoleAsync(user.Id, projectId, ProjectRole.Operator, ct);

        if (!httpRequest.HasFormContentType)
            throw new ValidationException("Request must be multipart/form-data.");

        var form = await httpRequest.ReadFormAsync(ct);
        var file = form.Files.GetFile("file")
            ?? throw new ValidationException("File 'file' is required in form data.");

        if (file.Length == 0)
            throw new ValidationException("Uploaded file is empty.");
        if (file.Length > MaxUploadBytes)
            throw new ValidationException(
                $"File exceeds the {MaxUploadBytes / 1024 / 1024} MB upload limit.");

        var path = $"documents/{Guid.NewGuid():N}/{file.FileName}";
        await using (var stream = file.OpenReadStream())
        {
            await store.UploadAsync(projectId.ToString("N"), path, stream, file.ContentType, ct);
        }

        var description = form["description"].ToString();
        var docTypeRaw  = form["documentType"].ToString();
        var documentType = Enum.TryParse<DocumentType>(docTypeRaw, ignoreCase: true, out var dt)
            ? dt
            : DocumentType.Reference;

        var document = await repo.AddAsync(new ProjectDocument
        {
            ProjectId        = projectId,
            FileName         = file.FileName,
            ContentType      = file.ContentType,
            FileSizeBytes    = file.Length,
            StorageUrl       = path,
            ContentHash      = "",
            ExtractionStatus = ExtractionStatus.Pending,
            DocumentType     = documentType,
            Description      = string.IsNullOrWhiteSpace(description) ? null : description,
            Tags             = [],
            UploadedById     = user.Id
        }, ct);

        return Results.Created(
            $"/api/v1/projects/{projectId}/documents/{document.Id}",
            new Response(document.Id, document.FileName, document.ContentType,
                document.FileSizeBytes, document.CreatedAt));
    }
}
