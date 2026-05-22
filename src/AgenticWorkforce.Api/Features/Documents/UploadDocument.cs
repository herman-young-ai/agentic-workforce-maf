using System.Security.Cryptography;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

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
            // 50 MB cap is scoped to THIS route only; JSON endpoints keep the
            // ASP.NET Core default. The handler re-checks file.Length so
            // over-sized payloads surface as a typed ValidationException
            // (HTTP 422) rather than a bare 413.
            .WithMetadata(new RequestSizeLimitAttribute(MaxUploadBytes))
            .WithTags("Documents");

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

        // The client-supplied filename feeds the storage path, so strip any
        // directory component before composing the key. The store layer also
        // rejects path-traversal, but doing it here keeps the stored key
        // free of "../" tokens that would later confuse audit/search.
        var safeFileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            throw new ValidationException("Uploaded file has no usable name.");

        var path = $"documents/{Guid.NewGuid():N}/{safeFileName}";

        // Hash the bytes as they stream into storage so we read the HTTP body
        // exactly once. The hash finalises when CryptoStream is disposed; only
        // then is `sha.Hash` populated. SHA-256 is enough for integrity
        // (Principle 13 audit trail) — not used as a security boundary.
        using var sha = SHA256.Create();
        string contentHash;
        await using (var src = file.OpenReadStream())
        {
            await using (var hashing = new CryptoStream(src, sha, CryptoStreamMode.Read, leaveOpen: false))
            {
                await store.UploadAsync(projectId.ToString("N"), path, hashing, file.ContentType, ct);
            }
            contentHash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }

        var description = form["description"].ToString();
        var docTypeRaw  = form["documentType"].ToString();
        var documentType = Enum.TryParse<DocumentType>(docTypeRaw, ignoreCase: true, out var dt)
            ? dt
            : DocumentType.Reference;

        var document = await repo.AddAsync(new ProjectDocument
        {
            ProjectId        = projectId,
            FileName         = safeFileName,
            ContentType      = file.ContentType,
            FileSizeBytes    = file.Length,
            StorageUrl       = path,
            ContentHash      = contentHash,
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
