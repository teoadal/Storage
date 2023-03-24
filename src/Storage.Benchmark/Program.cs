using BenchmarkDotNet.Running;
using Storage.Benchmark.InternalBenchmarks;

namespace Storage.Benchmark;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<DownloadBenchmark>();

        // const string fileId = "привет-как-делаdcd156a8-b6bd-4130-a2c7-8a38dbfebbc7";
        //
        // var config = BenchmarkHelper.ReadConfiguration();
        // var settings = BenchmarkHelper.ReadSettings(config);
        // var cancellation = new CancellationToken();
        // var storageClient = BenchmarkHelper.CreateStoragesClient(settings);
        //
        // var result = 0;
        // for (var i = 0; i < 50; i++)
        // {
        //     using var file = await storageClient.GetFile(fileId, cancellation);
        //     BenchmarkHelper.ReadStreamMock(await file.GetStream(cancellation));
        //
        //     Console.WriteLine(result++);
        // }
    }

}