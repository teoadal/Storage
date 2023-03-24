﻿using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Minio;

namespace Storage.Benchmark.Utils;

internal static class BenchmarkHelper
{
    private static readonly byte[] StreamBuffer = new byte[2048];

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

    public static void EnsureFileExists(
        IConfiguration config, StorageClient client, string fileName,
        CancellationToken cancellation)
    {
        var fileData = ReadBigFile(config);
        fileData.Seek(0, SeekOrigin.Begin);

        var result = client
            .UploadFile(fileName, fileData, "application/pdf", cancellation)
            .GetAwaiter()
            .GetResult();

        if (!result) throw new Exception("File isn't uploaded");
    }

    public static byte[] ReadBigArray(IConfiguration config)
    {
        var filePath = config.GetValue<string>("BigFilePath");

        return !string.IsNullOrEmpty(filePath) && File.Exists(filePath)
            ? File.ReadAllBytes(filePath)
            : GetByteArray(123 * 1024 * 1024); // 123 Mb
    }

    public static InputStream ReadBigFile(IConfiguration config)
    {
        return new InputStream(ReadBigArray(config));
    }

    public static IConfiguration ReadConfiguration() => new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", false)
        .Build();

    public static int ReadStreamMock(Stream input, byte[]? buffer = null)
    {
        buffer ??= StreamBuffer;

        var result = 0;
        while (input.Read(buffer) != 0)
        {
            result++;
        }

        input.Dispose();

        return result;
    }

    public static StorageSettings ReadSettings(IConfiguration config)
    {
        var settings = config.GetRequiredSection("S3Storage").Get<StorageSettings>();
        if (settings == null || string.IsNullOrEmpty(settings.EndPoint))
        {
            throw new Exception("S3Storage configuration is not found");
        }

        var isContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        if (isContainer != null && bool.TryParse(isContainer, out var value) && value)
        {
            settings = new StorageSettings
            {
                AccessKey = settings.AccessKey,
                Bucket = settings.Bucket,
                EndPoint = "host.docker.internal",
                Port = settings.Port,
                Region = settings.Region,
                SecretKey = settings.SecretKey,
                Service = settings.Service,
                UseHttp2 = settings.UseHttp2,
                UseHttps = settings.UseHttps,
            };
        }

        return settings;
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