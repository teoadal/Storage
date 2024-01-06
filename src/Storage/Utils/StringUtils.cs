using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Storage.Utils;

internal static class StringUtils
{
    private static StringBuilder? _sharedBuilder;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder Append(this StringBuilder builder, string start, int middle, string end)
    {
	    return builder
		    .Append(start)
		    .Append(middle)
		    .Append(end);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder Append(this StringBuilder builder, string start, string middle, string end)
    {
	    return builder
		    .Append(start)
		    .Append(middle)
		    .Append(end);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder GetBuilder()
    {
	    return Interlocked.Exchange(ref _sharedBuilder, null)
	           ?? new StringBuilder(4096);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Format(ref Span<char> buffer, DateTime dateTime, string format)
    {
        return dateTime.TryFormat(buffer, out var written, format, CultureInfo.InvariantCulture)
            ? written
            : -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FormatX2(ref Span<char> buffer, byte value)
    {
        return value.TryFormat(buffer, out var written, "x2", CultureInfo.InvariantCulture)
            ? written
            : -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FormatX2(ref Span<char> buffer, char value)
    {
        return ((int)value).TryFormat(buffer, out var written, "x2", CultureInfo.InvariantCulture)
            ? written
            : -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Flush(this StringBuilder builder)
    {
        var result = builder.ToString();
        builder.Clear();

        Interlocked.Exchange(ref _sharedBuilder, builder);
        return result;
    }
}
