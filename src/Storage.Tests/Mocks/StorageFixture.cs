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
    private readonly bool _isMinioPlayground;

    public StorageFixture()
    {
        _isMinioPlayground = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("USE_MINIO_PLAYGROUND"));

        if (_isMinioPlayground)
        {
            // https://min.io/docs/minio/linux/developers/python/minio-py.html#file-uploader-py

            Settings = new StorageSettings
            {
                AccessKey = "Q3AM3UQ867SPQQA43P2F",
                Bucket = "reconfig",
                EndPoint = "play.min.io",
                Port = 9000,
                SecretKey = "zuf+tfteSlswRu7BJ86wekitnifILbZam1KYY3TG",
                UseHttps = true
            };
        }
        else
        {
            Settings = new StorageSettings
            {
                AccessKey = "ROOTUSER",
                Bucket = "reconfig",
                EndPoint = "localhost",
                Port = 5300,
                SecretKey = "ChangeMe123",
                UseHttps = false
            };
        }

        HttpClient = new HttpClient();
        StorageClient = new StorageClient(Settings);

        if (_isMinioPlayground)
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
            if (_isMinioPlayground)
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