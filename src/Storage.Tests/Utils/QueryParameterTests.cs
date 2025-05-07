using Storage.Utils;

namespace Storage.Tests.Utils;

public class QueryParameterTests
{

	[Fact]
	public void EmptyQuery()
	{
		var builder = new ValueStringBuilder(stackalloc char[512]);
		UrlUtils.AppendCanonicalQueryParameters(ref builder, null);
		Assert.Empty(builder.ToString());
		UrlUtils.AppendCanonicalQueryParameters(ref builder, "");
		Assert.Empty(builder.ToString());
		UrlUtils.AppendCanonicalQueryParameters(ref builder, "?");
		Assert.Empty(builder.ToString());

		builder.Dispose();
	}

	[Fact]
	public void SingleParameter()
	{
		var builder = new ValueStringBuilder(stackalloc char[512]);
		UrlUtils.AppendCanonicalQueryParameters(ref builder, "?key=value");
		Assert.Equal("key=value", builder.ToString());

		builder.Dispose();
	}

	[Fact]
	public void MultipleParameters()
	{
		var builder = new ValueStringBuilder(stackalloc char[512]);
		UrlUtils.AppendCanonicalQueryParameters(ref builder, "?key1=value1&key2=value2");
		Assert.Equal("key1=value1&key2=value2", builder.ToString());

		builder.Dispose();
	}

	[Fact]
	public void ParameterWithWhitespace()
	{
		var builder = new ValueStringBuilder(stackalloc char[512]);
		UrlUtils.AppendCanonicalQueryParameters(ref builder, "? key1 = value1 & key2 = value2 ");
		Assert.Equal("key1%20=%20value1%20&key2%20=%20value2%20", builder.ToString());

		builder.Dispose();
	}

	[Fact]
	public void ParameterWithoutValue()
	{
		var builder = new ValueStringBuilder(stackalloc char[512]);
		UrlUtils.AppendCanonicalQueryParameters(ref builder, "?key1&key2=value2");
		Assert.Equal("key1=&key2=value2", builder.ToString());

		builder.Dispose();
	}

	[Fact]
	public void ParameterWithEmptyValue()
	{
		var builder = new ValueStringBuilder(stackalloc char[512]);
		UrlUtils.AppendCanonicalQueryParameters(ref builder, "?key1=&key2=");
		Assert.Equal("key1=&key2=", builder.ToString());

		builder.Dispose();
	}

	[Fact]
	public void ParameterWithSpecialCharacters()
	{
		var builder = new ValueStringBuilder(stackalloc char[512]);
		UrlUtils.AppendCanonicalQueryParameters(ref builder, "?key1=value%20with%20spaces&key2=value%26with%26ampersands");
		Assert.Equal("key1=value%20with%20spaces&key2=value%26with%26ampersands", builder.ToString());

		builder.Dispose();
	}
}
