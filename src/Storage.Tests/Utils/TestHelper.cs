using System.Globalization;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Storage.Tests.Utils;

public static class TestHelper
{
	public const int MinioInternalPort = 9000;
	public const string SecretKey = "ChangeMe123";
	public const string Username = "ROOTUSER";

	public static S3Settings CreateSettings(IContainer? container = null)
	{
		var environmentPort = Environment.GetEnvironmentVariable("STORAGE_PORT");
		int? port = string.IsNullOrEmpty(environmentPort)
			? container?.GetMappedPublicPort(MinioInternalPort) ?? 5300
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

	public static void EnsureBucketExists(S3Client client)
	{
		EnsureBucketExists(client, CancellationToken.None)
			.ConfigureAwait(false)
			.GetAwaiter()
			.GetResult();
	}

	public static async Task EnsureBucketExists(S3Client client, CancellationToken cancellation)
	{
		if (await client.IsBucketExists(cancellation).ConfigureAwait(false))
		{
			return;
		}

		await client.CreateBucket(cancellation).ConfigureAwait(false);
	}
}
