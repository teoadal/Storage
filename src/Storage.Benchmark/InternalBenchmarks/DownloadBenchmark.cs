using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Storage.Benchmark.Utils;

namespace Storage.Benchmark.InternalBenchmarks;

[SimpleJob(RuntimeMoniker.Net70)]
[MeanColumn, MemoryDiagnoser]
public class DownloadBenchmark
{
    [Benchmark]
    public async Task<int> JustDownload()
    {
        using var file = await _storageClient.GetFile(_fileId, _cancellation);
        return BenchmarkHelper.ReadStreamMock(await file.GetStream(_cancellation));
    }

    #region Configuration

    private CancellationToken _cancellation;
    private string _fileId = null!;
    private StorageClient _storageClient = null!;

    [GlobalSetup]
    public void Config()
    {
        var config = BenchmarkHelper.ReadConfiguration();
        var settings = BenchmarkHelper.ReadSettings(config);

        _cancellation = new CancellationToken();
        _fileId = $"привет-как-дела{Guid.NewGuid()}";
        _storageClient = BenchmarkHelper.CreateStoragesClient(settings);

        BenchmarkHelper.EnsureBucketExists(_storageClient, _cancellation);
        BenchmarkHelper.EnsureFileExists(config, _storageClient, _fileId, _cancellation);
    }

    [GlobalCleanup]
    public void Clear()
    {
        _storageClient.Dispose();
    }

    #endregion
}