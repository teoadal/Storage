using BenchmarkDotNet.Running;
using Storage.Benchmark.InternalBenchmarks;

namespace Storage.Benchmark;

public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkRunner.Run<MethodBenchmark>();
    }
}