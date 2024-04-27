using AutoFixture;
using DotNet.Testcontainers.Containers;

namespace Storage.Tests.Utils;

public sealed class StorageFixture : IDisposable, IAsyncDisposable
{
	public const string StreamContentType = "application/octet-stream";

	private const int DefaultByteArraySize = 1 * 1024 * 1024; // 7Mb

	private readonly IContainer? _container;
	private Fixture? _fixture;

	public StorageFixture()
	{
		_container = TestHelper.CreateContainer();

		Settings = TestHelper.CreateSettings(_container);
		HttpClient = new HttpClient();
		S3Client = new S3Client(Settings);

		TestHelper.EnsureBucketExists(S3Client);
	}

	internal S3Settings Settings { get; }

	internal S3Client S3Client { get; }

	internal HttpClient HttpClient { get; }

	internal Fixture Mocks => _fixture ??= new Fixture();

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

	public T Create<T>()
	{
		return Mocks.Create<T>();
	}

	public void Dispose()
	{
		HttpClient.Dispose();
		S3Client.Dispose();
	}

	public async ValueTask DisposeAsync()
	{
		if (_container != null)
		{
			await _container.DisposeAsync();
		}
	}
}
