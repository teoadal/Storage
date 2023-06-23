using AutoFixture;

namespace Storage.Tests.Mocks;

public sealed class StorageFixture : IDisposable
{
    public const string StreamContentType = "application/octet-stream";

    public Fixture Mocks => _fixture ??= new Fixture();

    public readonly HttpClient HttpClient;
    public readonly S3Client S3Client;
    public readonly S3Settings Settings;

    private const int DefaultByteArraySize = 1 * 1024 * 1024; //7Mb
    private Fixture? _fixture;

    public StorageFixture()
    {
        var environmentPort = Environment.GetEnvironmentVariable("STORAGE_PORT");
        int? port = string.IsNullOrEmpty(environmentPort)
            ? 5300
            : environmentPort == "null"
                ? null
                : int.Parse(environmentPort);

        var environmentHttps = Environment.GetEnvironmentVariable("STORAGE_HTTPS");
        var https = !string.IsNullOrEmpty(environmentHttps) && bool.Parse(environmentHttps);

        Settings = new S3Settings
        {
            AccessKey = Environment.GetEnvironmentVariable("STORAGE_KEY") ?? "ROOTUSER",
            Bucket = Environment.GetEnvironmentVariable("STORAGE_BUCKET") ?? "reconfig",
            EndPoint = Environment.GetEnvironmentVariable("STORAGE_ENDPOINT") ?? "localhost",
            Port = port,
            SecretKey = Environment.GetEnvironmentVariable("STORAGE_SECRET") ?? "ChangeMe123",
            UseHttps = https
        };

        HttpClient = new HttpClient();
        S3Client = new S3Client(Settings);
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
        HttpClient.Dispose();
        S3Client.Dispose();
    }
}