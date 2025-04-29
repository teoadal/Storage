namespace Storage.Utils;

internal static class XmlStreamReader
{
	[SkipLocalsInit]
	public static string ReadString(Stream stream, ReadOnlySpan<char> elementName, int valueBufferLength = 256)
	{
		Span<char> buffer = stackalloc char[valueBufferLength];

		var written = ReadTo(stream, elementName, ref buffer);
		return written is -1
			? string.Empty
			: buffer[..written].ToString();
	}

	private static int ReadTo(Stream stream, ReadOnlySpan<char> elementName, ref Span<char> valueBuffer)
	{
		var expectedIndex = 0;
		var propertyLength = elementName.Length;
		var sectionStarted = false;

		while (true)
		{
			var nextByte = stream.ReadByte();
			if (nextByte is -1)
			{
				break;
			}

			var nextChar = (char)nextByte;
			if (sectionStarted)
			{
				if (nextChar == elementName[expectedIndex])
				{
					if (++expectedIndex == propertyLength)
					{
						if ((char)stream.ReadByte() is '>')
						{
							return ReadValue(stream, ref valueBuffer);
						}

						expectedIndex = 0;
						sectionStarted = false;
					}
				}
				else
				{
					sectionStarted = false;
				}

				continue;
			}

			if (nextChar is '<')
			{
				sectionStarted = true;
			}
		}

		return -1;
	}

	private static int ReadValue(Stream stream, ref Span<char> valueBuffer)
	{
		var index = 0;
		while (true)
		{
			var nextByte = stream.ReadByte();
			if (nextByte is -1)
			{
				break;
			}

			var nextChar = (char)nextByte;
			if (nextChar is '<')
			{
				return index;
			}

			valueBuffer[index++] = nextChar;
		}

		return -1;
	}
}
