namespace ProcessingPortal.Services;

public sealed record BlobItemModel(
    string Name,
    long? Size,
    DateTimeOffset? LastModified,
    string? ContentType
);
