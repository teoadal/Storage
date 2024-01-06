namespace Storage.Benchmark.Utils;

internal sealed class InputStream : Stream
{
	private readonly Stream _baseStream;

	public InputStream(byte[] data)
	{
		_baseStream = new MemoryStream();
		_baseStream.Write(data);
	}

	public override long Position
	{
		get => _baseStream.Position;
		set => _baseStream.Position = value;
	}

	public override bool CanRead => true;

	public override bool CanSeek => true;

	public override bool CanWrite => false;

	public override long Length => _baseStream.Length;

	public override void Close()
	{
		_baseStream.Seek(0, SeekOrigin.Begin);
	}

	public override void Flush()
	{
		_baseStream.Flush();
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		return _baseStream.Read(buffer, offset, count);
	}

	public override long Seek(long offset, SeekOrigin origin)
	{
		return _baseStream.Seek(offset, origin);
	}

	public override void SetLength(long value)
	{
		_baseStream.SetLength(value);
	}

	public override void Write(byte[] buffer, int offset, int count)
	{
		_baseStream.Write(buffer, offset, count);
	}
}
