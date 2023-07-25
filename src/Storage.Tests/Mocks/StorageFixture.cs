using System.Globalization;
using AutoFixture;

namespace Storage.Tests.Mocks;

public sealed class StorageFixture : IDisposable
{
	public const string StreamContentType = "application/octet-stream";

	private const int DefaultByteArraySize = 1 * 1024 * 1024; // 7Mb

	private readonly HttpClient _httpClient;
	private readonly S3Client _s3Client;
	private Fixture? _fixture;

	public StorageFixture()
	{
		var environmentPort = Environment.GetEnvironmentVariable("STORAGE_PORT");
		int? port = string.IsNullOrEmpty(environmentPort)
			? 5300
			: environmentPort is "null"
				? null
				: int.Parse(environmentPort, CultureInfo.InvariantCulture);

		var environmentHttps = Environment.GetEnvironmentVariable("STORAGE_HTTPS");
		var https = !string.IsNullOrEmpty(environmentHttps) && bool.Parse(environmentHttps);

		var settings = new S3Settings
		{
			AccessKey = Environment.GetEnvironmentVariable("STORAGE_KEY") ?? "ROOTUSER",
			Bucket = Environment.GetEnvironmentVariable("STORAGE_BUCKET") ?? "reconfig",
			EndPoint = Environment.GetEnvironmentVariable("STORAGE_ENDPOINT") ?? "localhost",
			Port = port,
			SecretKey = Environment.GetEnvironmentVariable("STORAGE_SECRET") ?? "ChangeMe123",
			UseHttps = https,
		};

		_httpClient = new HttpClient();
		_s3Client = new S3Client(settings);
	}

	public Fixture Mocks => _fixture ??= new Fixture();

	public static byte[] GetByteArray(int size = DefaultByteArraySize)
	{
		var random = Random.Shared;
		var bytes = new byte[size];
		for (var i = 0; i < bytes.Length; i++)
		{
			bytes[i] = (byte)random.Next();
		}

		return bytes;
	}

	public static MemoryStream GetByteStream(int size = DefaultByteArraySize)
	{
		return new MemoryStream(GetByteArray(size));
	}

	public static MemoryStream GetEmptyByteStream(long? size)
	{
		return size.HasValue
			? new MemoryStream(new byte[(int)size])
			: new MemoryStream();
	}

	public void Dispose()
	{
		_httpClient.Dispose();
		_s3Client.Dispose();
	}

	public T Create<T>()
	{
		return Mocks.Create<T>();
	}
}
