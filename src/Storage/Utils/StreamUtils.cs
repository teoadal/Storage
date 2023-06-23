using System.Runtime.CompilerServices;

namespace Storage.Utils;

internal static class StreamUtils
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long? TryGetLength(Stream stream)
    {
        try
        {
            var length = stream.Length;
            return length == 0 ? null : length;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}