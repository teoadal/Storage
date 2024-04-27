using BenchmarkDotNet.Attributes;
using Storage.Benchmark.Utils;

namespace Storage.Benchmark.InternalBenchmarks;

[MeanColumn]
[MemoryDiagnoser]
[InProcess]
public class DownloadBenchmark
{
	private CancellationToken _cancellation;
	private string _fileId = null!;
	private S3Client _s3Client = null!;

	[GlobalSetup]
	public void Config()
	{
		var config = BenchmarkHelper.ReadConfiguration();
		var settings = BenchmarkHelper.ReadSettings(config);

		_cancellation = CancellationToken.None;
		_fileId = "привет-как-делаdcd156a8-b6bd-4130-a2c7-8a38dbfebbc7";
		_s3Client = BenchmarkHelper.CreateStoragesClient(settings);

		// BenchmarkHelper.EnsureBucketExists(_storageClient, _cancellation);
		// BenchmarkHelper.EnsureFileExists(config, _storageClient, _fileId, _cancellation);
	}

	[GlobalCleanup]
	public void Clear()
	{
		_s3Client.Dispose();
	}

	[Benchmark]
	public async Task<int> JustDownload()
	{
		using var file = await _s3Client.GetFile(_fileId, _cancellation);

		return await BenchmarkHelper.ReadStreamMock(
			await file.GetStream(_cancellation),
			BenchmarkHelper.StreamBuffer,
			_cancellation);
	}
}
