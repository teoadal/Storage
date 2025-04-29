namespace Storage;

/// <summary>
/// Represents the settings required to configure an S3 bucket connection.
/// </summary>
public sealed class S3BucketSettings
{
	public required string AccessKey { get; init; }

	public required string Bucket { get; init; }

	/// <summary>
	/// Gets the endpoint URL for the S3 service.
	/// This property is required.
	/// </summary>
	public required string Endpoint { get; init; }

	public string Region { get; init; } = "us-east-1";

	public required string SecretKey { get; init; }

	public string Service { get; init; } = "s3";

	public bool UseHttp2 { get; init; }
}
