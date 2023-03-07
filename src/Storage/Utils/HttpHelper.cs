using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;

namespace Storage.Utils;

internal readonly struct HttpHelper
{
    public static readonly HashSet<char> ValidUrlCharacters =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~".ToHashSet();

    public static void AppendEncodedName(ref ValueStringBuilder builder, string fileName)
    {
        Span<char> charBuffer = stackalloc char[2];
        Span<char> upperBuffer = stackalloc char[2];

        var count = Encoding.UTF8.GetByteCount(fileName);
        using var memory = MemoryPool<byte>.Shared.Rent(count);
        if (MemoryMarshal.TryGetArray(memory.Memory[..count], out ArraySegment<byte> segment))
        {
            Encoding.UTF8.GetBytes(fileName, segment);
            foreach (char symbol in segment)
            {
                if (ValidUrlCharacters.Contains(symbol))
                {
                    builder.Append(symbol);
                }
                else
                {
                    builder.Append('%');

                    StringUtils.FormatX2(ref charBuffer, symbol);
                    MemoryExtensions.ToUpperInvariant(charBuffer, upperBuffer);
                    builder.Append(upperBuffer);
                }
            }
        }
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

    public string BuildUrl(string bucket, string fileName, DateTime now, string expires)
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
        builder.Append(expires);

        builder.Append($"&X-Amz-SignedHeaders=host");

        return builder.Flush();
    }

    public static string EncodeName(string fileName)
    {
        Span<char> charBuffer = stackalloc char[2];
        Span<char> upperBuffer = stackalloc char[2];

        var builder = StringUtils.GetBuilder();
        var count = Encoding.UTF8.GetByteCount(fileName);
        var encoded = false;

        using var memory = MemoryPool<byte>.Shared.Rent(count);
        if (MemoryMarshal.TryGetArray(memory.Memory[..count], out ArraySegment<byte> segment))
        {
            Encoding.UTF8.GetBytes(fileName, segment);
            foreach (char symbol in segment)
            {
                if (ValidUrlCharacters.Contains(symbol))
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

        if (encoded)
        {
            return builder.Flush();
        }

        builder.Return();
        return fileName;
    }
}