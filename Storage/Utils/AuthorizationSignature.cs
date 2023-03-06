namespace Storage.Utils;

internal readonly struct AuthorizationSignature
{
    private readonly string _end;
    private readonly string _start;

    public AuthorizationSignature(string accessKey, string region, string service, string[] headers)
    {
        _start = $"AWS4-HMAC-SHA256 Credential={accessKey}/";
        _end = $"/{region}/{service}/aws4_request, SignedHeaders={string.Join(';', headers)}, Signature=";
    }

    public string Build(DateTime now, string signature)
    {
        Span<char> buffer = stackalloc char[16];

        return StringUtils
            .GetBuilder()
            .Append(_start)
            .Append(buffer[..StringUtils.Format(ref buffer, now, Signature.Iso8601Date)])
            .Append(_end)
            .Append(signature)
            .Flush();
    }
}