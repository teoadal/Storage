using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace Storage.Utils;

internal readonly struct HttpHelper
{
    public static readonly HashSet<char> ValidUrlCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~".ToHashSet();

    public static bool AppendEncodedName(ref ValueStringBuilder builder, ReadOnlySpan<char> name)
    {
        var count = Encoding.UTF8.GetByteCount(name);
        var encoded = false;
        using var memory = MemoryPool<byte>.Shared.Rent(count);

        // ReSharper disable once InvertIf
        if (MemoryMarshal.TryGetArray(memory.Memory[..count], out ArraySegment<byte> segment))
        {
            Span<char> charBuffer = stackalloc char[2];
            Span<char> upperBuffer = stackalloc char[2];

            var validCharacters = ValidUrlCharacters;
            Encoding.UTF8.GetBytes(name, segment);

            foreach (char symbol in segment)
            {
                if (validCharacters.Contains(symbol))
                {
                    builder.Append(symbol);
                }
                else
                {
                    builder.Append('%');

                    StringUtils.FormatX2(ref charBuffer, symbol);
                    MemoryExtensions.ToUpperInvariant(charBuffer, upperBuffer);
                    builder.Append(upperBuffer);

                    encoded = true;
                }
            }
        }

        return encoded;
    }

    public static string EncodeName(string fileName)
    {
        var builder = new ValueStringBuilder(stackalloc char[fileName.Length]);
        var encoded = AppendEncodedName(ref builder, fileName);

        return encoded
            ? builder.Flush()
            : fileName;
    }

    private readonly string _headerEnd;
    private readonly string _headerStart;

    private readonly string _urlMiddle;
    private readonly string _urlStart;

    public HttpHelper(string accessKey, string region, string service, string[] signedHeaders)
    {
        _headerStart = $"AWS4-HMAC-SHA256 Credential={accessKey}/";
        _headerEnd = $"/{region}/{service}/aws4_request, SignedHeaders={string.Join(';', signedHeaders)}, Signature=";

        _urlStart = $"?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential={accessKey}%2F";
        _urlMiddle = $"%2F{region}%2F{service}%2Faws4_request";
    }

    public string BuildHeader(DateTime now, string signature)
    {
        Span<char> buffer = stackalloc char[16];
        var builder = new ValueStringBuilder(stackalloc char[512]);

        builder.Append(_headerStart);
        builder.Append(buffer[..StringUtils.Format(ref buffer, now, Signature.Iso8601Date)]);
        builder.Append(_headerEnd);
        builder.Append(signature);

        return builder.Flush();
    }

    public string BuildUrl(string bucket, string fileName, DateTime now, TimeSpan expires)
    {
        Span<char> dateBuffer = stackalloc char[16];
        var builder = new ValueStringBuilder(stackalloc char[512]);

        builder.Append(bucket);
        builder.Append('/');

        AppendEncodedName(ref builder, fileName);

        builder.Append(_urlStart);
        builder.Append(dateBuffer[..StringUtils.Format(ref dateBuffer, now, Signature.Iso8601Date)]);
        builder.Append(_urlMiddle);

        builder.Append("&X-Amz-Date=");
        builder.Append(dateBuffer[..StringUtils.Format(ref dateBuffer, now, Signature.Iso8601DateTime)]);
        builder.Append("&X-Amz-Expires=");
        builder.Append(expires.TotalSeconds);

        builder.Append($"&X-Amz-SignedHeaders=host");

        return builder.Flush();
    }
}