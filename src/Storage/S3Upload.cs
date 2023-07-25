using System.Buffers;
using System.Runtime.CompilerServices;
using Storage.Utils;

namespace Storage;

public sealed class S3Upload : IDisposable
{
	private readonly S3Client _client;
	private readonly string _encodedFileName;
	private readonly string _fileName;
	private readonly string _uploadId;

	private byte[]? _byteBuffer;
	private bool _disposed;
	private int _partCount;
	private string[] _parts;

	internal S3Upload(S3Client client, string fileName, string encodedFileName, string uploadId)
	{
		_fileName = fileName;
		_uploadId = uploadId;

		_client = client;
		_encodedFileName = encodedFileName;
		_parts = ArrayPool<string>.Shared.Rent(16);
	}

	public long Written
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get;
		private set;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		Array.Clear(_parts, 0, _partCount);
		ArrayPool<string>.Shared.Return(_parts);
		_parts = null!;

		if (_byteBuffer is not null)
		{
			ArrayPool<byte>.Shared.Return(_byteBuffer);
			_byteBuffer = null;
		}

		_disposed = true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Task Abort(CancellationToken cancellation)
	{
		return _client.MultipartAbort(_encodedFileName, _uploadId, cancellation);
	}

	public Task<bool> Complete(CancellationToken cancellation)
	{
		return _partCount == 0
			? Task.FromResult(false)
			: _client.MultipartComplete(_encodedFileName, _uploadId, _parts, _partCount, cancellation);
	}

	public Task<bool> Upload(Stream data, CancellationToken cancellation)
	{
		_byteBuffer ??= ArrayPool<byte>.Shared.Rent(S3Client.DefaultPartSize);
		return Upload(data, _byteBuffer, cancellation);
	}

	public async Task<bool> Upload(Stream data, byte[] buffer, CancellationToken token)
	{
		while (true)
		{
			var written = await data.ReadTo(buffer, token);
			if (written is 0)
			{
				break;
			}

			if (!await Upload(buffer, written, token))
			{
				return false;
			}
		}

		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Task<bool> Upload(byte[] data, CancellationToken cancellation)
	{
		return Upload(data, data.Length, cancellation);
	}

	public async Task<bool> Upload(byte[] data, int length, CancellationToken token)
	{
		var partId = await _client.MultipartUpload(
			_encodedFileName, _uploadId, _partCount + 1, data, length, token);

		if (string.IsNullOrEmpty(partId))
		{
			return false;
		}

		if (_parts.Length == _partCount)
		{
			CollectionUtils.Resize(ref _parts, ArrayPool<string>.Shared, _partCount * 2);
		}

		_parts[_partCount++] = partId;

		Written += length;

		return true;
	}
}
