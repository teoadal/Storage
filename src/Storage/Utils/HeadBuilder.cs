namespace Storage.Utils;

internal sealed class HeadBuilder(string accessKey, string region, string service)
{
	// head
	public static readonly string[] S3Headers = // trimmed, lower invariant, ordered
	[
		"host",
		"x-amz-content-sha256",
		"x-amz-date",
	];


	private readonly string _headerEnd = $"/{region}/{service}/aws4_request, SignedHeaders={string.Join(';', S3Headers)}, Signature=";
	private readonly string _headerStart = $"AWS4-HMAC-SHA256 Credential={accessKey}/";



	[SkipLocalsInit]
	public string BuildAuthorizationValue(DateTimeOffset now, string signature)
	{
		using var builder = new ValueStringBuilder(stackalloc char[512]);

		builder.Append(_headerStart);
		builder.Append(now, Signature.Iso8601Date);
		builder.Append(_headerEnd);
		builder.Append(signature);

		return builder.Flush();
	}
}
