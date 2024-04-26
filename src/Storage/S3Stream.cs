namespace Storage;

internal sealed class S3Stream(HttpResponseMessage response, Stream stream) : Stream
{
	private long? _length;

	public override bool CanRead => stream.CanRead;

	public override bool CanSeek => stream.CanSeek;

	public override bool CanWrite => stream.CanWrite;

	public override long Length
	{
		get
		{
			_length ??= response.Content.Headers.ContentLength ?? stream.Length;
			return _length.Value;
		}
	}

	[ExcludeFromCodeCoverage]
	public override long Position
	{
		get => stream.Position;
		set => stream.Position = value;
	}

	public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		return stream.ReadAsync(buffer, offset, count, cancellationToken);
	}

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
	{
		return stream.ReadAsync(buffer, cancellationToken);
	}

	public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
	{
		return stream.CopyToAsync(destination, bufferSize, cancellationToken);
	}

	[ExcludeFromCodeCoverage]
	public override void Flush()
	{
		stream.Flush();
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		return stream.Read(buffer, offset, count);
	}

	[ExcludeFromCodeCoverage]
	public override long Seek(long offset, SeekOrigin origin)
	{
		return stream.Seek(offset, origin);
	}

	[ExcludeFromCodeCoverage]
	public override void SetLength(long value)
	{
		stream.SetLength(value);
	}

	[ExcludeFromCodeCoverage]
	public override void Write(byte[] buffer, int offset, int count)
	{
		stream.Write(buffer, offset, count);
	}

	protected override void Dispose(bool disposing)
	{
		stream.Dispose();
		response.Dispose();

		base.Dispose(true);
	}
}
