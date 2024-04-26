namespace Storage;

/// <summary>
///     Wrapper around <see cref="HttpResponseMessage" /> with a data of a file from storage
/// </summary>
[DebuggerDisplay("{ToString()}")]
public sealed class S3File : IDisposable
{
	private readonly HttpResponseMessage _response;

	internal S3File(HttpResponseMessage response)
	{
		_response = response;
	}

	/// <summary>
	///     Type of file content in MIME
	/// </summary>
	/// <remarks>It will be take from header "Content-Type" of <see cref="HttpResponseMessage" /></remarks>
	public string? ContentType
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _response.Content.Headers.ContentType?.MediaType;
	}

	/// <summary>
	///     Is the file data received successfully?
	/// </summary>
	public bool Exists
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _response.IsSuccessStatusCode;
	}

	/// <summary>
	///     Length of file data
	/// </summary>
	/// <remarks>It will be take from header "Content-Length" of <see cref="HttpResponseMessage" /></remarks>
	public long? Length
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _response.Content.Headers.ContentLength;
	}

	/// <summary>
	///     The code of storage response
	/// </summary>
	public HttpStatusCode StatusCode
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _response.StatusCode;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static implicit operator bool(S3File file)
	{
		return file._response.IsSuccessStatusCode;
	}

	public void Dispose()
	{
		_response.Dispose();
	}

	/// <summary>
	///     Gets stream of the file data from <see cref="HttpResponseMessage" />
	/// </summary>
	/// <returns>Stream of data</returns>
	/// <remarks>When stream will be closed the <see cref="HttpResponseMessage" /> will be disposed</remarks>
	public async Task<Stream> GetStream(CancellationToken cancellation)
	{
		if (!_response.IsSuccessStatusCode)
		{
			return Stream.Null;
		}

		var stream = await _response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
		return new S3Stream(_response, stream);
	}

	[ExcludeFromCodeCoverage]
	public override string ToString()
	{
		if (_response.IsSuccessStatusCode)
		{
			return $"OK (Length = {Length})";
		}

		var reasonPhrase = _response.ReasonPhrase;
		var statusCode = _response.StatusCode;
		return string.IsNullOrEmpty(reasonPhrase)
			? $"{statusCode} ({(int)statusCode})"
			: $"{statusCode} ({(int)statusCode}, '{reasonPhrase}')";
	}
}
