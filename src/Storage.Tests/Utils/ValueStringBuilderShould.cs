using Storage.Utils;

namespace Storage.Tests.Utils;

public class ValueStringBuilderShould
{
	[Fact]
	public void Grow()
	{
		const int stringLength = 256;
		var chars = Enumerable.Range(0, stringLength).Select(i => (char)i);

		var builder = new ValueStringBuilder(stackalloc char[64], DefaultArrayPool.Instance);
		foreach (var c in chars)
		{
			builder.Append(c);
		}

		builder.Length.Should().Be(stringLength);
	}

	[Fact]
	public void NotCreateEmptyString()
	{
		var builder = new ValueStringBuilder(stackalloc char[64], DefaultArrayPool.Instance);
		builder
			.ToString()
			.Should().BeEmpty();
	}

	[Fact]
	public void RemoveLastCorrectly()
	{
		var builder = new ValueStringBuilder(stackalloc char[64], DefaultArrayPool.Instance);
		builder.RemoveLast();

		builder.Length
			.Should().BeGreaterThan(-1);
	}
}
