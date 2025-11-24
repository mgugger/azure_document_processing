namespace ProcessingPortal.Services;

public interface IBlobStorageService
{
    Task UploadToInputAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BlobItemModel>> ListInputBlobsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BlobItemModel>> ListOutputBlobsAsync(CancellationToken cancellationToken = default);
    Task<(Stream Content, string ContentType)> OpenOutputBlobAsync(string blobName, CancellationToken cancellationToken = default);
    Task<(Stream Content, string ContentType)> OpenInputBlobAsync(string blobName, CancellationToken cancellationToken = default);
    Task<string> ReadInputBlobTextAsync(string blobName, CancellationToken cancellationToken = default);
    Task<string> ReadOutputBlobTextAsync(string blobName, CancellationToken cancellationToken = default);
    Task DeleteInputBlobAsync(string blobName, CancellationToken cancellationToken = default);
    Task DeleteOutputBlobAsync(string blobName, CancellationToken cancellationToken = default);
}
