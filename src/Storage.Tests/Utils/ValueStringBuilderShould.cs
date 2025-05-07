using Storage.Utils;

namespace Storage.Tests.Utils;

public class ValueStringBuilderShould
{
	[Fact]
	public void Grow()
	{
		const int stringLength = 256;
		var chars = Enumerable.Range(0, stringLength).Select(i => (char)i);

		var builder = new ValueStringBuilder(stackalloc char[64]);
		foreach (var c in chars)
		{
			builder.Append(c);
		}

		builder.Length.Should().Be(stringLength);
		builder.Dispose();
	}

	[Fact]
	public void NotCreateEmptyString()
	{
		var builder = new ValueStringBuilder(stackalloc char[64]);
		builder
			.ToString()
			.Should().BeEmpty();
		builder.Dispose();
	}

	[Fact]
	public void RemoveLastCorrectly()
	{
		var builder = new ValueStringBuilder(stackalloc char[64]);
		builder.RemoveLast();

		builder.Length
			.Should().BeGreaterThan(-1);
		builder.Dispose();
	}
}
