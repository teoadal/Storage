namespace Storage;

/// <summary>
/// Интерфейс класса, который берёт массивы из пула и возвращает их в пул.
/// </summary>
public interface IArrayPool
{
	T[] Rent<T>(int minimumLength);

	void Return<T>(T[] array, bool clear = false);
}
