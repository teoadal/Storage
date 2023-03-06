﻿namespace Storage.Utils;

internal static class MultipartUploadResult
{
    private const string PropertyName = "UploadId";

    public static string GetUploadId(Stream stream)
    {
        var expectedIndex = 0;
        var sectionStarted = false;
        while (stream.CanRead)
        {
            var ch = (char) stream.ReadByte();
            if (sectionStarted)
            {
                if (ch == PropertyName[expectedIndex])
                {
                    if (++expectedIndex == PropertyName.Length) return ReadUploadId(stream);
                }
                else
                {
                    sectionStarted = false;
                }

                continue;
            }

            if (ch == '<') sectionStarted = true;
        }

        return string.Empty;
    }

    private static string ReadUploadId(Stream stream)
    {
        stream.ReadByte(); // skip <

        var builder = StringUtils.GetBuilder();
        while (stream.CanRead)
        {
            var ch = (char) stream.ReadByte();
            if (ch == '<') break;

            builder.Append(ch);
        }

        return builder.Flush();
    }
}