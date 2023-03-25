using BenchmarkDotNet.Attributes;
using Storage.Benchmark.Utils;

namespace Storage.Benchmark.InternalBenchmarks;

[MeanColumn, MemoryDiagnoser]
[InProcess]
// [IterationCount(2)]
// [WarmupCount(10)]
public class DownloadBenchmark
{
    [Benchmark]
    public async Task<int> JustDownload()
    {
        using var file = await _storageClient.GetFile(_fileId, _cancellation);

        return await BenchmarkHelper.ReadStreamMock(
            await file.GetStream(_cancellation),
            BenchmarkHelper.StreamBuffer,
            _cancellation);
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
        _fileId = $"привет-как-делаdcd156a8-b6bd-4130-a2c7-8a38dbfebbc7";
        _storageClient = BenchmarkHelper.CreateStoragesClient(settings);

        // BenchmarkHelper.EnsureBucketExists(_storageClient, _cancellation);
        // BenchmarkHelper.EnsureFileExists(config, _storageClient, _fileId, _cancellation);
    }

    [GlobalCleanup]
    public void Clear()
    {
        _storageClient.Dispose();
    }

    #endregion
}