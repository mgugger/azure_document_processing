using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.OpenAI;
using Azure.AI.TextAnalytics;
using Azure.AI.Translation.Text;
using Azure.AI.Vision.ImageAnalysis;
using Azure.Identity;
using Azure.Messaging.EventGrid;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Tokens;

namespace InboundProcess
{
    public sealed class BlobCreatedEventData
    {
        public string api { get; set; }
        public string requestId { get; set; }
        public string eTag { get; set; }
        public string contentType { get; set; }
        public long contentLength { get; set; }
        public string blobType { get; set; }
        public string accessTier { get; set; }
        public string url { get; set; }
        public string sequencer { get; set; }
        public StorageDiagnostics storageDiagnostics { get; set; }
    }

    public sealed class StorageDiagnostics
    {
        public string batchId { get; set; }
    }

    public sealed class DocumentIntelligenceQueueMessage
    {
        public string OperationId { get; set; }
        public string BlobUrl { get; set; }
        public WorkflowEnvelope Workflow { get; set; }
    }

    public sealed class WorkflowEnvelope
    {
        public string ReferenceId { get; set; }
        public string BlobPath { get; set; }
        public string CurrentStep { get; set; }
        public List<string> RemainingSteps { get; set; } = new();
        public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public WorkflowFailure Failure { get; set; }
    }

    public sealed class WorkflowFailure
    {
        public string Step { get; set; }
        public string Error { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }

    public sealed class WorkflowAlertMessage
    {
        public string ReferenceId { get; set; }
        public string BlobPath { get; set; }
        public string FailedStep { get; set; }
        public string Error { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }

    public class InboundProcessFunctions
    {
        private readonly ILogger _logger;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly QueueServiceClient _queueServiceClient;
        private readonly IReadOnlyDictionary<string, string> _workflowQueues;
        private readonly string _alertQueueName;
        private readonly JsonSerializerOptions _camelCaseOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private const string ReferenceIdMetadataKey = "reference_id";
        private const string WorkflowStepsMetadataKey = "workflow_steps";
        private const string InputContainerName = "input";
        private const string OutputContainerName = "output";

        public InboundProcessFunctions(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<InboundProcessFunctions>();
            _blobServiceClient = CreateBlobServiceClient();
            _queueServiceClient = CreateQueueServiceClient();
            _workflowQueues = LoadWorkflowQueueMap();
            _alertQueueName = Environment.GetEnvironmentVariable("WORKFLOW_ALERT_QUEUE")?.Trim() ?? "workflow-alerts";
        }

        [Function("BlobCreatedEventGridFunction")]
        public async Task HandleBlobCreatedAsync(
            [EventGridTrigger] EventGridEvent eventGridEvent,
            FunctionContext context)
        {
            var cancellationToken = context.CancellationToken;
            if (!string.Equals(eventGridEvent.EventType, "Microsoft.Storage.BlobCreated", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Ignoring event type {EventType}", eventGridEvent.EventType);
                return;
            }

            var data = eventGridEvent.Data.ToObjectFromJson<BlobCreatedEventData>();
            var url = data?.url;
            if (string.IsNullOrWhiteSpace(url))
            {
                _logger.LogWarning("BlobCreated event missing url payload");
                return;
            }

            var blobUri = new Uri(url);
            var segments = blobUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                _logger.LogWarning("Blob URI missing container or blob path: {Url}", url);
                return;
            }

            var containerName = segments[0];
            if (!string.Equals(containerName, InputContainerName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Blob {Url} not in monitored container", url);
                return;
            }

            var blobName = string.Join('/', segments.Skip(1));
            var blobPath = $"{InputContainerName}/{blobName}";
            _logger.LogInformation("Scheduling workflow for blob {BlobPath}", blobPath);

            var blobClient = _blobServiceClient.GetBlobContainerClient(InputContainerName).GetBlobClient(blobName);

            Dictionary<string, string> metadata;
            try
            {
                metadata = await ReadBlobMetadataAsync(blobClient, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read blob metadata for {BlobPath}", blobPath);
                await PublishAlertAsync(blobPath, null, $"Failed to read blob metadata: {ex.Message}", cancellationToken);
                return;
            }

            var referenceId = ResolveReferenceId(metadata);
            if (string.IsNullOrWhiteSpace(referenceId))
            {
                await PublishAlertAsync(blobPath, null, "Blob missing reference_id metadata", cancellationToken);
                _logger.LogWarning("Blob {BlobPath} missing required reference_id", blobPath);
                return;
            }

            var workflowSteps = ResolveWorkflowSteps(metadata);
            if (workflowSteps.Count == 0)
            {
                await PublishAlertAsync(blobPath, referenceId, "Workflow steps missing", cancellationToken);
                _logger.LogWarning("Blob {BlobPath} missing workflow steps metadata", blobPath);
                return;
            }

            var envelope = new WorkflowEnvelope
            {
                ReferenceId = referenceId,
                BlobPath = blobPath,
                CurrentStep = workflowSteps[0],
                RemainingSteps = workflowSteps.Skip(1).ToList(),
                Metadata = metadata
            };

            await EnqueueWorkflowAsync(envelope, cancellationToken);
        }

        [Function("WorkflowDocIntelligenceStep")]
        public async Task WorkflowDocIntelligenceStep(
            [QueueTrigger("%WORKFLOW_QUEUE_DOCINTELLIGENCE%", Connection = "AzureWebJobsStorage")] string message,
            FunctionContext context) =>
            await ExecuteWorkflowStepAsync("docintelligence", message, context, RunDocumentIntelligenceStepAsync);

        [Function("WorkflowTranslationStep")]
        public async Task WorkflowTranslationStep(
            [QueueTrigger("%WORKFLOW_QUEUE_TRANSLATION%", Connection = "AzureWebJobsStorage")] string message,
            FunctionContext context) =>
            await ExecuteWorkflowStepAsync("translation", message, context, RunTranslationStepAsync);

        [Function("WorkflowPdfImagesStep")]
        public async Task WorkflowPdfImagesStep(
            [QueueTrigger("%WORKFLOW_QUEUE_PDFIMAGES%", Connection = "AzureWebJobsStorage")] string message,
            FunctionContext context) =>
            await ExecuteWorkflowStepAsync("pdfimages", message, context, RunPdfImageExtractionStepAsync);

        [Function("WorkflowPiiStep")]
        public async Task WorkflowPiiStep(
            [QueueTrigger("%WORKFLOW_QUEUE_PII%", Connection = "AzureWebJobsStorage")] string message,
            FunctionContext context) =>
            await ExecuteWorkflowStepAsync("pii", message, context, RunPiiStepAsync);

        [Function("WorkflowAIVisionStep")]
        public async Task WorkflowAIVisionStep(
            [QueueTrigger("%WORKFLOW_QUEUE_AIVISION%", Connection = "AzureWebJobsStorage")] string message,
            FunctionContext context) =>
            await ExecuteWorkflowStepAsync("aivision", message, context, RunAIVisionStepAsync);

        [Function("WorkflowGptVisionStep")]
        public async Task WorkflowGptVisionStep(
            [QueueTrigger("%WORKFLOW_QUEUE_GPTVISION%", Connection = "AzureWebJobsStorage")] string message,
            FunctionContext context) =>
            await ExecuteWorkflowStepAsync("gptvision", message, context, RunGptVisionStepAsync);

        [Function("ProcessDocumentIntelligenceResult")]
        public async Task ProcessDocumentIntelligenceResult(
            [QueueTrigger("%OPERATION_QUEUE_NAME%", Connection = "AzureWebJobsStorage")] string messageJson,
            FunctionContext context)
        {
            var cancellationToken = context.CancellationToken;
            DocumentIntelligenceQueueMessage diMessage;
            try
            {
                diMessage = JsonSerializer.Deserialize<DocumentIntelligenceQueueMessage>(messageJson, _camelCaseOptions)
                    ?? throw new InvalidOperationException("Document Intelligence payload was null");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invalid Document Intelligence queue message");
                return;
            }

            if (string.IsNullOrWhiteSpace(diMessage.OperationId) || string.IsNullOrWhiteSpace(diMessage.BlobUrl) || diMessage.Workflow is null)
            {
                _logger.LogError("Document Intelligence message missing required fields");
                return;
            }

            var endpoint = GetRequiredSetting("DOCUMENT_INTELLIGENCE_ENDPOINT");
            var client = new DocumentAnalysisClient(new Uri(endpoint), new DefaultAzureCredential());
            var operation = new AnalyzeDocumentOperation(diMessage.OperationId, client);
            await operation.UpdateStatusAsync(cancellationToken);

            if (!operation.HasCompleted)
            {
                _logger.LogInformation("Operation {OperationId} still running - requeue", diMessage.OperationId);
                var queueName = Environment.GetEnvironmentVariable("OPERATION_QUEUE_NAME") ?? "documentintelligence-events";
                var queueClient = _queueServiceClient.GetQueueClient(queueName);
                await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                await queueClient.SendMessageAsync(
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(messageJson)),
                    timeToLive: default,
                    visibilityTimeout: TimeSpan.FromSeconds(30),
                    cancellationToken: cancellationToken);
                return;
            }

            if (!operation.HasValue)
            {
                await FailWorkflowAsync(diMessage.Workflow, "docintelligence", "Operation completed without a result", cancellationToken);
                return;
            }

            var result = operation.Value;
            var blobUri = new Uri(diMessage.BlobUrl);
            var originalFileName = blobUri.Segments.LastOrDefault()?.Trim('/') ?? "input";
            var outputTypeFolder = "documentintelligence";
            var outputFileName = $"{originalFileName}_document_intelligence_output.json";
            var outputBlobPath = $"{outputTypeFolder}/{outputFileName}";
            var documentId = BuildDocumentId(outputFileName);

            var containerClient = _blobServiceClient.GetBlobContainerClient(OutputContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            var blobClient = containerClient.GetBlobClient(outputBlobPath);

            var docJson = JsonSerializer.Serialize(new
            {
                document_id = documentId,
                reference_id = diMessage.Workflow.ReferenceId,
                processor = "document intelligence",
                main_content = result.Content,
                message = result,
                original_filename = originalFileName,
                origin_file = originalFileName,
                folderName = outputTypeFolder
            }, new JsonSerializerOptions { WriteIndented = true });

            await UploadJsonAsync(blobClient, docJson, cancellationToken);

            diMessage.Workflow.Metadata["documentintelligence-output"] = outputBlobPath;
            await AdvanceWorkflowAsync(diMessage.Workflow, cancellationToken);
        }

        private async Task ExecuteWorkflowStepAsync(
            string expectedStep,
            string message,
            FunctionContext context,
            Func<WorkflowEnvelope, CancellationToken, Task> handler)
        {
            var cancellationToken = context.CancellationToken;
            WorkflowEnvelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<WorkflowEnvelope>(message, _camelCaseOptions)
                    ?? throw new InvalidOperationException("Workflow envelope payload was null");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse workflow envelope for step {Step}", expectedStep);
                await PublishAlertAsync("unknown", null, $"Invalid workflow envelope for step {expectedStep}", cancellationToken);
                return;
            }

            try
            {
                await handler(envelope, cancellationToken);
            }
            catch (Exception ex)
            {
                await FailWorkflowAsync(envelope, expectedStep, ex.Message, cancellationToken);
                throw;
            }
        }

        private async Task RunDocumentIntelligenceStepAsync(WorkflowEnvelope envelope, CancellationToken cancellationToken)
        {
            var endpoint = GetRequiredSetting("DOCUMENT_INTELLIGENCE_ENDPOINT");
            var client = new DocumentAnalysisClient(new Uri(endpoint), new DefaultAzureCredential());
            var modelId = ResolveDocumentIntelligenceModel(envelope.Metadata);
            var blobUri = BuildBlobUri(envelope.BlobPath);

            // Use stream instead of URI to avoid managed identity propagation issues or SAS requirements
            var blobClient = GetBlobClientForPath(envelope.BlobPath);
            using var stream = await blobClient.OpenReadAsync(cancellationToken: cancellationToken);

            var operation = await client.AnalyzeDocumentAsync(WaitUntil.Started, modelId, stream, cancellationToken: cancellationToken);
            var queueName = Environment.GetEnvironmentVariable("OPERATION_QUEUE_NAME") ?? "documentintelligence-events";
            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            var message = new DocumentIntelligenceQueueMessage
            {
                OperationId = operation.Id,
                BlobUrl = blobUri.ToString(),
                Workflow = envelope
            };

            var payload = JsonSerializer.Serialize(message, _camelCaseOptions);
            await queueClient.SendMessageAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)), cancellationToken: cancellationToken);
            _logger.LogInformation("Document Intelligence started for {ReferenceId} ({OperationId})", envelope.ReferenceId, operation.Id);
        }

        private async Task RunTranslationStepAsync(WorkflowEnvelope envelope, CancellationToken cancellationToken)
        {
            var blobClient = GetBlobClientForPath(envelope.BlobPath);
            string textContent;
            try
            {
                var download = await blobClient.DownloadAsync(cancellationToken: cancellationToken);
                using var reader = new StreamReader(download.Value.Content);
                textContent = await reader.ReadToEndAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read blob content: {ex.Message}");
            }

            var hasMainContent = TryExtractMainContent(textContent, out var mainContent) && !string.IsNullOrWhiteSpace(mainContent);
            var translationInput = hasMainContent ? mainContent : textContent;
            if (string.IsNullOrWhiteSpace(translationInput))
            {
                throw new InvalidOperationException("No content available to translate");
            }

            const int LanguageDetectionSampleLength = 4000; // stay under service limit (5120 text elements)
            var languageEndpoint = GetRequiredSetting("AI_LANGUAGE_ENDPOINT");
            var textAnalyticsClient = new TextAnalyticsClient(new Uri(languageEndpoint), new DefaultAzureCredential());
            string language;
            try
            {
                var sampleLength = Math.Min(LanguageDetectionSampleLength, translationInput.Length);
                var detectionSample = sampleLength == translationInput.Length
                    ? translationInput
                    : translationInput[..sampleLength];
                var response = await textAnalyticsClient.DetectLanguageAsync(detectionSample, cancellationToken: cancellationToken);
                language = response.Value.Iso6391Name;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to detect language: {ex.Message}");
            }

            string translatedText = translationInput;
            bool wasTranslated = false;
            if (!string.Equals(language, "es", StringComparison.OrdinalIgnoreCase))
            {
                const int TranslationChunkSize = 4000; // keep within Translator limits
                var translationEndpoint = GetRequiredSetting("AI_TRANSLATOR_ENDPOINT");
                var translationClient = new TextTranslationClient(new DefaultAzureCredential(), new Uri(translationEndpoint));
                var translatedBuilder = new StringBuilder(translationInput.Length);
                var chunkIndex = 0;

                foreach (var chunk in SplitIntoChunks(translationInput, TranslationChunkSize))
                {
                    try
                    {
                        var translateResult = await translationClient.TranslateAsync(
                            targetLanguage: "es",
                            sourceLanguage: language,
                            content: new[] { chunk },
                            cancellationToken: cancellationToken);

                        var translatedChunk = translateResult.Value
                            .FirstOrDefault()?.Translations
                            .FirstOrDefault()?.Text;

                        if (string.IsNullOrWhiteSpace(translatedChunk))
                        {
                            throw new InvalidOperationException("Translation service returned an empty chunk");
                        }

                        translatedBuilder.Append(translatedChunk);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Failed to translate text chunk {chunkIndex + 1}: {ex.Message}");
                    }

                    chunkIndex++;
                }

                translatedText = translatedBuilder.ToString();
                wasTranslated = true;
            }

            var originalFileName = Path.GetFileName(envelope.BlobPath);
            var outputTypeFolder = wasTranslated ? "translation" : "textpassthrough";
            var outputFileName = $"{originalFileName}_translated.json";
            var outputBlobPath = $"{outputTypeFolder}/{outputFileName}";
            var documentId = BuildDocumentId(outputFileName);

            var containerClient = _blobServiceClient.GetBlobContainerClient(OutputContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            var outputBlobClient = containerClient.GetBlobClient(outputBlobPath);

            var outputJson = JsonSerializer.Serialize(new
            {
                document_id = documentId,
                reference_id = envelope.ReferenceId,
                processor = wasTranslated ? "translation" : "text passthrough",
                main_content = translatedText,
                message = translatedText,
                original_filename = originalFileName,
                origin_file = originalFileName,
                folderName = outputTypeFolder
            }, new JsonSerializerOptions { WriteIndented = true });

            await UploadJsonAsync(outputBlobClient, outputJson, cancellationToken);

            envelope.Metadata[$"{outputTypeFolder}-output"] = outputBlobPath;
            await AdvanceWorkflowAsync(envelope, cancellationToken);
        }

        private async Task RunPdfImageExtractionStepAsync(WorkflowEnvelope envelope, CancellationToken cancellationToken)
        {
            var hasDownstream = envelope.RemainingSteps is { Count: > 0 };
            var nextStep = hasDownstream ? envelope.RemainingSteps![0] : null;
            var remainingAfterNext = hasDownstream ? envelope.RemainingSteps!.Skip(1).ToList() : new List<string>();

            var blobClient = GetBlobClientForPath(envelope.BlobPath);
            if (!blobClient.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("PDF image extraction step requires a PDF input");
            }

            Stream pdfStream;
            try
            {
                var download = await blobClient.DownloadAsync(cancellationToken: cancellationToken);
                pdfStream = new MemoryStream();
                await download.Value.Content.CopyToAsync(pdfStream, cancellationToken);
                pdfStream.Position = 0;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to download PDF for extraction: {ex.Message}");
            }

            List<ExtractedPdfImage> extractedImages;
            try
            {
                extractedImages = ExtractPdfImages(pdfStream);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to parse PDF for image extraction: {ex.Message}");
            }
            finally
            {
                pdfStream.Dispose();
            }

            if (extractedImages.Count == 0)
            {
                await PublishAlertAsync(envelope.BlobPath, envelope.ReferenceId, "PDF image extraction produced no images", cancellationToken);
                return;
            }

            var referenceSegment = BuildDocumentId(envelope.ReferenceId ?? "reference");
            var pdfSegment = BuildDocumentId(Path.GetFileNameWithoutExtension(blobClient.Name));
            var containerClient = _blobServiceClient.GetBlobContainerClient(OutputContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

            foreach (var image in extractedImages)
            {
                var imageBlobName = $"pdfimages/{referenceSegment}/{pdfSegment}-page{image.PageNumber:D4}-img{image.IndexOnPage:D4}{image.FileExtension}";
                var outputBlobClient = containerClient.GetBlobClient(imageBlobName);

                using (var imageStream = new MemoryStream(image.Bytes, writable: false))
                {
                    var uploadOptions = new BlobUploadOptions
                    {
                        HttpHeaders = new BlobHttpHeaders { ContentType = image.ContentType },
                        Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            [ReferenceIdMetadataKey] = envelope.ReferenceId,
                            ["source_pdf"] = envelope.BlobPath,
                            ["source_page"] = image.PageNumber.ToString(CultureInfo.InvariantCulture),
                            ["source_image_index"] = image.IndexOnPage.ToString(CultureInfo.InvariantCulture)
                        }
                    };

                    await outputBlobClient.UploadAsync(imageStream, uploadOptions, cancellationToken);
                }

                var clonedMetadata = CloneMetadata(envelope.Metadata);
                clonedMetadata["source-pdf"] = envelope.BlobPath;
                clonedMetadata["source-pdf-page"] = image.PageNumber.ToString(CultureInfo.InvariantCulture);
                clonedMetadata["source-pdf-image-index"] = image.IndexOnPage.ToString(CultureInfo.InvariantCulture);

                if (hasDownstream && nextStep is not null)
                {
                    var downstreamEnvelope = new WorkflowEnvelope
                    {
                        ReferenceId = envelope.ReferenceId,
                        BlobPath = $"{OutputContainerName}/{imageBlobName}",
                        CurrentStep = nextStep,
                        RemainingSteps = new List<string>(remainingAfterNext),
                        Metadata = clonedMetadata
                    };

                    await EnqueueWorkflowAsync(downstreamEnvelope, cancellationToken);
                }
            }

            _logger.LogInformation(
                hasDownstream && nextStep is not null
                    ? "Extracted {Count} images from {BlobPath} for reference {ReferenceId}; dispatched downstream step {NextStep}"
                    : "Extracted {Count} images from {BlobPath} for reference {ReferenceId}; no downstream steps configured",
                extractedImages.Count,
                envelope.BlobPath,
                envelope.ReferenceId,
                nextStep ?? string.Empty);

            if (hasDownstream)
            {
                await AdvanceWorkflowAsync(envelope, cancellationToken);
            }
        }

        private async Task RunPiiStepAsync(WorkflowEnvelope envelope, CancellationToken cancellationToken)
        {
            if (!envelope.Metadata.TryGetValue("documentintelligence-output", out var docIntPath) || string.IsNullOrWhiteSpace(docIntPath))
            {
                throw new InvalidOperationException("Document Intelligence output path not found in workflow metadata");
            }

            var containerClient = _blobServiceClient.GetBlobContainerClient(OutputContainerName);
            var sourceBlob = containerClient.GetBlobClient(docIntPath);
            string blobContent;
            try
            {
                var download = await sourceBlob.DownloadContentAsync(cancellationToken);
                blobContent = download.Value.Content.ToString();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to read Document Intelligence output '{docIntPath}': {ex.Message}");
            }

            string contentToAnalyze;
            try
            {
                using var doc = JsonDocument.Parse(blobContent);
                if (!doc.RootElement.TryGetProperty("main_content", out var contentProp))
                {
                    throw new InvalidOperationException("Document Intelligence output missing 'main_content' property");
                }

                contentToAnalyze = contentProp.GetString() ?? string.Empty;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                throw new InvalidOperationException($"Invalid Document Intelligence output JSON: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(contentToAnalyze))
            {
                throw new InvalidOperationException("No content found to analyze for PII");
            }

            const int maxChunkSize = 5120;
            var chunks = new List<string>();
            for (var index = 0; index < contentToAnalyze.Length; index += maxChunkSize)
            {
                chunks.Add(contentToAnalyze.Substring(index, Math.Min(maxChunkSize, contentToAnalyze.Length - index)));
            }

            var piiEndpoint = GetRequiredSetting("PII_DETECTION_ENDPOINT");
            var client = new TextAnalyticsClient(new Uri(piiEndpoint), new DefaultAzureCredential());
            var allResults = new List<object>();

            for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
            {
                try
                {
                    var response = await client.RecognizePiiEntitiesAsync(chunks[chunkIndex], language: "en", cancellationToken: cancellationToken);
                    var entities = response.Value;
                    allResults.Add(new
                    {
                        chunk = chunkIndex,
                        entities,
                        redactedText = entities.RedactedText
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error calling PII detection service for chunk {chunkIndex + 1}: {ex.Message}");
                }
            }

            var originalFileName = Path.GetFileName(docIntPath);
            var outputTypeFolder = "pii_detection";
            var outputFileName = $"{originalFileName}_pii_detection_result.json";
            var outputPath = $"{outputTypeFolder}/{outputFileName}";
            var resultBlobClient = containerClient.GetBlobClient(outputPath);
            var documentId = BuildDocumentId(outputFileName);

            var outputJson = JsonSerializer.Serialize(new
            {
                document_id = documentId,
                reference_id = envelope.ReferenceId,
                processor = "pii detection",
                main_content = string.Join("\n", allResults.Select(r => ((dynamic)r).redactedText)),
                message = allResults,
                original_filename = originalFileName,
                origin_file = originalFileName,
                folderName = outputTypeFolder
            }, new JsonSerializerOptions { WriteIndented = true });

            await UploadJsonAsync(resultBlobClient, outputJson, cancellationToken);

            envelope.Metadata["pii-output"] = outputPath;
            await AdvanceWorkflowAsync(envelope, cancellationToken);
        }

        private async Task RunAIVisionStepAsync(WorkflowEnvelope envelope, CancellationToken cancellationToken)
        {
            var endpoint = GetRequiredSetting("AI_VISION_ENDPOINT");
            var client = new ImageAnalysisClient(new Uri(endpoint), new DefaultAzureCredential());
            var blobClient = GetBlobClientForPath(envelope.BlobPath);

            Stream imageStream;
            try
            {
                var download = await blobClient.DownloadAsync(cancellationToken: cancellationToken);
                imageStream = new MemoryStream();
                await download.Value.Content.CopyToAsync(imageStream, cancellationToken);
                imageStream.Position = 0;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to download blob content: {ex.Message}");
            }

            var options = new ImageAnalysisOptions { GenderNeutralCaption = true };
            var result = await client.AnalyzeAsync(
                BinaryData.FromStream(imageStream),
                VisualFeatures.Read | VisualFeatures.Objects | VisualFeatures.Tags,
                options,
                cancellationToken);

            var outputTypeFolder = "aivision";
            var outputFileName = $"{Path.GetFileName(envelope.BlobPath)}_aivision_output.json";
            var outputBlobPath = $"{outputTypeFolder}/{outputFileName}";
            var documentId = BuildDocumentId(outputFileName);
            var containerClient = _blobServiceClient.GetBlobContainerClient(OutputContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            var outputBlobClient = containerClient.GetBlobClient(outputBlobPath);

            var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            var visionOutputJson = JsonSerializer.Serialize(new
            {
                document_id = documentId,
                reference_id = envelope.ReferenceId,
                processor = "aivision",
                main_content = string.Join(",", result.Value.Tags.Values.Select(t => $"{t.Name}:{t.Confidence}")),
                message = JsonDocument.Parse(resultJson).RootElement,
                original_filename = Path.GetFileName(envelope.BlobPath),
                origin_file = Path.GetFileName(envelope.BlobPath),
                folderName = outputTypeFolder
            }, new JsonSerializerOptions { WriteIndented = true });

            await UploadJsonAsync(outputBlobClient, visionOutputJson, cancellationToken);

            envelope.Metadata["aivision-output"] = outputBlobPath;
            await AdvanceWorkflowAsync(envelope, cancellationToken);
        }

        private async Task RunGptVisionStepAsync(WorkflowEnvelope envelope, CancellationToken cancellationToken)
        {
            var openAiEndpoint = GetRequiredSetting("GPT4_VISION_ENDPOINT");
            var openAiDeployment = GetRequiredSetting("GPT4_VISION_DEPLOYMENT_NAME");
            var blobClient = GetBlobClientForPath(envelope.BlobPath);

            Stream imageStream;
            try
            {
                var download = await blobClient.DownloadAsync(cancellationToken: cancellationToken);
                imageStream = new MemoryStream();
                await download.Value.Content.CopyToAsync(imageStream, cancellationToken);
                imageStream.Position = 0;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to download blob content: {ex.Message}");
            }

            var client = new AzureOpenAIClient(new Uri(openAiEndpoint), new DefaultAzureCredential());
            var chatClient = client.GetChatClient(openAiDeployment);

            var imagePart = ChatMessageContentPart.CreateImagePart(BinaryData.FromStream(imageStream), "image/jpeg", ChatImageDetailLevel.Low);
            ChatMessage[] messages =
            [
                new SystemChatMessage("You are a helpful assistant that helps describe images."),
                new UserChatMessage(imagePart, ChatMessageContentPart.CreateTextPart("describe this image, focus on details which are relevant for insurances and claims"))
            ];

            var response = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions { MaxOutputTokenCount = 2048 }, cancellationToken);
            var result = response.Value.Content.Select(t => t.Text).Aggregate((a, b) => a + b);

            var outputTypeFolder = "gptvision";
            var originalFileName = Path.GetFileName(envelope.BlobPath);
            var outputFileName = $"{originalFileName}_gpt4ovision_output.json";
            var outputBlobPath = $"{outputTypeFolder}/{outputFileName}";
            var documentId = BuildDocumentId(outputFileName);

            var containerClient = _blobServiceClient.GetBlobContainerClient(OutputContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            var outputBlobClient = containerClient.GetBlobClient(outputBlobPath);

            var outputJson = JsonSerializer.Serialize(new
            {
                document_id = documentId,
                reference_id = envelope.ReferenceId,
                processor = "gpt4o vision",
                main_content = result,
                message = result,
                original_filename = originalFileName,
                origin_file = originalFileName,
                folderName = outputTypeFolder
            }, new JsonSerializerOptions { WriteIndented = true });

            await UploadJsonAsync(outputBlobClient, outputJson, cancellationToken);

            envelope.Metadata["gptvision-output"] = outputBlobPath;
            await AdvanceWorkflowAsync(envelope, cancellationToken);
        }

        private async Task EnqueueWorkflowAsync(WorkflowEnvelope envelope, CancellationToken cancellationToken)
        {
            if (envelope is null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            if (!_workflowQueues.TryGetValue(envelope.CurrentStep, out var queueName) || string.IsNullOrWhiteSpace(queueName))
            {
                var reason = $"No queue configured for workflow step '{envelope.CurrentStep}'";
                _logger.LogError(reason);
                await PublishAlertAsync(envelope.BlobPath, envelope.ReferenceId, reason, cancellationToken);
                return;
            }

            var queueClient = _queueServiceClient.GetQueueClient(queueName);
            await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            var payload = JsonSerializer.Serialize(envelope, _camelCaseOptions);
            await queueClient.SendMessageAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)), cancellationToken: cancellationToken);
            _logger.LogInformation(
                "Enqueued step {Step} for reference {ReferenceId} ({Remaining} remaining)",
                envelope.CurrentStep,
                envelope.ReferenceId,
                envelope.RemainingSteps.Count);
        }

        private async Task AdvanceWorkflowAsync(WorkflowEnvelope envelope, CancellationToken cancellationToken)
        {
            if (envelope.RemainingSteps is null || envelope.RemainingSteps.Count == 0)
            {
                _logger.LogInformation("Workflow complete for reference {ReferenceId}", envelope.ReferenceId);
                return;
            }

            envelope.CurrentStep = envelope.RemainingSteps[0];
            envelope.RemainingSteps.RemoveAt(0);
            await EnqueueWorkflowAsync(envelope, cancellationToken);
        }

        private async Task FailWorkflowAsync(WorkflowEnvelope envelope, string step, string error, CancellationToken cancellationToken)
        {
            if (envelope is null)
            {
                await PublishAlertAsync("unknown", null, $"Step {step} failed: {error}", cancellationToken);
                return;
            }

            envelope.Failure = new WorkflowFailure
            {
                Step = step,
                Error = error,
                Timestamp = DateTimeOffset.UtcNow
            };

            await PublishAlertAsync(envelope.BlobPath, envelope.ReferenceId, $"Step {step} failed: {error}", cancellationToken);
        }

        private async Task PublishAlertAsync(string blobPath, string referenceId, string reason, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_alertQueueName))
            {
                _logger.LogWarning("Alert queue name empty; cannot publish alert for {BlobPath}", blobPath);
                return;
            }

            try
            {
                var queueClient = _queueServiceClient.GetQueueClient(_alertQueueName);
                await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                var alert = new WorkflowAlertMessage
                {
                    ReferenceId = referenceId ?? "unknown",
                    BlobPath = blobPath,
                    FailedStep = "orchestrator",
                    Error = reason,
                    Timestamp = DateTimeOffset.UtcNow
                };
                var payload = JsonSerializer.Serialize(alert, _camelCaseOptions);
                await queueClient.SendMessageAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)), cancellationToken: cancellationToken);
                _logger.LogInformation("Alert queued for {BlobPath}: {Reason}", blobPath, reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish workflow alert for {BlobPath}", blobPath);
            }
        }

        private async Task<Dictionary<string, string>> ReadBlobMetadataAsync(BlobClient blobClient, CancellationToken cancellationToken)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            foreach (var pair in properties.Value.Metadata)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    metadata[pair.Key] = pair.Value;
                }
            }

            try
            {
                var tags = await blobClient.GetTagsAsync(cancellationToken: cancellationToken);
                foreach (var tag in tags.Value.Tags)
                {
                    metadata[tag.Key] = tag.Value;
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404 || ex.Status == 409)
            {
                _logger.LogDebug(ex, "Blob tags unavailable for {Blob}", blobClient.Name);
            }

            return metadata;
        }

        private static string ResolveReferenceId(IDictionary<string, string> metadata)
        {
            if (metadata is null)
            {
                return null;
            }

            return metadata.TryGetValue(ReferenceIdMetadataKey, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
        }

        private List<string> ResolveWorkflowSteps(IDictionary<string, string> metadata)
        {
            if (metadata != null && metadata.TryGetValue(WorkflowStepsMetadataKey, out var rawSteps))
            {
                var parsed = ParseWorkflowSteps(rawSteps);
                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }

            var defaultWorkflow = Environment.GetEnvironmentVariable("DEFAULT_WORKFLOW");
            return ParseWorkflowSteps(defaultWorkflow);
        }

        private static List<string> ParseWorkflowSteps(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new List<string>();
            }

            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(step => step.Trim().ToLowerInvariant())
                .Where(step => !string.IsNullOrWhiteSpace(step))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private BlobClient GetBlobClientForPath(string blobPath)
        {
            if (string.IsNullOrWhiteSpace(blobPath))
            {
                throw new ArgumentException("Blob path is required", nameof(blobPath));
            }

            var segments = blobPath.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length != 2)
            {
                throw new InvalidOperationException($"Blob path '{blobPath}' is invalid");
            }

            return _blobServiceClient.GetBlobContainerClient(segments[0]).GetBlobClient(segments[1]);
        }

        private Uri BuildBlobUri(string blobPath) => new(_blobServiceClient.Uri, blobPath);

        private static string GetRequiredSetting(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Configuration value '{key}' is required");
            }

            return value.Trim();
        }

        private static string ResolveDocumentIntelligenceModel(IDictionary<string, string> metadata)
        {
            if (metadata != null && metadata.TryGetValue("document-model", out var model) && !string.IsNullOrWhiteSpace(model))
            {
                return model.Trim();
            }

            return "prebuilt-document";
        }

        private static BlobServiceClient CreateBlobServiceClient()
        {
            var blobServiceUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");
            if (!string.IsNullOrWhiteSpace(blobServiceUri))
            {
                return new BlobServiceClient(new Uri(blobServiceUri), new DefaultAzureCredential());
            }

            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return new BlobServiceClient(connectionString);
            }

            throw new InvalidOperationException("Unable to create BlobServiceClient. Configure AzureWebJobsStorage or AzureWebJobsStorage__blobServiceUri");
        }

        private static QueueServiceClient CreateQueueServiceClient()
        {
            var queueServiceUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__queueServiceUri");
            if (!string.IsNullOrWhiteSpace(queueServiceUri))
            {
                return new QueueServiceClient(new Uri(queueServiceUri), new DefaultAzureCredential());
            }

            var connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return new QueueServiceClient(connectionString);
            }

            var blobServiceUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");
            if (!string.IsNullOrWhiteSpace(blobServiceUri))
            {
                var derived = blobServiceUri.Replace(".blob.", ".queue.", StringComparison.OrdinalIgnoreCase);
                return new QueueServiceClient(new Uri(derived), new DefaultAzureCredential());
            }

            throw new InvalidOperationException("Unable to create QueueServiceClient. Configure AzureWebJobsStorage or AzureWebJobsStorage__queueServiceUri");
        }

        private static IReadOnlyDictionary<string, string> LoadWorkflowQueueMap()
        {
            var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["docintelligence"] = "workflow-docintelligence",
                ["pii"] = "workflow-pii",
                ["translation"] = "workflow-translation",
                ["aivision"] = "workflow-aivision",
                ["gptvision"] = "workflow-gptvision",
                ["pdfimages"] = "workflow-pdfimages"
            };

            foreach (var key in defaults.Keys.ToList())
            {
                var envKey = $"WORKFLOW_QUEUE_{key.ToUpperInvariant()}";
                var overrideValue = Environment.GetEnvironmentVariable(envKey);
                if (!string.IsNullOrWhiteSpace(overrideValue))
                {
                    defaults[key] = overrideValue.Trim();
                }
            }

            return defaults;
        }

        private static string BuildDocumentId(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return Guid.NewGuid().ToString("N");
            }

            var builder = new StringBuilder(fileName.Length);
            foreach (var ch in fileName)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '=')
                {
                    builder.Append(ch);
                }
                else
                {
                    builder.Append('-');
                }
            }

            var sanitized = builder.ToString().Trim('-');
            return string.IsNullOrEmpty(sanitized) ? Guid.NewGuid().ToString("N") : sanitized;
        }

        private static async Task UploadJsonAsync(BlobClient blobClient, string payload, CancellationToken cancellationToken)
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
            };
            await blobClient.UploadAsync(ms, uploadOptions, cancellationToken);
        }

        private static Dictionary<string, string> CloneMetadata(IDictionary<string, string> metadata)
        {
            var clone = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (metadata is null)
            {
                return clone;
            }

            foreach (var pair in metadata)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
                {
                    clone[pair.Key] = pair.Value;
                }
            }

            return clone;
        }

        private static List<ExtractedPdfImage> ExtractPdfImages(Stream pdfStream)
        {
            var images = new List<ExtractedPdfImage>();
            using var document = PdfDocument.Open(pdfStream, new ParsingOptions { UseLenientParsing = true });

            foreach (var page in document.GetPages())
            {
                var pdfImages = page.GetImages();
                if (pdfImages is null)
                {
                    continue;
                }

                var indexOnPage = 0;
                foreach (var pdfImage in pdfImages)
                {
                    if (!TryMaterializePdfImage(pdfImage, out var imageBytes, out var contentType, out var fileExtension))
                    {
                        continue;
                    }

                    images.Add(new ExtractedPdfImage(imageBytes, page.Number, indexOnPage++, contentType, fileExtension));
                }
            }

            return images;
        }

        private static bool TryExtractMainContent(string payload, out string mainContent)
        {
            mainContent = string.Empty;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(payload);
                if (document.RootElement.TryGetProperty("main_content", out var mainContentProperty) &&
                    mainContentProperty.ValueKind == JsonValueKind.String)
                {
                    mainContent = mainContentProperty.GetString() ?? string.Empty;
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return false;
        }

        private static IEnumerable<string> SplitIntoChunks(string text, int chunkSize)
        {
            if (chunkSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkSize));
            }

            for (var index = 0; index < text.Length; index += chunkSize)
            {
                yield return text.Substring(index, Math.Min(chunkSize, text.Length - index));
            }
        }

        private static bool TryMaterializePdfImage(IPdfImage pdfImage, out byte[] bytes, out string contentType, out string fileExtension)
        {
            if (pdfImage.TryGetPng(out var pngBytes) && pngBytes is { Length: > 0 })
            {
                bytes = pngBytes;
                contentType = "image/png";
                fileExtension = ".png";
                return true;
            }

            if (pdfImage.TryGetBytes(out var rawBytes) && rawBytes is { Count: > 0 })
            {
                bytes = rawBytes as byte[] ?? rawBytes.ToArray();
                var filterName = ResolvePrimaryFilterName(pdfImage.ImageDictionary);
                switch (filterName)
                {
                    case "DCTDecode":
                    case "DCT":
                        contentType = "image/jpeg";
                        fileExtension = ".jpg";
                        return true;
                    case "JPXDecode":
                        contentType = "image/jpx";
                        fileExtension = ".jpx";
                        return true;
                    case "CCITTFaxDecode":
                    case "CCITTFax":
                        contentType = "image/tiff";
                        fileExtension = ".tiff";
                        return true;
                    default:
                        contentType = "application/octet-stream";
                        fileExtension = ".img";
                        return true;
                }
            }

            if (pdfImage.RawBytes is { Count: > 0 } buffer)
            {
                bytes = buffer as byte[] ?? buffer.ToArray();
                var filterName = ResolvePrimaryFilterName(pdfImage.ImageDictionary);
                switch (filterName)
                {
                    case "DCTDecode" or "DCT":
                        contentType = "image/jpeg";
                        fileExtension = ".jpg";
                        return true;
                    case "JPXDecode":
                        contentType = "image/jpx";
                        fileExtension = ".jpx";
                        return true;
                    case "CCITTFaxDecode" or "CCITTFax":
                        contentType = "image/tiff";
                        fileExtension = ".tiff";
                        return true;
                    default:
                        contentType = "application/octet-stream";
                        fileExtension = ".img";
                        return true;
                }
            }

            bytes = Array.Empty<byte>();
            contentType = string.Empty;
            fileExtension = string.Empty;
            return false;
        }

        private static string ResolvePrimaryFilterName(DictionaryToken dictionary)
        {
            if (dictionary is null)
            {
                return string.Empty;
            }

            if (dictionary.TryGet(NameToken.Filter, out var filterToken))
            {
                return filterToken switch
                {
                    NameToken name => name.Data,
                    ArrayToken array => array.Data.OfType<NameToken>().FirstOrDefault()?.Data ?? string.Empty,
                    _ => string.Empty
                };
            }

            return string.Empty;
        }

        private sealed record ExtractedPdfImage(byte[] Bytes, int PageNumber, int IndexOnPage, string ContentType, string FileExtension);

    }
}