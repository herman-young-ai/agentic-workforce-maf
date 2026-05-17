using AgenticWorkforce.Domain.Interfaces.Services;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Local filesystem document store used in development and integration tests.
/// Files are written under {BasePath}/{containerName}/{path}. Replaced by an
/// Azure Blob Storage implementation in Phase 11.
/// </summary>
internal sealed class LocalFileDocumentStore(string basePath) : IDocumentStore
{
    public async Task<string> UploadAsync(
        string containerName,
        string path,
        Stream content,
        string contentType,
        CancellationToken ct = default)
    {
        var full = ResolvePath(containerName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = File.Create(full);
        await content.CopyToAsync(file, ct);
        return full;
    }

    public Task<Stream> DownloadAsync(string containerName, string path, CancellationToken ct = default)
        => Task.FromResult<Stream>(File.OpenRead(ResolvePath(containerName, path)));

    public Task DeleteAsync(string containerName, string path, CancellationToken ct = default)
    {
        var full = ResolvePath(containerName, path);
        if (File.Exists(full)) File.Delete(full);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string containerName, string path, CancellationToken ct = default)
        => Task.FromResult(File.Exists(ResolvePath(containerName, path)));

    private string ResolvePath(string containerName, string path)
    {
        var safeContainer = Path.GetFileName(containerName);
        var combined = Path.GetFullPath(Path.Combine(basePath, safeContainer, path));
        var root = Path.GetFullPath(basePath);
        if (!combined.StartsWith(root, StringComparison.Ordinal))
            throw new UnauthorizedAccessException($"Path '{path}' escapes the document store root.");
        return combined;
    }
}
