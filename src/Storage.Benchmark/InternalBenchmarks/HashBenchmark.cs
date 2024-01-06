using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Storage.Benchmark.Utils;
using Storage.Utils;

namespace Storage.Benchmark.InternalBenchmarks;

[SimpleJob(RuntimeMoniker.Net80)]
[MeanColumn]
[MemoryDiagnoser]
public class HashBenchmark
{
	private byte[] _byteData = null!;
	private string _stringData = null!;

	[GlobalSetup]
	public void Config()
	{
		var config = BenchmarkHelper.ReadConfiguration();

		_byteData = BenchmarkHelper.ReadBigArray(config);
		_stringData = "C10F4FFD-BB46-452C-B054-C595EB92248E";
	}

	[Benchmark]
	public int ByteHash()
	{
		return HashHelper
			.GetPayloadHash(_byteData)
			.Length;
	}

	[Benchmark]
	public int StringHash()
	{
		return HashHelper
			.GetPayloadHash(_stringData)
			.Length;
	}
}
