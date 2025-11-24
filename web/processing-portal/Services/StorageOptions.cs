namespace ProcessingPortal.Services;

public sealed class StorageOptions
{
    public string AccountName { get; set; } = string.Empty;
    public string? BlobServiceUri { get; set; }
    public string InputContainer { get; set; } = "input";
    public string OutputContainer { get; set; } = "output";
}
