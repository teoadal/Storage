namespace Storage;

[Obsolete("Use S3BucketSettings instead.")]
public sealed class S3Settings
{
	public required string AccessKey { get; init; }

	public required string Bucket { get; init; }

	public required string EndPoint { get; init; }

	public int? Port { get; init; }

	public string Region { get; init; } = "us-east-1";

	public required string SecretKey { get; init; }

	public string Service { get; init; } = "s3";

	public bool UseHttp2 { get; init; }

	public required bool UseHttps { get; init; }

	internal S3BucketSettings MapToBucketSettings()
	{
		var schema = UseHttps ? "https" : "http";
		var port = Port.HasValue ? $":{Port}": string.Empty;

		return new S3BucketSettings
		{
			AccessKey = AccessKey,
			Bucket = Bucket,
			Endpoint = $"{schema}:\\{EndPoint}{port}",
			SecretKey = SecretKey,
			Region = Region,
			Service = Service,
			UseHttp2 = UseHttp2	
		};
	}
}
