using AutoFixture;

namespace Storage.Tests.Mocks;

public sealed class StorageFixture : IDisposable
{
    public const string StreamContentType = "application/octet-stream";

    public Fixture Mocks => _fixture ??= new Fixture();

    public readonly HttpClient HttpClient;
    public readonly StorageClient StorageClient;
    public readonly StorageSettings Settings;

    private const int DefaultByteArraySize = 1 * 1024 * 1024; //7Mb
    private Fixture? _fixture;
    private readonly bool _isPlayground;

    public StorageFixture()
    {
        _isPlayground = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB"));

        Settings = new StorageSettings
        {
            AccessKey = "ROOTUSER",
            Bucket = "reconfig",
            EndPoint = _isPlayground ? "127.0.0.1" : "localhost",
            Port = _isPlayground ? 900 : 5300,
            SecretKey = "ChangeMe123",
            UseHttps = false
        };

        HttpClient = new HttpClient();
        StorageClient = new StorageClient(Settings);

        if (_isPlayground)
        {
            StorageClient.CreateBucket(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
    }

    public T Create<T>() => Mocks.Create<T>();

    public static byte[] GetByteArray(int size = DefaultByteArraySize)
    {
        var random = Random.Shared;
        var bytes = new byte[size];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte) random.Next();
        }

        return bytes;
    }

    public static MemoryStream GetByteStream(int size = DefaultByteArraySize) => new(GetByteArray(size));

    public static MemoryStream GetEmptyByteStream(long? size) => size.HasValue
        ? new MemoryStream(new byte[(int) size])
        : new MemoryStream();


    public void Dispose()
    {
        try
        {
            if (_isPlayground)
            {
                StorageClient.DeleteBucket(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
        }
        finally
        {
            StorageClient.Dispose();
            HttpClient.Dispose();
        }
    }
}