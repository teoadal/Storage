using System.Buffers;
using System.Runtime.CompilerServices;

namespace Storage.Utils;

public static class CollectionUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Resize<T>(ref T[] array, ArrayPool<T> pool, int newLength, bool clear = false)
    {
        var newArray = pool.Rent(newLength);

        if (array.Length > 0)
        {
            Array.Copy(array, newArray, array.Length);
            pool.Return(array, clear);
        }

        array = newArray;
    }
}