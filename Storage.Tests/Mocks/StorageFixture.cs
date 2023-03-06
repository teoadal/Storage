using AutoFixture;

namespace Storage.Tests.Mocks;

public sealed class StorageFixture : IDisposable
{
    public const string StreamContentType = "application/octet-stream";

    public Fixture Mocks => _fixture ??= new Fixture();

    public readonly StorageClient Client;
    public readonly StorageSettings Settings;

    private const int DefaultByteArraySize = 7 * 1024 * 1024; //7Mb
    private Fixture? _fixture;

    public StorageFixture()
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

        Client = new StorageClient(Settings);
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
        Client.Dispose();
    }
}