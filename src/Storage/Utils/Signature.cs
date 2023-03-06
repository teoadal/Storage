using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Storage.Utils;

internal sealed class Signature
{
    public const string Iso8601DateTime = "yyyyMMddTHHmmssZ";
    public const string Iso8601Date = "yyyyMMdd";

    public static readonly string EmptyPayloadHash = Sha256ToHex(string.Empty);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetPayloadHash(ReadOnlySpan<byte> data)
    {
        Span<byte> hashBuffer = stackalloc byte[32];
        return SHA256.TryHashData(data, hashBuffer, out _)
            ? ToHex(hashBuffer)
            : string.Empty;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetPayloadHash(string data) => Sha256ToHex(data);

    private static readonly HashSet<char> ValidUrlCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~".ToHashSet();

    private static SortedDictionary<string, string>? _headerSort = new();
    private readonly string _region;
    private readonly byte[] _secretKey;
    private readonly string _service;
    private readonly string _signaturePart;

    public Signature(string secretKey, string region, string service)
    {
        _region = region;
        _secretKey = Encoding.UTF8.GetBytes($"AWS4{secretKey}");
        _service = service;
        _signaturePart = $"/{region}/{service}/aws4_request\n";
    }

    /// <summary>
    /// Calculates request signature string using Signature Version 4.
    /// http://docs.aws.amazon.com/general/latest/gr/sigv4_signing.html
    /// </summary>
    public string CalculateSignature(
        HttpRequestMessage request, string payloadHash,
        string[] signedHeaders, DateTime requestDate)
    {
        Span<char> dateBuffer = stackalloc char[16];
        var builder = new ValueStringBuilder(stackalloc char[256]);

        builder.Append("AWS4-HMAC-SHA256\n");
        builder.Append(dateBuffer[..StringUtils.Format(ref dateBuffer, requestDate, Iso8601DateTime)]);
        builder.Append('\n');

        var formattedDate = dateBuffer[..StringUtils.Format(ref dateBuffer, requestDate, Iso8601Date)];

        builder.Append(formattedDate);
        builder.Append(_signaturePart);

        AppendCanonicalRequestHash(ref builder, request, signedHeaders, payloadHash);

        Span<byte> buffer = stackalloc byte[32];

        GetKeyedHash(ref buffer, _secretKey, formattedDate);
        GetKeyedHash(ref buffer, buffer, _region);
        GetKeyedHash(ref buffer, buffer, _service);
        GetKeyedHash(ref buffer, buffer, "aws4_request");
        GetKeyedHash(ref buffer, buffer, builder.AsReadonlySpan());

        builder.Dispose();

        return ToHex(buffer);
    }

    private static void AppendCanonicalHeaders(
        ref ValueStringBuilder builder,
        HttpRequestMessage request, string[] signedHeaders)
    {
        var sortedHeaders = Interlocked.Exchange(ref _headerSort, null) ?? new SortedDictionary<string, string>();
        foreach (var requestHeader in request.Headers)
        {
            var header = NormalizeHeader(requestHeader.Key);
            if (signedHeaders.Contains(header))
            {
                sortedHeaders.Add(header, string.Join(' ', requestHeader.Value).Trim());
            }
        }

        var content = request.Content;
        if (content != null)
        {
            foreach (var contentHeader in content.Headers)
            {
                var header = NormalizeHeader(contentHeader.Key);
                if (signedHeaders.Contains(header))
                {
                    sortedHeaders.Add(header, string.Join(' ', contentHeader.Value).Trim());
                }
            }
        }

        foreach (var (header, value) in sortedHeaders)
        {
            builder.Append(header);
            builder.Append(':');
            builder.Append(value);
            builder.Append('\n');
        }

        sortedHeaders.Clear();
        Interlocked.Exchange(ref _headerSort, sortedHeaders);
    }

    private static void AppendCanonicalQueryParameters(ref ValueStringBuilder builder, string? query)
    {
        if (string.IsNullOrEmpty(query) || query == "?") return;

        var scanIndex = 0;
        if (query[0] == '?') scanIndex = 1;

        var textLength = query.Length;
        var equalIndex = query.IndexOf('=');
        if (equalIndex == -1) equalIndex = textLength;

        while (scanIndex < textLength)
        {
            var delimiter = query.IndexOf('&', scanIndex);
            if (delimiter == -1) delimiter = textLength;

            if (equalIndex < delimiter)
            {
                while (scanIndex != equalIndex && char.IsWhiteSpace(query[scanIndex]))
                {
                    ++scanIndex;
                }

                AppendEncodedUrl(ref builder, UnescapeString(query.AsSpan(scanIndex, equalIndex - scanIndex)));
                builder.Append('=');
                AppendEncodedUrl(ref builder, UnescapeString(query.AsSpan(equalIndex + 1, delimiter - equalIndex - 1)));
                builder.Append('&');

                equalIndex = query.IndexOf('=', delimiter);
                if (equalIndex == -1) equalIndex = textLength;
            }
            else
            {
                if (delimiter > scanIndex)
                {
                    AppendEncodedUrl(ref builder, query.AsSpan(scanIndex, delimiter - scanIndex));
                    builder.Append('=');
                    AppendEncodedUrl(ref builder, string.Empty);
                    builder.Append('&');
                }
            }

            scanIndex = delimiter + 1;
        }

        builder.RemoveLast();
    }

    /// <summary>
    /// http://docs.aws.amazon.com/general/latest/gr/sigv4-create-canonical-request.html
    /// </summary>
    private static void AppendCanonicalRequestHash(
        ref ValueStringBuilder builder,
        HttpRequestMessage request, string[] signedHeaders, string payloadHash)
    {
        var canonical = new ValueStringBuilder(stackalloc char[512]);
        var uri = request.RequestUri!;

        const char newLine = '\n';

        canonical.Append(request.Method.Method);
        canonical.Append(newLine);
        canonical.Append(uri.AbsolutePath);
        canonical.Append(newLine);

        AppendCanonicalQueryParameters(ref canonical, uri.Query);
        canonical.Append(newLine);

        AppendCanonicalHeaders(ref canonical, request, signedHeaders);
        canonical.Append(newLine);

        var first = true;
        foreach (var header in signedHeaders)
        {
            if (first) first = false;
            else canonical.Append(';');

            canonical.Append(header);
        }

        canonical.Append(newLine);
        canonical.Append(payloadHash);

        AppendSha256ToHex(ref builder, canonical.AsReadonlySpan());

        canonical.Dispose();
    }

    [SuppressMessage("ReSharper", "InvertIf")]
    private static void AppendEncodedUrl(ref ValueStringBuilder builder, ReadOnlySpan<char> url)
    {
        Span<char> charBuffer = stackalloc char[2];

        var count = Encoding.UTF8.GetByteCount(url);
        using var memory = MemoryPool<byte>.Shared.Rent(count);
        if (MemoryMarshal.TryGetArray(memory.Memory[..count], out ArraySegment<byte> segment))
        {
            Encoding.UTF8.GetBytes(url, segment);
            foreach (char symbol in segment)
            {
                if (ValidUrlCharacters.Contains(symbol))
                {
                    builder.Append(symbol);
                }
                else
                {
                    builder.Append('%');
                    builder.Append(charBuffer[..StringUtils.FormatX2(ref charBuffer, symbol)]);
                }
            }
        }
    }

    [SuppressMessage("ReSharper", "InvertIf")]
    private static void AppendSha256ToHex(ref ValueStringBuilder builder, scoped ReadOnlySpan<char> value)
    {
        var count = Encoding.UTF8.GetByteCount(value);
        using var memory = MemoryPool<byte>.Shared.Rent(count);
        if (MemoryMarshal.TryGetArray(memory.Memory[..count], out ArraySegment<byte> segment))
        {
            Encoding.UTF8.GetBytes(value, segment);
            Span<byte> hashBuffer = stackalloc byte[32];
            if (SHA256.TryHashData(segment, hashBuffer, out _))
            {
                Span<char> buffer = stackalloc char[2];
                foreach (var element in hashBuffer)
                {
                    builder.Append(buffer[..StringUtils.FormatX2(ref buffer, element)]);
                }
            }
        }
    }

    [SuppressMessage("ReSharper", "InvertIf")]
    private static void GetKeyedHash(ref Span<byte> buffer, ReadOnlySpan<byte> key, scoped ReadOnlySpan<char> value)
    {
        var count = Encoding.UTF8.GetByteCount(value);
        using var memory = MemoryPool<byte>.Shared.Rent(count);
        if (MemoryMarshal.TryGetArray(memory.Memory[..count], out ArraySegment<byte> segment))
        {
            Encoding.UTF8.GetBytes(value, segment);
            HMACSHA256.TryHashData(key, segment, buffer, out _);
        }
    }

    private static string NormalizeHeader(string header)
    {
        var builder = new ValueStringBuilder(stackalloc char[header.Length]);
        var culture = CultureInfo.InvariantCulture;

        // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
        foreach (var ch in header)
        {
            if (ch == ' ') continue;

            builder.Append(char.ToLower(ch, culture));
        }

        return string.Intern(builder.Flush());
    }

    [SuppressMessage("ReSharper", "InvertIf")]
    private static string Sha256ToHex(ReadOnlySpan<char> value)
    {
        var count = Encoding.UTF8.GetByteCount(value);
        using var memory = MemoryPool<byte>.Shared.Rent(count);
        if (MemoryMarshal.TryGetArray(memory.Memory[..count], out ArraySegment<byte> segment))
        {
            Encoding.UTF8.GetBytes(value, segment);
            Span<byte> hashBuffer = stackalloc byte[32];
            if (SHA256.TryHashData(segment, hashBuffer, out _))
            {
                return ToHex(hashBuffer);
            }
        }

        return string.Empty;
    }

    // ReSharper disable once ParameterTypeCanBeEnumerable.Local
    private static string ToHex(ReadOnlySpan<byte> data)
    {
        Span<char> buffer = stackalloc char[2];
        var builder = new ValueStringBuilder(stackalloc char[64]);

        foreach (var element in data)
        {
            builder.Append(buffer[..StringUtils.FormatX2(ref buffer, element)]);
        }

        return builder.Flush();
    }

    private static string UnescapeString(ReadOnlySpan<char> query)
    {
        var data = new ValueStringBuilder(stackalloc char[query.Length]);
        foreach (var ch in query)
        {
            data.Append(ch == '+' ? ' ' : ch);
        }

        return Uri.UnescapeDataString(data.Flush());
    }
}