namespace Storage.Utils;

internal readonly struct HttpHelper
{
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
        builder.Append(fileName);

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
}