using Storage.Utils;

namespace Storage.Tests.Utils;

public class CollectionUtilsShould
{
	private static readonly IArrayPool Pool = DefaultArrayPool.Instance;

	[Fact]
	public void ResizeArray()
	{
		var array = Pool.Rent<int>(5);

		var newLength = array.Length * 2;
		CollectionUtils.Resize(ref array, Pool, newLength);

		array.Length
			.Should()
			.BeGreaterThanOrEqualTo(newLength);
	}

	[Fact]
	public void ResizeEmptyArray()
	{
		const int newLength = 5;

		var emptyArray = Array.Empty<int>();
		CollectionUtils.Resize(ref emptyArray, Pool, newLength);

		emptyArray.Length
			.Should()
			.BeGreaterThanOrEqualTo(newLength);
	}
}
