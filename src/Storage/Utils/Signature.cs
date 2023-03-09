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

    private static SortedDictionary<string, string>? _headerSort = new();
    private readonly string _region;
    private readonly byte[] _secretKey;
    private readonly string _scope;
    private readonly string _service;

    public Signature(string secretKey, string region, string service)
    {
        _region = region;
        _secretKey = Encoding.UTF8.GetBytes($"AWS4{secretKey}");
        _scope = $"/{region}/{service}/aws4_request\n";
        _service = service;
    }

    public string Calculate(
        HttpRequestMessage request,
        string payload, string[] signedHeaders, DateTime requestDate)
    {
        var builder = new ValueStringBuilder(stackalloc char[512]);

        AppendStringToSign(ref builder, requestDate);
        AppendCanonicalRequestHash(ref builder, request, signedHeaders, payload);

        Span<byte> signature = stackalloc byte[32];
        CreateSigningKey(ref signature, requestDate);

        signature = signature[..Sign(ref signature, signature, builder.AsReadonlySpan())];
        builder.Dispose();

        return HashHelper.ToHex(signature);
    }

    public string Calculate(string url, DateTime requestDate)
    {
        var builder = new ValueStringBuilder(stackalloc char[512]);

        AppendStringToSign(ref builder, requestDate);
        AppendCanonicalRequestHash(ref builder, url);

        Span<byte> signature = stackalloc byte[32];
        CreateSigningKey(ref signature, requestDate);

        signature = signature[..Sign(ref signature, signature, builder.AsReadonlySpan())];
        builder.Dispose();

        return HashHelper.ToHex(signature);
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

                var name = UnescapeString(query.AsSpan(scanIndex, equalIndex - scanIndex));
                HttpHelper.AppendEncodedName(ref builder, name);
                builder.Append('=');

                var value = UnescapeString(query.AsSpan(equalIndex + 1, delimiter - equalIndex - 1));
                HttpHelper.AppendEncodedName(ref builder, value);
                builder.Append('&');

                equalIndex = query.IndexOf('=', delimiter);
                if (equalIndex == -1) equalIndex = textLength;
            }
            else
            {
                if (delimiter > scanIndex)
                {
                    HttpHelper.AppendEncodedName(ref builder, query.AsSpan(scanIndex, delimiter - scanIndex));
                    builder.Append('=');
                    HttpHelper.AppendEncodedName(ref builder, string.Empty);
                    builder.Append('&');
                }
            }

            scanIndex = delimiter + 1;
        }

        builder.RemoveLast();
    }

    private static void AppendCanonicalRequestHash(
        ref ValueStringBuilder builder, HttpRequestMessage request,
        string[] signedHeaders, string payload)
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
        canonical.Append(payload);

        AppendSha256ToHex(ref builder, canonical.AsReadonlySpan());

        canonical.Dispose();
    }

    private static void AppendCanonicalRequestHash(ref ValueStringBuilder builder, string url)
    {
        var uri = new Uri(url);

        var canonical = new ValueStringBuilder(stackalloc char[256]);
        canonical.Append("GET\n"); // canonical request
        canonical.Append(uri.AbsolutePath);
        canonical.Append('\n');
        canonical.Append(uri.Query.AsSpan(1));
        canonical.Append('\n');
        canonical.Append("host:");
        canonical.Append(uri.Host);
        
        if (!uri.IsDefaultPort)
        {
            canonical.Append(':');
            canonical.Append(uri.Port);
            
        }
        
        canonical.Append("\n\n");
        canonical.Append("host\n");
        canonical.Append("UNSIGNED-PAYLOAD");

        AppendSha256ToHex(ref builder, canonical.AsReadonlySpan());

        canonical.Dispose();
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AppendStringToSign(ref ValueStringBuilder builder, DateTime requestDate)
    {
        Span<char> dateBuffer = stackalloc char[16];

        builder.Append("AWS4-HMAC-SHA256\n");
        builder.Append(dateBuffer[..StringUtils.Format(ref dateBuffer, requestDate, Iso8601DateTime)]);
        builder.Append("\n");
        builder.Append(dateBuffer[..StringUtils.Format(ref dateBuffer, requestDate, Iso8601Date)]);
        builder.Append(_scope);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CreateSigningKey(ref Span<byte> buffer, DateTime requestDate)
    {
        Span<char> dateBuffer = stackalloc char[16];

        Sign(ref buffer, _secretKey, dateBuffer[..StringUtils.Format(ref dateBuffer, requestDate, Iso8601Date)]);
        Sign(ref buffer, buffer, _region);
        Sign(ref buffer, buffer, _service);
        Sign(ref buffer, buffer, "aws4_request");
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
    private static int Sign(ref Span<byte> buffer, ReadOnlySpan<byte> key, scoped ReadOnlySpan<char> content)
    {
        var count = Encoding.UTF8.GetByteCount(content);
        using var memory = MemoryPool<byte>.Shared.Rent(count);
        if (MemoryMarshal.TryGetArray(memory.Memory[..count], out ArraySegment<byte> segment))
        {
            Encoding.UTF8.GetBytes(content, segment);
            if (HMACSHA256.TryHashData(key, segment, buffer, out var written)) return written;
        }

        return -1;
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