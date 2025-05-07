using System.Globalization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;


namespace Storage.Tests;

public static class TestHelper
{
	public const int MinioInternalPort = 9000;
	public const string SecretKey = "ChangeMe123";
	public const string Username = "ROOTUSER";

	public static S3BucketClient CloneClient(StorageFixture fixture, string? bucket = null, HttpClient? client = null)
	{
		var settings = fixture.Settings;
		var clonedSettings = new S3BucketSettings
		{
			AccessKey = settings.AccessKey,
			Bucket = bucket ?? fixture.Create<string>(),
			Endpoint = settings.Endpoint,
			SecretKey = settings.SecretKey
		};

		return new S3BucketClient(client ?? fixture.HttpClient, clonedSettings);
	}

	public static S3BucketSettings CreateSettings(IContainer? container = null)
	{
		var environmentPort = Environment.GetEnvironmentVariable("STORAGE_PORT");
		int? port;
		if (string.IsNullOrEmpty(environmentPort))
		{
			port = container?.GetMappedPublicPort(MinioInternalPort) ?? 5300;
		}
		else
		{
			port = environmentPort is "null"
				? null
				: int.Parse(environmentPort, CultureInfo.InvariantCulture);
		}

		var environmentHttps = Environment.GetEnvironmentVariable("STORAGE_HTTPS");
		var https = !string.IsNullOrEmpty(environmentHttps) && bool.Parse(environmentHttps);
		var schema = https ? "https" : "http";
		var host = Environment.GetEnvironmentVariable("STORAGE_ENDPOINT") ?? "localhost";

		return new S3BucketSettings
		{
			AccessKey = Environment.GetEnvironmentVariable("STORAGE_KEY") ?? Username,
			Bucket = Environment.GetEnvironmentVariable("STORAGE_BUCKET") ?? "reconfig",
			Endpoint = $"{schema}://{host}:{port}",
			SecretKey = Environment.GetEnvironmentVariable("STORAGE_SECRET") ?? SecretKey
		};
	}

	public static IContainer CreateContainer(bool run = true)
	{
		var container = new ContainerBuilder()
			.WithImage("minio/minio:latest")
			.WithEnvironment("MINIO_ROOT_USER", Username)
			.WithEnvironment("MINIO_ROOT_PASSWORD", SecretKey)
			.WithPortBinding(MinioInternalPort, true)
			.WithCommand("server", "/data")
			.WithWaitStrategy(Wait
				.ForUnixContainer()
				.UntilHttpRequestIsSucceeded(static request =>
					request.ForPath("/minio/health/ready").ForPort(MinioInternalPort)))
			.Build();

		if (run)
		{
			container
				.StartAsync()
				.ConfigureAwait(false)
				.GetAwaiter()
				.GetResult();
		}

		return container;
	}

	public static void EnsureBucketExists(S3BucketClient client)
	{
		EnsureBucketExists(client, CancellationToken.None)
			.ConfigureAwait(false)
			.GetAwaiter()
			.GetResult();
	}

	public static async Task EnsureBucketExists(S3BucketClient client, CancellationToken cancellation)
	{
		if (await client.IsBucketExists(cancellation).ConfigureAwait(false))
		{
			return;
		}

		await client.CreateBucket(cancellation).ConfigureAwait(false);
	}
}
