using System;
using System.Collections.Generic;
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
    private readonly HashSet<string> _validatedContainers = new(StringComparer.OrdinalIgnoreCase);
        private const string WorkflowVersion = "1";

    public BlobStorageService(BlobServiceClient blobServiceClient, IOptions<StorageOptions> options)
    {
        _blobServiceClient = blobServiceClient;
        _options = options.Value;
    }

        public async Task UploadToInputAsync(
            string fileName,
            Stream content,
            string contentType,
            string referenceId,
            IReadOnlyList<string> workflowSteps,
            CancellationToken cancellationToken = default)
    {
            if (content is null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var trimmedReference = referenceId?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedReference))
            {
                throw new ArgumentException("Reference ID is required", nameof(referenceId));
            }

            if (workflowSteps is null || workflowSteps.Count == 0)
            {
                throw new ArgumentException("At least one workflow step is required", nameof(workflowSteps));
            }

            var normalizedSteps = workflowSteps
                .Select(NormalizeStep)
                .Where(step => !string.IsNullOrWhiteSpace(step))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedSteps.Count == 0)
            {
                throw new ArgumentException("Workflow steps are invalid", nameof(workflowSteps));
            }

        await EnsureContainerExistsAsync(_options.InputContainer, cancellationToken);
        var sanitizedName = string.IsNullOrWhiteSpace(fileName) ? $"upload-{Guid.NewGuid():N}" : fileName;
        var uniqueName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{sanitizedName}";
        var containerClient = _blobServiceClient.GetBlobContainerClient(_options.InputContainer);
        var blobClient = containerClient.GetBlobClient(uniqueName);
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["reference_id"] = trimmedReference,
                ["workflow_steps"] = string.Join(',', normalizedSteps),
                ["workflow_version"] = WorkflowVersion
            };

            await blobClient.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType },
                Metadata = metadata
            }, cancellationToken);
    }

    public async Task<IReadOnlyList<BlobItemModel>> ListInputBlobsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(_options.InputContainer, cancellationToken);
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
        await EnsureContainerExistsAsync(_options.OutputContainer, cancellationToken);
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
        await EnsureContainerExistsAsync(_options.InputContainer, cancellationToken);
        var containerClient = _blobServiceClient.GetBlobContainerClient(_options.InputContainer);
        var blobClient = containerClient.GetBlobClient(blobName);
        var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
        var contentType = response.Value.Details.ContentType ?? "application/octet-stream";
        return (response.Value.Content, contentType);
    }

    public async Task<(Stream Content, string ContentType)> OpenOutputBlobAsync(string blobName, CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(_options.OutputContainer, cancellationToken);
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

        await EnsureContainerExistsAsync(_options.InputContainer, cancellationToken);
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

        await EnsureContainerExistsAsync(_options.OutputContainer, cancellationToken);
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

        await EnsureContainerExistsAsync(containerName, cancellationToken);
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    private async Task EnsureContainerExistsAsync(string containerName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new ArgumentException("Container name is required", nameof(containerName));
        }

        if (_validatedContainers.Contains(containerName))
        {
            return;
        }

        await _containerInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_validatedContainers.Contains(containerName))
            {
                return;
            }

            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var existsResponse = await containerClient.ExistsAsync(cancellationToken);
            if (!existsResponse.Value)
            {
                throw new InvalidOperationException($"Blob container '{containerName}' does not exist. Create it manually to proceed.");
            }

            _validatedContainers.Add(containerName);
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

    private static string NormalizeStep(string step)
    {
        return string.IsNullOrWhiteSpace(step)
            ? string.Empty
            : step.Trim().ToLowerInvariant();
    }
}
