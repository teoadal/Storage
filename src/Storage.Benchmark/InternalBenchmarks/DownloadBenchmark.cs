using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Storage.Benchmark.Utils;

namespace Storage.Benchmark.InternalBenchmarks;

[SimpleJob(RuntimeMoniker.Net70)]
[MeanColumn, MemoryDiagnoser]
public class DownloadBenchmark
{
    [Benchmark]
    public async Task<int> DownloadFile()
    {
        await using var storageFile = await _storageClient.GetFile(_fileId, _cancellation);
        if (!storageFile) ThrowException(storageFile.ToString());

        var result = ReadStream(storageFile.GetStream());
        await storageFile.DisposeAsync();

        return result;
    }

    #region Configuration

    private byte[] _buffer = null!;
    private CancellationToken _cancellation;
    private InputStream _inputData = null!;
    private string _fileId = null!;

    private StorageClient _storageClient = null!;

    [GlobalSetup]
    public void Config()
    {
        var config = BenchmarkHelper.ReadConfiguration();
        var settings = BenchmarkHelper.ReadSettings(config);

        _buffer = new byte[2048];
        _cancellation = new CancellationToken();
        _fileId = $"привет-как-дела{Guid.NewGuid()}";
        _inputData = BenchmarkHelper.ReadBigFile(config);

        _storageClient = BenchmarkHelper.CreateStoragesClient(settings);

        BenchmarkHelper.EnsureBucketExists(_storageClient, _cancellation);
        BenchmarkHelper.EnsureFileExists(_storageClient, _fileId, _inputData, _cancellation);
    }

    [GlobalCleanup]
    public void Clear()
    {
        _storageClient.Dispose();
        _inputData.Dispose();
    }

    private int ReadStream(Stream input)
    {
        var result = 0;
        while (input.Read(_buffer) != 0)
        {
            result++;
        }

        input.Dispose();

        return result;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowException(string? message = null)
    {
        throw new Exception(message);
    }

    #endregion
}