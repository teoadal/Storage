using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Storage.Benchmark.Utils;

namespace Storage.Benchmark.InternalBenchmarks;

[SimpleJob(RuntimeMoniker.Net70)]
[MarkdownExporterAttribute.GitHub]
[MeanColumn, MemoryDiagnoser]
public class MethodBenchmark
{
    [Benchmark]
    public async Task<int> BucketExists()
    {
        var bucketExistsResult = await _storageClient.BucketExists(_cancellation);
        return bucketExistsResult
            ? 1
            : ThrowException();
    }

    [Benchmark]
    public async Task<int> FileExistsFalse()
    {
        var fileExistsResult = await _storageClient.FileExists(_fileId, _cancellation);
        return fileExistsResult
            ? 1
            : ThrowException();
    }

    [Benchmark]
    public async Task<int> UploadFile()
    {
        _inputData.Seek(0, SeekOrigin.Begin);
        var fileUploadResult = await _storageClient.UploadFile(_fileId, _inputData, "application/pdf", _cancellation);
        return fileUploadResult
            ? 1
            : ThrowException();
    }

    [Benchmark]
    public async Task<int> UploadedFileExists()
    {
        var fileExistsResult = await _storageClient.FileExists(_fileId, _cancellation);
        return fileExistsResult
            ? 1
            : ThrowException();
    }

    [Benchmark]
    public async Task<long> UploadedFileDownload()
    {
        _outputData.Seek(0, SeekOrigin.Begin);
        var storageFile = await _storageClient.GetFile(_fileId, _cancellation);
        if (!storageFile) ThrowException(storageFile.ToString());

        await storageFile
            .GetStream()
            .CopyToAsync(_outputData, _cancellation);

        await storageFile.DisposeAsync();

        return _outputData.Length;
    }

    #region Configuration

    private CancellationToken _cancellation;
    private InputStream _inputData = null!;
    private string _fileId = null!;
    private MemoryStream _outputData = null!;

    private StorageClient _storageClient = null!;

    [GlobalSetup]
    public void Config()
    {
        var config = BenchmarkHelper.ReadConfiguration();
        var settings = BenchmarkHelper.ReadSettings(config);

        _cancellation = new CancellationToken();
        _fileId = $"привет-как-дела{Guid.NewGuid()}";
        _inputData = BenchmarkHelper.ReadBigFile(config);
        _outputData = new MemoryStream(new byte[_inputData.Length]);

        _storageClient = BenchmarkHelper.CreateStoragesClient(settings);

        BenchmarkHelper.EnsureBucketExists(_storageClient, _cancellation);
    }

    [GlobalCleanup]
    public void Clear()
    {
        _storageClient.Dispose();
        _inputData.Dispose();
        _outputData.Dispose();
    }

    private static int ThrowException(string? message = null)
    {
        throw new Exception(message);
    }

    #endregion
}