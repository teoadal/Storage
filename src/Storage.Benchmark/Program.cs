using BenchmarkDotNet.Running;

namespace Storage.Benchmark;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<S3Benchmark>();
    }
}