using System.Buffers;
using System.Globalization;

namespace Storage.Utils;

internal ref struct ValueStringBuilder(Span<char> initialBuffer)
{
	private Span<char> _buffer = initialBuffer;
	private int _length = 0;
	private char[]? _array = null;

	// ReSharper disable once ConvertToAutoPropertyWithPrivateSetter
	public readonly int Length
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => _length;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Append(char c)
	{
		var pos = _length;
		if ((uint)pos < (uint)_buffer.Length)
		{
			_buffer[pos] = c;
			_length = pos + 1;
		}
		else
		{
			GrowAndAppend(c);
		}
	}

	public void Append(int value)
	{
		Span<char> buffer = stackalloc char[10];
		var pos = _length;
		if (value.TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture))
		{
			if (pos > _buffer.Length - written)
			{
				Grow(written);
			}

			buffer.CopyTo(_buffer[pos..]);

			_length = pos + written;
		}
		else
		{
			Errors.CantFormatToString(value);
		}
	}

	public void Append(DateTime value, string format)
	{
		Span<char> buffer = stackalloc char[16];
		var pos = _length;
		if (value.TryFormat(buffer, out var written, format, CultureInfo.InvariantCulture))
		{
			if (pos > _buffer.Length - written)
			{
				Grow(written);
			}

			buffer.CopyTo(_buffer[pos..]);

			_length = pos + written;
		}
		else
		{
			Errors.CantFormatToString(value);
		}
	}

	public void Append(double value)
	{
		Span<char> buffer = stackalloc char[32];
		var pos = _length;
		if (value.TryFormat(buffer, out var written, default, CultureInfo.InvariantCulture))
		{
			if (pos > _buffer.Length - written)
			{
				Grow(written);
			}

			buffer.CopyTo(_buffer[pos..]);

			_length = pos + written;
		}
		else
		{
			Errors.CantFormatToString(value);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Append(string? s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return;
		}

		var pos = _length;
		if (s.Length == 1 && (uint)pos < (uint)_buffer.Length)
		{
			_buffer[pos] = s[0];
			_length = pos + 1;
		}
		else
		{
			Append(s.AsSpan());
		}
	}

	public void Append(scoped Span<char> value)
	{
		var pos = _length;
		var valueLength = value.Length;

		if (pos > _buffer.Length - valueLength)
		{
			Grow(valueLength);
		}

		value.CopyTo(_buffer[pos..]);

		_length = pos + valueLength;
	}

	public void Append(ReadOnlySpan<char> value)
	{
		var pos = _length;
		var valueLength = value.Length;

		if (pos > _buffer.Length - valueLength)
		{
			Grow(valueLength);
		}

		value.CopyTo(_buffer[pos..]);

		_length = pos + valueLength;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly ReadOnlySpan<char> AsReadonlySpan()
	{
		return _length == 0
			? ReadOnlySpan<char>.Empty
			: _buffer[.._length];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public readonly void Dispose()
	{
		var toReturn = _array;
		if (toReturn is not null)
		{
			ArrayPool<char>.Shared.Return(toReturn);
		}
	}

	public readonly string Flush()
	{
		var result = _length == 0
			? string.Empty
			: _buffer[.._length].ToString();

		Dispose();

		return result;
	}

	public void RemoveLast()
	{
		if (_length == 0)
		{
			return;
		}

		_length--;
	}

	[ExcludeFromCodeCoverage]
	public override readonly string ToString()
	{
		return _length is 0
			? string.Empty
			: _buffer[.._length].ToString();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void GrowAndAppend(char c)
	{
		Grow(1);
		Append(c);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private void Grow(int additionalCapacityBeyondPos)
	{
		const uint arrayMaxLength = 0x7FFFFFC7; // same as Array.MaxLength

		var newCapacity = (int)Math.Max(
			(uint)(_length + additionalCapacityBeyondPos),
			Math.Min((uint)_buffer.Length * 2, arrayMaxLength));

		var poolArray = ArrayPool<char>.Shared.Rent(newCapacity);

		_buffer[.._length].CopyTo(poolArray);

		var toReturn = _array;
		_buffer = _array = poolArray;
		if (toReturn is not null)
		{
			ArrayPool<char>.Shared.Return(toReturn);
		}
	}
}
