﻿using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Storage.Utils;

internal static class StringUtils
{
    private static StringBuilder? _sharedBuilder;

    #region Append

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder Append(this StringBuilder builder, string start, char ch) => builder
        .Append(start)
        .Append(ch);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder Append(this StringBuilder builder, string start, int middle, string end) => builder
        .Append(start)
        .Append(middle)
        .Append(end);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder Append(this StringBuilder builder, string start, string middle, string end) => builder
        .Append(start)
        .Append(middle)
        .Append(end);

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringBuilder GetBuilder() => Interlocked.Exchange(ref _sharedBuilder, null)
                                                ?? new StringBuilder(4096);

    public static int Format(ref Span<char> buffer, DateTime dateTime, string format)
    {
        dateTime.TryFormat(buffer, out var written, format, CultureInfo.InvariantCulture);
        return written;
    }

    public static int FormatX2(ref Span<char> buffer, byte value)
    {
        value.TryFormat(buffer, out var written, "x2", CultureInfo.InvariantCulture);
        return written;
    }

    public static int FormatX2(ref Span<char> buffer, char value)
    {
        ((int) value).TryFormat(buffer, out var written, "x2", CultureInfo.InvariantCulture);
        return written;
    }

    public static string Flush(this StringBuilder builder)
    {
        var result = builder.ToString();
        builder.Clear();

        Interlocked.Exchange(ref _sharedBuilder, builder);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Return(this StringBuilder builder)
    {
        builder.Clear();
        Interlocked.Exchange(ref _sharedBuilder, builder);
    }
}