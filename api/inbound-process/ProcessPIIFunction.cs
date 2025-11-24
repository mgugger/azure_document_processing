using Azure.Storage.Queues;
using System.Text.Json;
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Azure.Identity;
using System.IO;
using Azure.AI.TextAnalytics;

namespace InboundProcess
{
    public class PiiQueueMessage
    {
        public string outputPath { get; set; }
    }

    public class ProcessPIIFunction
    {
        private readonly ILogger _logger;

        public ProcessPIIFunction(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ProcessPIIFunction>();
        }

        [Function("ProcessPIIQueueEvent")]
        public async Task Run(
            [QueueTrigger("pii-in")] string messageJson,
            FunctionContext context)
        {
            _logger.LogInformation($"Received message from pii-in queue: {messageJson}");
            try
            {
                var message = JsonSerializer.Deserialize<PiiQueueMessage>(messageJson);
                if (message == null || string.IsNullOrEmpty(message.outputPath))
                {
                    _logger.LogError("Invalid queue message format.");
                    return;
                }

                // Read the content from the output blob
                string blob_endpoint = Environment.GetEnvironmentVariable("AzureWebJobsStorage__blobServiceUri");
                if (string.IsNullOrEmpty(blob_endpoint))
                {
                    _logger.LogError("AzureWebJobsStorage__blobServiceUri environment variable is not set.");
                    return;
                }
                var blobServiceClient = new BlobServiceClient(new Uri(blob_endpoint), new DefaultAzureCredential());
                var containerClient = blobServiceClient.GetBlobContainerClient("output");
                var blobClient = containerClient.GetBlobClient(message.outputPath);

                string blobContent;
                try
                {
                    var download = await blobClient.DownloadAsync();
                    using (var reader = new StreamReader(download.Value.Content))
                    {
                        blobContent = await reader.ReadToEndAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to read blob content: {ex.Message}");
                    return;
                }

                // Extract only the 'content' property from the Document Intelligence result
                string contentToAnalyze = null;
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(blobContent))
                    {
                        if (doc.RootElement.TryGetProperty("Content", out var contentProp))
                        {
                            contentToAnalyze = contentProp.GetString();
                        }
                        else
                        {
                            _logger.LogError("No 'Content' property found in the document intelligence result.");
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to parse blob content as JSON: {ex.Message}");
                    return;
                }

                if (string.IsNullOrEmpty(contentToAnalyze))
                {
                    _logger.LogError("Content to analyze is empty.");
                    return;
                }

                // Split content into chunks of <=5120 text elements (characters)
                const int maxChunkSize = 5120;
                var chunks = new System.Collections.Generic.List<string>();
                for (int i = 0; i < contentToAnalyze.Length; i += maxChunkSize)
                {
                    int length = Math.Min(maxChunkSize, contentToAnalyze.Length - i);
                    chunks.Add(contentToAnalyze.Substring(i, length));
                }

                string piiEndpoint = Environment.GetEnvironmentVariable("PII_DETECTION_ENDPOINT");
                if (string.IsNullOrEmpty(piiEndpoint))
                {
                    _logger.LogError("PII_DETECTION_ENDPOINT environment variable is not set.");
                    return;
                }

                var credential = new Azure.Identity.DefaultAzureCredential();
                var client = new Azure.AI.TextAnalytics.TextAnalyticsClient(new Uri(piiEndpoint), credential);
                var allResults = new System.Collections.Generic.List<object>();
                for (int chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
                {
                    var chunk = chunks[chunkIndex];
                    try
                    {
                        var response = await client.RecognizePiiEntitiesAsync(chunk, language: "en");
                        var entities = response.Value;
                        allResults.Add(new {
                            redactedText = entities.RedactedText,
                            entities = entities
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error calling PII detection service for chunk {chunkIndex + 1}: {ex.Message}");
                    }
                }

                // Store all results as pii_detection_result.json in the output container
                try
                {

                    // Use output type as folder name
                    string[] pathParts = message.outputPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    string originalFileName = pathParts.Length > 0 ? pathParts[^1] : "input";
                    string outputTypeFolder = "pii_detection";
                    string outputFileName = $"{originalFileName}_pii_detection_result.json";
                    string outputPath = $"{outputTypeFolder}/{outputFileName}";
                    var resultBlobClient = containerClient.GetBlobClient(outputPath);

                    var outputJson = System.Text.Json.JsonSerializer.Serialize(new {
                        processor = "pii detection",
                        main_content = "TODO",
                        message = allResults,
                        original_filename = originalFileName,
                        origin_file = originalFileName,
                        folderName = outputTypeFolder
                    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

                    using (var ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(outputJson)))
                    {
                        var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
                        {
                            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders { ContentType = "application/json" }
                        };
                        await resultBlobClient.UploadAsync(ms, uploadOptions);
                    }
                    _logger.LogInformation($"PII detection results stored as {outputPath} in output container.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to store PII detection results: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing PII event: {ex.Message}");
                throw;
            }
        }
    }
}