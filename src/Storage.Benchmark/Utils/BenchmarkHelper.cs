using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Minio;

namespace Storage.Benchmark.Utils;

internal static class BenchmarkHelper
{
    // ReSharper disable once InconsistentNaming
    public static AmazonS3Client CreateAWSClient(StorageSettings settings)
    {
        var scheme = settings.UseHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var port = settings.Port.HasValue ? $":{settings.Port}" : string.Empty;

        return new AmazonS3Client(
            new BasicAWSCredentials(settings.AccessKey, settings.SecretKey),
            new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.USEast1,
                ServiceURL = $"{scheme}://{settings.EndPoint}{port}",
                ForcePathStyle = true // MUST be true to work correctly with MinIO server
            });
    }

    public static MinioClient CreateMinioClient(StorageSettings settings)
    {
        var builder = new MinioClient();
        var port = settings.Port;
        if (port.HasValue) builder.WithEndpoint(settings.EndPoint, port.Value);
        else builder.WithEndpoint(settings.EndPoint);

        return builder
            .WithCredentials(settings.AccessKey, settings.SecretKey)
            .WithSSL(settings.UseHttps)
            .Build();
    }

    public static StorageClient CreateStoragesClient(StorageSettings settings) => new(settings);

    public static void EnsureBucketExists(StorageClient client, CancellationToken cancellation)
    {
        if (client.BucketExists(cancellation).GetAwaiter().GetResult()) return;

        client.CreateBucket(cancellation).GetAwaiter().GetResult();
    }

    public static InputStream ReadBigFile(IConfiguration config)
    {
        var filePath = config.GetValue<string>("BigFilePath");

        return new InputStream(!string.IsNullOrEmpty(filePath) && File.Exists(filePath)
            ? File.ReadAllBytes(filePath)
            : GetByteArray(123 * 1024 * 1024)); // 123 Mb
    }

    public static IConfiguration ReadConfiguration() => new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", false)
        .Build();

    public static StorageSettings ReadSettings(IConfiguration config)
    {
        var settings = config.GetRequiredSection("S3Storage").Get<StorageSettings>();
        return settings == null || string.IsNullOrEmpty(settings.EndPoint)
            ? throw new Exception("S3Storage configuration is not found")
            : settings;
    }

    public static InputStream ReadSmallFile(IConfiguration config)
    {
        var filePath = config.GetValue<string>("BigFilePath");

        return new InputStream(!string.IsNullOrEmpty(filePath) && File.Exists(filePath)
            ? File.ReadAllBytes(filePath)
            : GetByteArray(1 * 1024 * 1024)); // 1 Mb
    }

    private static byte[] GetByteArray(int size)
    {
        var random = Random.Shared;
        var bytes = new byte[size];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte) random.Next();
        }

        return bytes;
    }
}