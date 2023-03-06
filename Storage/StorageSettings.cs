namespace Storage;

public sealed class StorageSettings
{
    public required string AccessKey { get; init; }

    public required string Bucket { get; init; }

    public required string EndPoint { get; init; }

    public required int Port { get; init; }

    public string Region { get; init; } = "us-east-1";

    public required string SecretKey { get; init; }

    public string Service { get; init; } = "s3";

    public required bool UseHttps { get; init; }
}