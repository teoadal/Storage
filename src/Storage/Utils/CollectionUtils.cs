namespace Storage.Utils;

internal static class CollectionUtils
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Resize<T>(ref T[] array, IArrayPool arrayPool, int newLength, bool clear = false)
	{
		var newArray = arrayPool.Rent<T>(newLength);

		if (array.Length > 0)
		{
			Array.Copy(array, newArray, array.Length);
			arrayPool.Return(array, clear);
		}

		array = newArray;
	}
}
