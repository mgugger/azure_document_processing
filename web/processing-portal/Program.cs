using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Azure;
using Azure.Core;
using Azure.Core.Diagnostics;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProcessingPortal.Components;
using ProcessingPortal.Services;

var builder = WebApplication.CreateBuilder(args);
LoadAzdEnvironmentVariables(builder);
EnableIdentityLogging(builder);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));



builder.Services.AddSingleton<TokenCredential>(sp => CreateCredential(sp));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
    var configuration = sp.GetRequiredService<IConfiguration>();
    var credential = sp.GetRequiredService<TokenCredential>();
    var serviceUri = ResolveBlobServiceUri(options, configuration);
    return new BlobServiceClient(serviceUri, credential);
});
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/input/{**blobName}", async Task<IResult> (HttpContext httpContext, string blobName, IBlobStorageService storage, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(blobName))
    {
        return Results.BadRequest(new { message = "Blob name is required." });
    }

    try
    {
        var result = await storage.OpenInputBlobAsync(blobName, cancellationToken);
        SetInlineDisposition(httpContext, blobName);
        return Results.Stream(result.Content, result.ContentType, enableRangeProcessing: true);
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        return Results.NotFound(new { message = $"Blob '{blobName}' was not found." });
    }
});

app.MapGet("/api/output/{**blobName}", async Task<IResult> (HttpContext httpContext, string blobName, IBlobStorageService storage, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(blobName))
    {
        return Results.BadRequest(new { message = "Blob name is required." });
    }

    try
    {
        var result = await storage.OpenOutputBlobAsync(blobName, cancellationToken);
        SetInlineDisposition(httpContext, blobName);
        return Results.Stream(result.Content, result.ContentType, enableRangeProcessing: true);
    }
    catch (RequestFailedException ex) when (ex.Status == 404)
    {
        return Results.NotFound(new { message = $"Blob '{blobName}' was not found." });
    }
});

app.Run();

static Uri ResolveBlobServiceUri(StorageOptions options, IConfiguration configuration)
{
    
    if (!string.IsNullOrWhiteSpace(options.BlobServiceUri))
    {
        return new Uri(options.BlobServiceUri);
    }

    var envUri = GetConfigValue(configuration, "STORAGE_BLOB_SERVICE_URI")
        ?? GetConfigValue(configuration, "Storage:BlobServiceUri")
        ?? GetConfigValue(configuration, "Storage__BlobServiceUri");
    if (!string.IsNullOrWhiteSpace(envUri))
    {
        return new Uri(envUri);
    }

    var accountName = options.AccountName;
    if (string.IsNullOrWhiteSpace(accountName))
    {
        accountName = GetConfigValue(configuration, "STORAGE_ACCOUNT_NAME")
            ?? GetConfigValue(configuration, "Storage__AccountName")
            ?? GetConfigValue(configuration, "Storage:AccountName")
            ?? GetConfigValue(configuration, "storageAccountName");
    }

    if (string.IsNullOrWhiteSpace(accountName))
    {
        throw new InvalidOperationException("Storage account name or blob service URI must be provided via configuration.");
    }

    return new Uri($"https://{accountName}.blob.core.windows.net");
}

static TokenCredential CreateCredential(IServiceProvider serviceProvider)
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Identity");
    var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();

    if (ShouldUseAzureCliCredential(configuration))
    {
        logger.LogInformation("Using AzureCliCredential for blob access (user identity).");
        return new AzureCliCredential();
    }

    var options = new DefaultAzureCredentialOptions
    {
        ExcludeAzureCliCredential = false
    };

    // Only use managed identity when deployed to Azure, not when running locally
    var managedIdentityClientId = GetConfigValue(configuration, "AZURE_USER_ASSIGNED_CLIENT_ID");
    if (!string.IsNullOrWhiteSpace(managedIdentityClientId) && !environment.IsDevelopment())
    {
        options.ManagedIdentityClientId = managedIdentityClientId;
        logger.LogInformation("Using DefaultAzureCredential with managed identity client ID: {ClientId}", managedIdentityClientId);
    }
    else
    {
        logger.LogInformation("Using DefaultAzureCredential for blob access (local development - will use Azure CLI or VS Code credentials).");
    }

    return new DefaultAzureCredential(options);
}

static string? GetConfigValue(IConfiguration configuration, string key)
{
    var candidates = GenerateKeyCandidates(key).ToList();

    foreach (var candidate in candidates)
    {
        var value = configuration[candidate];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    var enumerated = configuration.AsEnumerable(makePathsRelative: true)
        .FirstOrDefault(kv => candidates.Contains(kv.Key, StringComparer.OrdinalIgnoreCase));
    if (!string.IsNullOrWhiteSpace(enumerated.Value))
    {
        return enumerated.Value;
    }

    foreach (var candidate in candidates)
    {
        var envValue = Environment.GetEnvironmentVariable(candidate);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            return envValue;
        }
    }

    return null;
}

static bool ShouldUseAzureCliCredential(IConfiguration configuration)
{
    return IsEnabled(configuration, "AZURE_USE_CLI_IDENTITY")
        || IsEnabled(configuration, "Azure:UseCliIdentity")
        || IsEnabled(configuration, "Azure__UseCliIdentity");
}

static IEnumerable<string> GenerateKeyCandidates(string key)
{
    if (string.IsNullOrWhiteSpace(key))
    {
        yield break;
    }

    yield return key;
    if (key.Contains(':', StringComparison.Ordinal))
    {
        yield return key.Replace(":", "__");
    }

    if (key.Contains("__", StringComparison.Ordinal))
    {
        yield return key.Replace("__", ":");
    }

    yield return key.ToUpperInvariant();
    yield return key.ToLowerInvariant();
}

static bool IsEnabled(IConfiguration configuration, string key)
{
    var value = GetConfigValue(configuration, key);
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    if (bool.TryParse(value, out var parsed))
    {
        return parsed;
    }

    return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase);
}

static void LoadAzdEnvironmentVariables(WebApplicationBuilder builder)
{
    var envValues = ReadEnvFiles(builder.Environment.ContentRootPath);
    if (envValues.Count == 0)
    {
        return;
    }

    AddWellKnownAliases(envValues);
    
    builder.Configuration.AddInMemoryCollection(envValues.Select(static kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));
    foreach (var (key, value) in envValues)
    {
        Environment.SetEnvironmentVariable(key, value);
    }
}

static void EnableIdentityLogging(WebApplicationBuilder builder)
{
    if (!IsIdentityLoggingEnabled(builder.Configuration))
    {
        return;
    }

    builder.Services.AddSingleton(sp =>
    {
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Identity");
        logger.LogInformation("Azure Identity diagnostic logging enabled.");
        return AzureEventSourceListener.CreateConsoleLogger();
    });
}

static bool IsIdentityLoggingEnabled(IConfiguration configuration)
{
    return IsEnabled(configuration, "AZURE_IDENTITY_LOGGING_ENABLED")
        || IsEnabled(configuration, "Azure:Identity:LoggingEnabled")
        || IsEnabled(configuration, "Azure__Identity__LoggingEnabled");
}

static void AddWellKnownAliases(IDictionary<string, string> values)
{
    AddAlias(values, "storageAccountName", new[]
    {
        "Storage:AccountName",
        "Storage__AccountName",
        "STORAGE_ACCOUNT_NAME"
    });

    AddAlias(values, "storageBlobServiceUri", new[]
    {
        "Storage:BlobServiceUri",
        "Storage__BlobServiceUri",
        "STORAGE_BLOB_SERVICE_URI"
    });
}

static void AddAlias(IDictionary<string, string> values, string sourceKey, IEnumerable<string> aliases)
{
    if (!TryGetValueIgnoreCase(values, sourceKey, out var value) || string.IsNullOrWhiteSpace(value))
    {
        return;
    }

    foreach (var alias in aliases)
    {
        values[alias] = value;
    }
}

static bool TryGetValueIgnoreCase(IDictionary<string, string> values, string key, out string value)
{
    if (values.TryGetValue(key, out value!))
    {
        return true;
    }

    var match = values.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
    if (!string.IsNullOrWhiteSpace(match.Key))
    {
        value = match.Value;
        return true;
    }

    value = string.Empty;
    return false;
}

static IDictionary<string, string> ReadEnvFiles(string contentRoot)
{
    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in EnumerateCandidateEnvFiles(contentRoot))
    {
        if (!File.Exists(path))
        {
            continue;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            if (TryParseEnvLine(line, out var key, out var value))
            {
                values[key] = value;
            }
        }
    }

    return values;
}

static IReadOnlyList<string> EnumerateCandidateEnvFiles(string contentRoot)
{
    var orderedPaths = new List<string>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    void AddCandidate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var fullPath = Path.GetFullPath(candidate);
        if (seen.Add(fullPath))
        {
            orderedPaths.Add(fullPath);
        }
    }

    foreach (var root in EnumerateAncestorRoots(contentRoot))
    {
        AddCandidate(Path.Combine(root, ".env.tmp"));
        AddCandidate(Path.Combine(root, ".env"));
        AddCandidate(Path.Combine(root, ".env.local"));
        AddCandidate(Path.Combine(root, "azure.env"));

        var azureDir = Path.Combine(root, ".azure");
        if (!Directory.Exists(azureDir))
        {
            continue;
        }

        var azureEnvName = Environment.GetEnvironmentVariable("AZURE_ENV_NAME");
        if (!string.IsNullOrWhiteSpace(azureEnvName))
        {
            var envRoot = Path.Combine(azureDir, azureEnvName);
            AddCandidate(Path.Combine(envRoot, "env"));
            AddCandidate(Path.Combine(envRoot, ".env"));
            AddCandidate(Path.Combine(envRoot, ".env.local"));
        }

        foreach (var envDir in Directory.GetDirectories(azureDir))
        {
            AddCandidate(Path.Combine(envDir, "env"));
            AddCandidate(Path.Combine(envDir, ".env"));
            AddCandidate(Path.Combine(envDir, ".env.local"));
        }
    }

    return orderedPaths;
}

static IEnumerable<string> EnumerateAncestorRoots(string startingPath)
{
    var current = new DirectoryInfo(Path.GetFullPath(startingPath));
    while (current is not null)
    {
        yield return current.FullName;
        current = current.Parent;
    }
}

static bool TryParseEnvLine(string line, out string key, out string value)
{
    key = string.Empty;
    value = string.Empty;

    if (string.IsNullOrWhiteSpace(line))
    {
        return false;
    }

    var trimmed = line.Trim();
    if (trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal))
    {
        return false;
    }

    if (trimmed.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
    {
        trimmed = trimmed.Substring(7).Trim();
    }

    var separatorIndex = trimmed.IndexOf('=');
    if (separatorIndex <= 0)
    {
        return false;
    }

    key = trimmed[..separatorIndex].Trim();
    var rawValue = trimmed[(separatorIndex + 1)..].Trim();
    value = UnwrapQuotes(rawValue);
    return !string.IsNullOrWhiteSpace(key);
}

static string UnwrapQuotes(string input)
{
    if (string.IsNullOrEmpty(input))
    {
        return string.Empty;
    }

    if ((input.StartsWith('"') && input.EndsWith('"')) || (input.StartsWith('\'') && input.EndsWith('\'')))
    {
        return input[1..^1];
    }

    return input;
}

static void SetInlineDisposition(HttpContext httpContext, string blobName)
{
    var fileName = Path.GetFileName(blobName);
    if (string.IsNullOrWhiteSpace(fileName))
    {
        return;
    }

    var contentDisposition = new System.Net.Mime.ContentDisposition
    {
        Inline = true,
        FileName = fileName
    };

    httpContext.Response.Headers.ContentDisposition = contentDisposition.ToString();
}
