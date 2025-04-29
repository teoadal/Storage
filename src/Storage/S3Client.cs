namespace Storage;


[Obsolete("Use S3BucketClient instead.")]
public sealed class S3Client(S3Settings settings, HttpClient? client = null)
	: S3BucketClient(client ?? new HttpClient(), settings.MapToBucketSettings())
{
}
