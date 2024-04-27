using System.Buffers;
using Storage.Utils;

namespace Storage.Tests.Utils;

public class CollectionUtilsShould
{
	private static readonly ArrayPool<int> Pool = ArrayPool<int>.Shared;

	[Fact]
	public void ResizeArray()
	{
		var array = Pool.Rent(5);

		var newLength = array.Length * 2;
		CollectionUtils.Resize(ref array, Pool, newLength);

		array.Length
			.Should().BeGreaterOrEqualTo(newLength);
	}

	[Fact]
	public void ResizeEmptyArray()
	{
		const int newLength = 5;

		var emptyArray = Array.Empty<int>();
		CollectionUtils.Resize(ref emptyArray, Pool, newLength);

		emptyArray.Length
			.Should().BeGreaterOrEqualTo(newLength);
	}
}
