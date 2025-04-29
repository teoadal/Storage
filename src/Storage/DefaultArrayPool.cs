namespace Storage;

public sealed class DefaultArrayPool
{
    class ArrayPool : IArrayPool
	{
		public T[] Rent<T>(int minimumLength) => ArrayPool<T>.Shared.Rent(minimumLength);
		public void Return<T>(T[] array, bool clear = false) => ArrayPool<T>.Shared.Return(array, clear);
	}


	private static Lazy<IArrayPool> s_instance = new(new ArrayPool());

	public static IArrayPool Instance => s_instance.Value;


	public static bool SetDefault(IArrayPool arrayPool)
	{
		ArgumentNullException.ThrowIfNull(arrayPool);

		if (!s_instance.IsValueCreated)
		{
			s_instance = new Lazy<IArrayPool>(arrayPool);
			return true;
		}
		return false;
	}
}
