using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Amazon.S3.Util;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Minio;
using Storage.Benchmark.Utils;

namespace Storage.Benchmark;

[SimpleJob(RuntimeMoniker.Net70)]
[MeanColumn]
[MemoryDiagnoser]
public class S3Benchmark
{
	private string _bucket = null!;
	private CancellationToken _cancellation;
	private InputStream _inputData = null!;
	private string _fileId = null!;
	private MemoryStream _outputData = null!;

	private IAmazonS3 _amazonClient = null!;
	private TransferUtility _amazonTransfer = null!;
	private MinioClient _minioClient = null!;
	private S3Client _s3Client = null!;

	[GlobalSetup]
	public void Config()
	{
		var config = BenchmarkHelper.ReadConfiguration();
		var settings = BenchmarkHelper.ReadSettings(config);

		_bucket = settings.Bucket;
		_cancellation = default;
		_fileId = $"привет-как-дела{Guid.NewGuid()}";
		_inputData = BenchmarkHelper.ReadBigFile(config);
		_outputData = new MemoryStream(new byte[_inputData.Length]);

		_amazonClient = BenchmarkHelper.CreateAWSClient(settings);
		_amazonTransfer = new TransferUtility(_amazonClient);
		_minioClient = BenchmarkHelper.CreateMinioClient(settings);
		_s3Client = BenchmarkHelper.CreateStoragesClient(settings);

		BenchmarkHelper.EnsureBucketExists(_s3Client, _cancellation);
	}

	[GlobalCleanup]
	public void Clear()
	{
		_s3Client.Dispose();
		_inputData.Dispose();
		_outputData.Dispose();
	}

	[Benchmark]
	public async Task<int> Aws()
	{
		var result = 0;

		await AmazonS3Util.DoesS3BucketExistV2Async(_amazonClient, _bucket);

		try
		{
			await _amazonClient.GetObjectMetadataAsync(_bucket, _fileId, _cancellation);
		}
		catch (Exception)
		{
			result++; // it's OK - file not found
		}

		_inputData.Seek(0, SeekOrigin.Begin);

		await _amazonTransfer.UploadAsync(_inputData, _bucket, _fileId, _cancellation);
		result++;

		await _amazonClient.GetObjectMetadataAsync(_bucket, _fileId, _cancellation);
		result++;

		_outputData.Seek(0, SeekOrigin.Begin);
		var fileDownload = await _amazonClient.GetObjectAsync(_bucket, _fileId, _cancellation);
		await fileDownload.ResponseStream.CopyToAsync(_outputData, _cancellation);
		result++;

		await _amazonClient.DeleteObjectAsync(
			new DeleteObjectRequest { BucketName = _bucket, Key = _fileId },
			_cancellation);

		return ++result;
	}

	[Benchmark]
	public async Task<int> Minio()
	{
		var result = 0;

		if (!await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket), _cancellation))
		{
			ThrowException();
		}

		result++;

		try
		{
			await _minioClient.StatObjectAsync(
				new StatObjectArgs()
				.WithBucket(_bucket)
				.WithObject(_fileId),
				_cancellation);
		}
		catch (Exception)
		{
			result++; // it's OK - file not found
		}

		_inputData.Seek(0, SeekOrigin.Begin);
		await _minioClient.PutObjectAsync(
			new PutObjectArgs()
				.WithBucket(_bucket)
				.WithObject(_fileId)
				.WithObjectSize(_inputData.Length)
				.WithStreamData(_inputData)
				.WithContentType("application/pdf"),
			_cancellation);

		result++;

		await _minioClient.StatObjectAsync(
			new StatObjectArgs()
				.WithBucket(_bucket)
				.WithObject(_fileId),
			_cancellation);

		result++;

		_outputData.Seek(0, SeekOrigin.Begin);
		await _minioClient.GetObjectAsync(
			new GetObjectArgs()
				.WithBucket(_bucket)
				.WithObject(_fileId)
				.WithCallbackStream((file, ct) => file.CopyToAsync(_outputData, ct)),
			_cancellation);

		result++;

		await _minioClient.RemoveObjectAsync(
			new RemoveObjectArgs()
				.WithBucket(_bucket)
				.WithObject(_fileId),
			_cancellation);

		return ++result;
	}

	[Benchmark(Baseline = true)]
	public async Task<int> Storage()
	{
		var result = 0;

		var bucketExistsResult = await _s3Client.IsBucketExists(_cancellation);
		if (!bucketExistsResult)
		{
			ThrowException();
		}

		result++;

		var fileExistsResult = await _s3Client.IsFileExists(_fileId, _cancellation);
		if (fileExistsResult)
		{
			ThrowException();
		}

		result++;

		_inputData.Seek(0, SeekOrigin.Begin);
		var fileUploadResult = await _s3Client.UploadFile(_fileId, "application/pdf", _inputData, _cancellation);
		if (!fileUploadResult)
		{
			ThrowException();
		}

		result++;

		fileExistsResult = await _s3Client.IsFileExists(_fileId, _cancellation);
		if (!fileExistsResult)
		{
			ThrowException();
		}

		result++;

		_outputData.Seek(0, SeekOrigin.Begin);
		var storageFile = await _s3Client.GetFile(_fileId, _cancellation);
		if (!storageFile)
		{
			ThrowException(storageFile.ToString());
		}

		var fileStream = await storageFile.GetStream(_cancellation);
		await fileStream.CopyToAsync(_outputData, _cancellation);

		storageFile.Dispose();

		result++;

		await _s3Client.DeleteFile(_fileId, _cancellation);
		return ++result;
	}

	private static void ThrowException(string? message = null)
	{
		throw new Exception(message);
	}
}
