using System.Globalization;
using Testcontainers.Minio;

namespace Storage.Tests.Utils;

public static class TestHelper
{
	public const string SecretKey = "ChangeMe123";
	public const string Username = "ROOTUSER";

	public static S3Settings CreateSettings(MinioContainer? container = null)
	{
		var environmentPort = Environment.GetEnvironmentVariable("STORAGE_PORT");
		int? port = string.IsNullOrEmpty(environmentPort)
			? container?.GetMappedPublicPort(MinioBuilder.MinioPort) ?? 5300
			: environmentPort is "null"
				? null
				: int.Parse(environmentPort, CultureInfo.InvariantCulture);

		var environmentHttps = Environment.GetEnvironmentVariable("STORAGE_HTTPS");
		var https = !string.IsNullOrEmpty(environmentHttps) && bool.Parse(environmentHttps);

		return new S3Settings
		{
			AccessKey = Environment.GetEnvironmentVariable("STORAGE_KEY") ?? Username,
			Bucket = Environment.GetEnvironmentVariable("STORAGE_BUCKET") ?? "reconfig",
			EndPoint = Environment.GetEnvironmentVariable("STORAGE_ENDPOINT") ?? "localhost",
			Port = port,
			SecretKey = Environment.GetEnvironmentVariable("STORAGE_SECRET") ?? SecretKey,
			UseHttps = https,
		};
	}

	public static MinioContainer CreateContainer(bool run = true)
	{
		var container = new MinioBuilder()
			.WithPassword(SecretKey)
			.WithUsername(Username)
			.Build();

		if (run)
		{
			container.StartAsync().Wait();
		}

		return container;
	}

	public static void EnsureBucketExists(S3Client client)
	{
		EnsureBucketExists(client, CancellationToken.None).Wait();
	}

	public static async Task EnsureBucketExists(S3Client client, CancellationToken cancellation)
	{
		if (await client.IsBucketExists(cancellation))
		{
			return;
		}

		await client.CreateBucket(cancellation);
	}
}
