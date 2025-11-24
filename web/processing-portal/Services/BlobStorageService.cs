using System;
using System.Linq;
using System.Threading;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace ProcessingPortal.Services;

public sealed class BlobStorageService : IBlobStorageService, IDisposable
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly StorageOptions _options;
    private readonly SemaphoreSlim _containerInitializationLock = new(1, 1);
    private bool _containersInitialized;

    public BlobStorageService(BlobServiceClient blobServiceClient, IOptions<StorageOptions> options)
    {
        _blobServiceClient = blobServiceClient;
        _options = options.Value;
    }

    public async Task UploadToInputAsync(string fileName, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureContainersAsync(cancellationToken);
        var sanitizedName = string.IsNullOrWhiteSpace(fileName) ? $"upload-{Guid.NewGuid():N}" : fileName;
        var uniqueName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{sanitizedName}";
        var containerClient = _blobServiceClient.GetBlobContainerClient(_options.InputContainer);
        var blobClient = containerClient.GetBlobClient(uniqueName);
        await blobClient.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<BlobItemModel>> ListInputBlobsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureContainersAsync(cancellationToken);
        var containerClient = _blobServiceClient.GetBlobContainerClient(_options.InputContainer);
        var results = new List<BlobItemModel>();
        await foreach (var blob in containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            results.Add(new BlobItemModel(
                blob.Name,
                blob.Properties.ContentLength,
                blob.Properties.LastModified,
                blob.Properties.ContentType
            ));
        }

        return results
            .OrderByDescending(b => b.LastModified)
            .ThenByDescending(b => b.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<BlobItemModel>> ListOutputBlobsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureContainersAsync(cancellationToken);
        var containerClient = _blobServiceClient.GetBlobContainerClient(_options.OutputContainer);
        var results = new List<BlobItemModel>();
        await foreach (var blob in containerClient.GetBlobsAsync(cancellationToken: cancellationToken))
        {
            results.Add(new BlobItemModel(
                blob.Name,
                blob.Properties.ContentLength,
                blob.Properties.LastModified,
                blob.Properties.ContentType
            ));
        }

        return results
            .OrderByDescending(b => b.LastModified)
            .ThenByDescending(b => b.Name)
            .ToList();
    }

    public async Task<(Stream Content, string ContentType)> OpenInputBlobAsync(string blobName, CancellationToken cancellationToken = default)
    {
        await EnsureContainersAsync(cancellationToken);
        var containerClient = _blobServiceClient.GetBlobContainerClient(_options.InputContainer);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        var contentType = response.Value.Details.ContentType ?? "application/octet-stream";
        return (response.Value.Content, contentType);
    }

    public async Task<(Stream Content, string ContentType)> OpenOutputBlobAsync(string blobName, CancellationToken cancellationToken = default)
    {
        await EnsureContainersAsync(cancellationToken);
        var containerClient = _blobServiceClient.GetBlobContainerClient(_options.OutputContainer);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        var contentType = response.Value.Details.ContentType ?? "application/octet-stream";
        return (response.Value.Content, contentType);
    }

    public async Task<string> ReadInputBlobTextAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new ArgumentException("Blob name is required", nameof(blobName));
        }

        await EnsureContainersAsync(cancellationToken);
        var containerClient = _blobServiceClient.GetBlobContainerClient(_options.InputContainer);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToString();
    }

    public async Task<string> ReadOutputBlobTextAsync(string blobName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new ArgumentException("Blob name is required", nameof(blobName));
        }

        await EnsureContainersAsync(cancellationToken);
        var containerClient = _blobServiceClient.GetBlobContainerClient(_options.OutputContainer);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToString();
    }

    public async Task DeleteInputBlobAsync(string blobName, CancellationToken cancellationToken = default)
    {
        await DeleteBlobAsync(_options.InputContainer, blobName, cancellationToken);
    }

    public async Task DeleteOutputBlobAsync(string blobName, CancellationToken cancellationToken = default)
    {
        await DeleteBlobAsync(_options.OutputContainer, blobName, cancellationToken);
    }

    private async Task DeleteBlobAsync(string containerName, string blobName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            throw new ArgumentException("Blob name is required", nameof(blobName));
        }

        await EnsureContainersAsync(cancellationToken);
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    private async Task EnsureContainersAsync(CancellationToken cancellationToken)
    {
        if (_containersInitialized)
        {
            return;
        }

        await _containerInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_containersInitialized)
            {
                return;
            }

            await _blobServiceClient.GetBlobContainerClient(_options.InputContainer)
                .CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            await _blobServiceClient.GetBlobContainerClient(_options.OutputContainer)
                .CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _containersInitialized = true;
        }
        finally
        {
            _containerInitializationLock.Release();
        }
    }

    public void Dispose()
    {
        _containerInitializationLock.Dispose();
    }
}
