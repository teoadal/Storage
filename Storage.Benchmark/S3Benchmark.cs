using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using AspNetCore.Yandex.ObjectStorage;
using AspNetCore.Yandex.ObjectStorage.Configuration;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Minio;

namespace Storage.Benchmark;

[SimpleJob(RuntimeMoniker.Net70)]
[MeanColumn]
[MemoryDiagnoser]
public class S3Benchmark
{
    [Benchmark]
    public async Task<int> Aws()
    {
        var bucket = _settings.Bucket;

        var result = 0;

        await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_amazonClient, bucket);

        try
        {
            await _amazonClient.GetObjectMetadataAsync(bucket, _fileId, _cancellation);
        }
        catch (Exception)
        {
            result++; // it's OK - file not found
        }

        _inputData.Seek(0, SeekOrigin.Begin);

        await _amazonTransfer.UploadAsync(_inputData, bucket, _fileId, _cancellation);
        result++;

        await _amazonClient.GetObjectMetadataAsync(bucket, _fileId, _cancellation);
        result++;

        _outputData.Seek(0, SeekOrigin.Begin);
        var fileDownload = await _amazonClient.GetObjectAsync(bucket, _fileId, _cancellation);
        await fileDownload.ResponseStream.CopyToAsync(_outputData, _cancellation);
        result++;

        await _amazonClient.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucket,
            Key = _fileId
        }, _cancellation);

        return ++result;
    }

    [Benchmark]
    public async Task<int> Minio()
    {
        var bucket = _settings.Bucket;
        var result = 0;

        if (!await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), _cancellation))
        {
            ThrowException();
        }

        result++;

        try
        {
            await _minioClient.StatObjectAsync(new StatObjectArgs()
                .WithBucket(bucket)
                .WithObject(_fileId), _cancellation);
        }
        catch (Exception)
        {
            result++; // it's OK - file not found
        }

        _inputData.Seek(0, SeekOrigin.Begin);
        await _minioClient.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(_fileId)
            .WithObjectSize(_inputData.Length)
            .WithStreamData(_inputData)
            .WithContentType("application/pdf"), _cancellation);

        result++;

        await _minioClient.StatObjectAsync(new StatObjectArgs()
            .WithBucket(bucket)
            .WithObject(_fileId), _cancellation);

        result++;

        _outputData.Seek(0, SeekOrigin.Begin);
        await _minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(bucket)
                .WithObject(_fileId)
                .WithCallbackStream((file, ct) => file.CopyToAsync(_outputData, ct)),
            _cancellation);

        result++;

        await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(bucket)
            .WithObject(_fileId), _cancellation);

        return ++result;
    }

    [Benchmark]
    public async Task<int> Yandex()
    {
        var bucket = _settings.Bucket;
        var objectService = _yandexClient.ObjectService;

        var result = 0;

        var bucketExists = await _yandexClient.BucketService.GetBucketMeta(bucket);
        if (!bucketExists.IsSuccessStatusCode) throw new Exception();
        result++;

        var fileNotExists = await objectService.GetAsync(_fileId);
        if (fileNotExists.IsSuccessStatusCode) ThrowException();
        result++;

        _inputData.Seek(0, SeekOrigin.Begin);
        var fileUpload = await objectService.PutAsync(_inputData, _fileId);
        if (!fileUpload.IsSuccessStatusCode) ThrowException();
        result++;

        var fileExists = await objectService.GetAsync(_fileId);
        if (!fileExists.IsSuccessStatusCode) ThrowException();
        result++;

        _outputData.Seek(0, SeekOrigin.Begin);
        var fileDownload = await objectService.GetAsync(_fileId);
        var fileDownloadStream = await fileDownload.ReadAsStreamAsync();
        if (fileDownloadStream.IsFailed) ThrowException();
        await fileDownloadStream.Value!.CopyToAsync(_outputData, _cancellation);
        result++;

        var fileDelete = await objectService.DeleteAsync(_fileId);
        if (!fileDelete.IsSuccessStatusCode) ThrowException();

        return ++result;
    }

    [Benchmark(Baseline = true)]
    public async Task<int> Handmade()
    {
        var result = 0;

        if (!await _handmadeClient.BucketExists(_cancellation)) ThrowException();
        result++;

        if (await _handmadeClient.FileExists(_fileId, _cancellation)) ThrowException();
        result++;

        _inputData.Seek(0, SeekOrigin.Begin);
        if (!await _handmadeClient.UploadFile(_fileId, _inputData, "application/pdf", _cancellation))
        {
            ThrowException();
        }

        result++;

        if (!await _handmadeClient.FileExists(_fileId, _cancellation)) ThrowException();
        result++;

        _outputData.Seek(0, SeekOrigin.Begin);
        var storageFile = await _handmadeClient.GetFile(_fileId, _cancellation);
        await storageFile
            .GetStream()
            .CopyToAsync(_outputData, _cancellation);

        result++;

        if (!await _handmadeClient.DeleteFile(_fileId, _cancellation)) ThrowException();
        return ++result;
    }

    #region Configuration

    private CancellationToken _cancellation;
    private Stream _inputData = null!;
    private string _fileId = null!;
    private MemoryStream _outputData = null!;
    private StorageSettings _settings = null!;

    private IAmazonS3 _amazonClient = null!;
    private TransferUtility _amazonTransfer = null!;
    private StorageClient _handmadeClient = null!;
    private MinioClient _minioClient = null!;
    private YandexStorageService _yandexClient = null!;


    [GlobalSetup]
    public void Config()
    {
        var fileData = File.ReadAllBytes("d:\\book.pdf");

        _cancellation = new CancellationToken();
        _inputData = new InputStream(fileData);
        _outputData = new MemoryStream(new byte[fileData.Length]);
        _fileId = $"привет-как-дела{Guid.NewGuid()}";

        _settings = new StorageSettings
        {
            AccessKey = "ROOTUSER",
            Bucket = "reconfig",
            EndPoint = "localhost",
            Port = 5300,
            SecretKey = "ChangeMe123",
            UseHttps = false
        };

        _amazonClient = new AmazonS3Client(
            new BasicAWSCredentials(_settings.AccessKey, _settings.SecretKey),
            new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.USEast1,
                ServiceURL = $"http://{_settings.EndPoint}:{_settings.Port}",
                ForcePathStyle = true // MUST be true to work correctly with MinIO server
            });
        _amazonTransfer = new TransferUtility(_amazonClient);

        _handmadeClient = new StorageClient(_settings);

        _minioClient = new MinioClient()
            .WithEndpoint(_settings.EndPoint, _settings.Port)
            .WithCredentials(_settings.AccessKey, _settings.SecretKey)
            .WithSSL(_settings.UseHttps)
            .Build();

        _yandexClient = new YandexStorageService(new YandexStorageOptions
        {
            AccessKey = _settings.AccessKey,
            BucketName = _settings.Bucket,
            Endpoint = $"{_settings.EndPoint}:{_settings.Port}",
            Protocol = "http",
            SecretKey = _settings.SecretKey
        });
    }

    [GlobalCleanup]
    public void Clear()
    {
        _handmadeClient.Dispose();
        _inputData.Dispose();
        _outputData.Dispose();
    }

    private static void ThrowException() => throw new Exception();

    #endregion
}