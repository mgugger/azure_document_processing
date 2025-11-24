using Azure.Storage.Queues;
using System.Text.Json;

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.AI.Vision.ImageAnalysis;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Azure.AI.OpenAI;
using OpenAI;
using OpenAI.Chat;

namespace InboundProcess
{
    public class BlobCreatedEventData
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

    public class StorageDiagnostics
    {
        public string batchId { get; set; }
    }

        public class OperationQueueMessage
    {
        public string operationId { get; set; }
        public string blobUrl { get; set; }
    }

    public class InboundProcessFunctions
    {
        private readonly ILogger _logger;

        public InboundProcessFunctions(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<InboundProcessFunctions>();
        }

        [Function("BlobCreatedEventGridFunction")]
        public async Task Run(
            [EventGridTrigger] Azure.Messaging.EventGrid.EventGridEvent eventGridEvent,
            FunctionContext context)
        {
            _logger.LogInformation($"EventGrid event received: {eventGridEvent.EventType}\nRaw Data: {eventGridEvent.Data}");

            if (eventGridEvent.EventType == "Microsoft.Storage.BlobCreated")
            {
                var data = eventGridEvent.Data.ToObjectFromJson<BlobCreatedEventData>();
                string url = data?.url;
                _logger.LogInformation($"Blob created at URL: {url}");

                if (!string.IsNullOrEmpty(url) && url.Contains("/input/"))
                {
                    _logger.LogInformation($"New file uploaded to 'input' container: {url}");
                    string blob_endpoint = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");

                    var blobUri = new Uri(url);
                    var segments = blobUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                    var containerName = segments[0];
                    var blobName = string.Join('/', segments, 1, segments.Length - 1);
                    var blobServiceClient = new Azure.Storage.Blobs.BlobServiceClient(new Uri(blob_endpoint), new DefaultAzureCredential());
                    var blobClient = blobServiceClient.GetBlobContainerClient(containerName).GetBlobClient(blobName);

                    if (blobName.EndsWith(".txt"))
                    {
                        // for text
                        await CheckAndTranslateIfNecessary(url, blobClient);
                    }
                    if (blobName.EndsWith(".pdf"))
                    {
                        await RunDocumentIntelligence(url, blobClient);
                    }
                    else if (blobName.StartsWith("email_"))
                    {
                        await RunAIVision(url, blobClient);
                        await RunGPTVision(url, blobClient);
                    }
                }
                else
                {
                    _logger.LogInformation($"Blob is not in the 'input' container, ignoring: {url}");
                }
            }
            else
            {
                _logger.LogInformation($"Event type is not BlobCreated, ignoring.");
            }
        }

        private async Task RunGPTVision(string url, BlobClient blobClient)
        {
            string openAiEndpoint = Environment.GetEnvironmentVariable("GPT4_VISION_ENDPOINT");
            string openAiDeployment = Environment.GetEnvironmentVariable("GPT4_VISION_DEPLOYMENT_NAME");
            if (string.IsNullOrEmpty(openAiEndpoint) || string.IsNullOrEmpty(openAiDeployment))
            {
                _logger.LogError("GPT4_VISION_ENDPOINT or GPT4_VISION_DEPLOYMENT_NAME environment variable is not set.");
                return;
            }

            try
            {
                AzureOpenAIClient client = new(
                    new Uri(openAiEndpoint),
                    new DefaultAzureCredential());
                ChatClient chatClient = client.GetChatClient(openAiDeployment);

                ChatMessageContentPart imagePart;
                Stream imageStream;
                try
                {
                    var download = await blobClient.DownloadAsync();
                    imageStream = new MemoryStream();
                    await download.Value.Content.CopyToAsync(imageStream);
                    imageStream.Position = 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to download image blob: {ex.Message}");
                    return;
                }

                imagePart = ChatMessageContentPart.CreateImagePart(
                    BinaryData.FromStream(imageStream), "image/jpeg", ChatImageDetailLevel.Low
                );

                ChatMessage[] messages =
                [
                    new SystemChatMessage("You are a helpful assistant that helps describe images."),
                    new UserChatMessage(imagePart, ChatMessageContentPart.CreateTextPart("describe this image, focus on details which are relevant for insurances and claims"))
                ];

                ChatCompletionOptions options = new()
                {
                    MaxOutputTokenCount = 2048,
                };

                var response = await chatClient.CompleteChatAsync(messages, options);

                var result = response.Value.Content.Select(t => t.Text).Aggregate((a, b) => a + b);

                // Parse blob path for output (same logic as RunAIVision)
                var blobUri = new Uri(url);
                var segments = blobUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                string outputTypeFolder = "gptvision";
                string originalFileName = segments.Length > 0 ? segments.Last() : "input";
                string outputFileName = $"{originalFileName}_gpt4ovision_output.json";
                string outputBlobPath = $"{outputTypeFolder}/{outputFileName}";

                string outputContainerName = "output";
                string blob_endpoint = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");
                if (string.IsNullOrEmpty(blob_endpoint))
                {
                    _logger.LogError("AzureWebJobsStorage__blobServiceUri environment variable is not set.");
                    return;
                }

                var blobServiceClient = new BlobServiceClient(new Uri(blob_endpoint), new DefaultAzureCredential());
                var containerClient = blobServiceClient.GetBlobContainerClient(outputContainerName);
                await containerClient.CreateIfNotExistsAsync();
                var outputBlobClient = containerClient.GetBlobClient(outputBlobPath);

                var outputJson = System.Text.Json.JsonSerializer.Serialize(new {
                    processor = "gpt4o vision",
                    main_content = result,
                    message = result,
                    original_filename = originalFileName,
                    origin_file = originalFileName,
                    folderName = outputTypeFolder
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(outputJson));
                var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
                {
                    HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = "application/json" }
                };
                await outputBlobClient.UploadAsync(ms, uploadOptions);

                _logger.LogInformation($"Stored GPT-4o Vision result in output container at path: {outputBlobPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in RunGPTVision: {ex.Message}");
            }
        }

        private async Task CheckAndTranslateIfNecessary(string url, BlobClient blobClient)
        {
            // Download the .txt file content
            string textContent;
            try
            {
                var download = await blobClient.DownloadAsync();
                using (var reader = new StreamReader(download.Value.Content))
                {
                    textContent = await reader.ReadToEndAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to download blob content: {ex.Message}");
                return;
            }

            // Preprocess: split by \n and remove the first element

            var lines = textContent.Split('\n');
            if (lines.Length > 1)
            {
                textContent = string.Join("\n", lines.Skip(1));
            }

            // Detect language using Azure.AI.TextAnalytics
            string language = "en";
            try
            {
                var endpoint = Environment.GetEnvironmentVariable("AI_LANGUAGE_ENDPOINT");
                if (string.IsNullOrEmpty(endpoint))
                {
                    _logger.LogError("AI_LANGUAGE_ENDPOINT environment variable is not set.");
                    return;
                }
                var credential = new DefaultAzureCredential();
                var textAnalyticsClient = new Azure.AI.TextAnalytics.TextAnalyticsClient(new Uri(endpoint), credential);
                var response = await textAnalyticsClient.DetectLanguageAsync(textContent);
                language = response.Value.Iso6391Name;
                _logger.LogInformation($"Detected language: {language} for text: {textContent}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to detect language: {ex.Message}");
                return;
            }

            string translatedText = textContent;
            bool wasTranslated = false;
            if (language != "en")
            {
                try
                {
                    var translationEndpoint = Environment.GetEnvironmentVariable("AI_TRANSLATOR_ENDPOINT");
                    if (string.IsNullOrEmpty(translationEndpoint))
                    {
                        _logger.LogError("AI_TRANSLATOR_ENDPOINT environment variable is not set.");
                        return;
                    }
                    var translationClient = new Azure.AI.Translation.Text.TextTranslationClient(new DefaultAzureCredential(), new Uri(translationEndpoint));
                    var translateResult = await translationClient.TranslateAsync(
                        targetLanguage: "en",
                        sourceLanguage: language,
                        content: new[] { textContent }
                    );
                    translatedText = string.Join("\n", translateResult.Value[0].Translations.Select(t => t.Text));
                    wasTranslated = true;
                    _logger.LogInformation($"Translated text from {language} to en.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to translate text: {ex.Message}");
                    return;
                }
            }

            try
            {
                var blobUri = new Uri(url);
                var segments = blobUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                // segments[0] = container, segments[1..n-1] = folders, segments[^1] = filename
                string originalFileName = segments.Length > 0 ? segments[^1] : "input";

                string outputTypeFolder = wasTranslated ? "translation" : "textpassthrough";
                string outputFileName = $"{originalFileName}_translated.json";
                string outputBlobPath = $"{outputTypeFolder}/{outputFileName}";

                string outputContainerName = "output";
                string blob_endpoint = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");
                if (string.IsNullOrEmpty(blob_endpoint))
                {
                    _logger.LogError("AzureWebJobsStorage__blobServiceUri environment variable is not set.");
                    return;
                }

                var blobServiceClient = new BlobServiceClient(new Uri(blob_endpoint), new DefaultAzureCredential());
                var containerClient = blobServiceClient.GetBlobContainerClient(outputContainerName);
                await containerClient.CreateIfNotExistsAsync();
                var outputBlobClient = containerClient.GetBlobClient(outputBlobPath);


                var outputJson = System.Text.Json.JsonSerializer.Serialize(new {
                    processor = wasTranslated ? "translation" : "text passthrough",
                    main_content = translatedText,
                    message = translatedText,
                    original_filename = originalFileName,
                    origin_file = originalFileName,
                    folderName = outputTypeFolder
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });


                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(outputJson));
                var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
                {
                    HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = "application/json" }
                };
                await outputBlobClient.UploadAsync(ms, uploadOptions);

                _logger.LogInformation($"Stored translated text in output container at path: {outputBlobPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to store translated text: {ex.Message}");
            }
        }

        private async Task RunAIVision(string url, BlobClient blobClient)
        {
            string aiVisionEndpoint = Environment.GetEnvironmentVariable("AI_VISION_ENDPOINT");
            if (string.IsNullOrEmpty(aiVisionEndpoint))
            {
                _logger.LogError("AI_VISION_ENDPOINT environment variable is not set.");
                return;
            }

            try
            {
                var credential = new DefaultAzureCredential();
                var client = new ImageAnalysisClient(new Uri(aiVisionEndpoint), credential);

                // Download the image as bytes from blob storage
                Stream imageStream;
                try
                {
                    var download = await blobClient.DownloadAsync();
                    imageStream = new MemoryStream();
                    await download.Value.Content.CopyToAsync(imageStream);
                    imageStream.Position = 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to download image blob: {ex.Message}");
                    return;
                }

                var options = new ImageAnalysisOptions { GenderNeutralCaption = true };
                var result = await client.AnalyzeAsync(
                    BinaryData.FromStream(imageStream),
                    /*VisualFeatures.DenseCaptions |*/ VisualFeatures.Read | VisualFeatures.Objects | VisualFeatures.Tags,
                    options);

                var resultJson = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                // Parse blob path for output
                var blobUri = new Uri(url);
                var segments = blobUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                // segments[0] = container, segments[1..n-1] = folders, segments[^1] = filename

                string outputTypeFolder = "aivision";
                string outputFileName = $"{segments.Last()}_aivision_output.json";
                string outputBlobPath = $"{outputTypeFolder}/{outputFileName}";

                string outputContainerName = "output";
                string blob_endpoint = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");
                if (string.IsNullOrEmpty(blob_endpoint))
                {
                    _logger.LogError("AzureWebJobsStorage__blobServiceUri environment variable is not set.");
                    return;
                }

                var blobServiceClient = new BlobServiceClient(new Uri(blob_endpoint), new DefaultAzureCredential());
                var containerClient = blobServiceClient.GetBlobContainerClient(outputContainerName);
                await containerClient.CreateIfNotExistsAsync();
                var outputBlobClient = containerClient.GetBlobClient(outputBlobPath);

                var visionOutputJson = System.Text.Json.JsonSerializer.Serialize(new {
                    processor = "aivision",
                    main_content = string.Join(",", result.Value.Tags.Values.Select(t => $"{t.Name}:{t.Confidence}")),
                    message = System.Text.Json.JsonDocument.Parse(resultJson).RootElement,
                    original_filename = segments.Last(),
                    origin_file = segments.Last(),
                    folderName = outputTypeFolder
                }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(visionOutputJson));
                var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
                {
                    HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = "application/json" }
                };
                await outputBlobClient.UploadAsync(ms, uploadOptions);

                _logger.LogInformation($"Stored AI Vision result in output container at path: {outputBlobPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in RunAIVision: {ex.Message}");
            }
        }

        private async Task RunDocumentIntelligence(string url, BlobClient blobClient)
        {
            string modelId = "prebuilt-document";
            try
            {
                var tags = await blobClient.GetTagsAsync();
                if (tags.Value.Tags.TryGetValue("document-model", out var tagModel) && !string.IsNullOrWhiteSpace(tagModel))
                {
                    modelId = tagModel;
                    _logger.LogInformation($"Using model from blob index tag: {modelId}");
                }
                else
                {
                    _logger.LogInformation("No 'document-model' blob index tag found, using default 'prebuilt-document'.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not read blob index tags, using default model. Error: {ex.Message}");
            }

            var client = new DocumentAnalysisClient(new Uri(Environment.GetEnvironmentVariable("DOCUMENT_INTELLIGENCE_ENDPOINT")), new DefaultAzureCredential());
            try
            {
                var operation = await client.AnalyzeDocumentFromUriAsync(Azure.WaitUntil.Started, modelId, new Uri(url));
                string operationId = operation.Id;
                _logger.LogInformation($"Document Intelligence operation started. OperationId: {operationId}");

                // Enqueue operationId and blob url to Azure Storage queue for later processing
                string queueUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__queueServiceUri");
                string queueName = Environment.GetEnvironmentVariable("OPERATION_QUEUE_NAME") ?? "documentintelligence-events";
                var queueClient = new QueueClient(new Uri($"{queueUri}{queueName}"), new DefaultAzureCredential());
                await queueClient.CreateIfNotExistsAsync();

                var message = new { operationId, blobUrl = url };
                string base64Message = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message)));
                await queueClient.SendMessageAsync(base64Message, visibilityTimeout: TimeSpan.FromSeconds(10));
                _logger.LogInformation($"Enqueued operationId and blob url to queue: {operationId} {queueName}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error starting Document Intelligence operation or enqueuing: {ex.Message}");
            }
        }

        [Function("ProcessDocumentIntelligenceResult")]
        public async Task ProcessDocumentIntelligenceResult(
            [QueueTrigger("documentintelligence-events")] string messageJson,
            FunctionContext context)
        {
            _logger.LogInformation($"message: {messageJson}");
            try
            {
                var message = JsonSerializer.Deserialize<OperationQueueMessage>(messageJson);
                if (message == null || string.IsNullOrEmpty(message.operationId) || string.IsNullOrEmpty(message.blobUrl))
                {
                    _logger.LogError("Invalid queue message format.");
                    return;
                }

                string endpoint = Environment.GetEnvironmentVariable("DOCUMENT_INTELLIGENCE_ENDPOINT");
                var client = new DocumentAnalysisClient(new Uri(endpoint), new DefaultAzureCredential());

                // Poll the operation status using AnalyzeDocumentOperation
                var operation = new AnalyzeDocumentOperation(message.operationId, client);
                await operation.UpdateStatusAsync();

                if (!operation.HasCompleted)
                {
                    _logger.LogInformation($"Operation {message.operationId} not ready, re-queueing with 30s delay.");
                    // Re-queue with 30s delay
                    string queueUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__queueServiceUri");
                    string queueName = Environment.GetEnvironmentVariable("OPERATION_QUEUE_NAME") ?? "documentintelligence-events";
                    QueueClient queueClient = new QueueClient(new Uri($"{queueUri}{queueName}"), new DefaultAzureCredential());
                    await queueClient.CreateIfNotExistsAsync();
                    await queueClient.SendMessageAsync(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(messageJson)), null, TimeSpan.FromSeconds(30));
                    return;
                }

                if (operation.HasValue)
                {
                    var result = operation.Value;
                    _logger.LogInformation($"Document Intelligence operation {message.operationId} completed. Result: {result.ModelId}");

                    // Store result as JSON in output container, in a folder named after the analyzed file
                    try
                    {
                        // Parse the blob name from the URL

                        var blobUri = new Uri(message.blobUrl);
                        string[] segments = blobUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        // segments[0] = container, segments[1..n-1] = folders, segments[^1] = filename

                        string originalFileName = segments.Length > 0 ? segments[^1] : "input";

                        string outputTypeFolder = "documentintelligence";
                        string outputFileName = $"{originalFileName}_document_intelligence_output.json";
                        string outputBlobPath = $"{outputTypeFolder}/{outputFileName}";

                        // Get output container connection string and name
                        string outputContainerName = "output";
                        string blob_endpoint = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");
                        if (string.IsNullOrEmpty(blob_endpoint))
                        {
                            _logger.LogError("AzureWebJobsStorage environment variable is not set.");
                            return;
                        }

                        // Create blob client and upload JSON
                        var blobServiceClient = new BlobServiceClient(new Uri(blob_endpoint), new DefaultAzureCredential());
                        var containerClient = blobServiceClient.GetBlobContainerClient(outputContainerName);
                        await containerClient.CreateIfNotExistsAsync();
                        var blobClient = containerClient.GetBlobClient(outputBlobPath);

                        // Serialize result to JSON

                        // Add origin_file and folderName to the output JSON
                        var docIntelligenceJson = JsonSerializer.Serialize(new {
                            processor = "document intelligence",
                            main_content = result.Content,
                            message = result,
                            original_filename = originalFileName,
                            origin_file = originalFileName,
                            folderName = outputTypeFolder
                        }, new JsonSerializerOptions { WriteIndented = true });

                        using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(docIntelligenceJson)))
                        {
                            var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
                            {
                                HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = "application/json" }
                            };
                            await blobClient.UploadAsync(ms, uploadOptions);
                        }
                        _logger.LogInformation($"Stored result in output container at path: {outputBlobPath}");

                        // Send event to 'pii-in' storage queue for further PII processing
                        string piiQueueName = "pii-in";
                        var piiQueueUri = Environment.GetEnvironmentVariable("AzureWebJobsStorage__queueServiceUri");
                        if (string.IsNullOrEmpty(piiQueueUri))
                        {
                            _logger.LogError("AzureWebJobsStorage__queueServiceUri environment variable is not set.");
                        }
                        else
                        {
                            var piiQueueClient = new QueueClient(new Uri($"{piiQueueUri}{piiQueueName}"), new DefaultAzureCredential());
                            await piiQueueClient.CreateIfNotExistsAsync();
                            var piiEvent = new { outputPath = outputBlobPath };
                            string piiMessage = JsonSerializer.Serialize(piiEvent);
                            string piiBase64Message = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(piiMessage));
                            await piiQueueClient.SendMessageAsync(piiBase64Message);
                            _logger.LogInformation($"Sent event to 'pii-in' queue with outputPath: {outputBlobPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to store result in output container: {ex.Message}");
                    }
                }
                else
                {
                    _logger.LogError($"Operation {message.operationId} completed but no result found.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing Document Intelligence result: {ex.Message}");
                throw;
            }
        }
    }
}