using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Storage.Benchmark;

//BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new DebugInProcessConfig());

BenchmarkRunner.Run<S3Benchmark>(ManualConfig
    .CreateMinimumViable()
    .WithOptions(ConfigOptions.DisableOptimizationsValidator)); // for Yandex strange client