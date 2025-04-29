namespace Storage;

internal sealed class DefaultArrayPool : IArrayPool
{
	public static readonly IArrayPool Instance = new DefaultArrayPool();

	private DefaultArrayPool()
	{
	}

	public T[] Rent<T>(int minimumLength)
	{
		return ArrayPool<T>.Shared.Rent(minimumLength);
	}

	public void Return<T>(T[] array, bool clear)
	{
		ArrayPool<T>.Shared.Return(array, clear);
	}
}
