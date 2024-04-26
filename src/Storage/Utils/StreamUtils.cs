using System.Runtime.CompilerServices;

namespace Storage.Utils;

internal static class StreamUtils
{
    public static async Task<int> ReadTo(this Stream stream, byte[] buffer, CancellationToken cancellation)
    {
        var length = buffer.Length;
        var offset = 0;

        int written;
        do
        {
            written = await stream
                .ReadAsync(buffer.AsMemory(offset, length), cancellation)
                .ConfigureAwait(false);

            length -= written;
            offset += written;
        }
        while (written > 0 && length > 0);

        return offset;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long? TryGetLength(this Stream stream)
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
