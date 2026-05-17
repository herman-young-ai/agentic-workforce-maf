namespace AgenticWorkforce.Domain.Interfaces.Services;

/// <summary>
/// Object storage for uploaded documents and large extracted text.
/// Implementations wrap Azure Blob Storage (or a local equivalent in dev).
/// </summary>
public interface IDocumentStore
{
    Task<string> UploadAsync(
        string containerName,
        string path,
        Stream content,
        string contentType,
        CancellationToken ct = default);

    Task<Stream> DownloadAsync(
        string containerName,
        string path,
        CancellationToken ct = default);

    Task DeleteAsync(
        string containerName,
        string path,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        string containerName,
        string path,
        CancellationToken ct = default);
}
