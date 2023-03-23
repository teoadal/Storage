using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Storage.Benchmark.Utils;
using Storage.Utils;

namespace Storage.Benchmark.InternalBenchmarks;

[SimpleJob(RuntimeMoniker.Net70)]
[MeanColumn, MemoryDiagnoser]
public class SignatureBenchmark
{
    [Benchmark]
    public int Content() => _signature
        .Calculate(_request, _payloadHash, _headers, _now)
        .Length;

    [Benchmark]
    public int Url() => _signature
        .Calculate("http://subdomain-name.company-name.com/controller/method?arg=eto-arg", _now)
        .Length;

    #region Configuration

    private string[] _headers = null!;
    private DateTime _now;
    private HttpRequestMessage _request = null!;
    private string _payloadHash = null!;
    private Signature _signature = null!;

    [GlobalSetup]
    public void Config()
    {
        var config = BenchmarkHelper.ReadConfiguration();
        var data = BenchmarkHelper.ReadBigArray(config);
        var settings = BenchmarkHelper.ReadSettings(config);

        _headers = new[] {"host", "x-amz-content-sha256", "x-amz-date"};
        _now = DateTime.UtcNow;
        _request = new HttpRequestMessage(HttpMethod.Post, "http://company-name.com/controller");
        _payloadHash = HashHelper.GetPayloadHash(data);
        _signature = new Signature(settings.SecretKey, settings.Region, settings.Service);
    }

    #endregion
}