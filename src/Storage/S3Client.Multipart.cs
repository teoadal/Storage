using System.Text;
using Storage.Utils;
using static Storage.Utils.HashHelper;

namespace Storage;

/// <summary>
/// Функции управления multipart-загрузкой
/// </summary>
public sealed partial class S3Client
{
	internal async Task<bool> MultipartAbort(string encodedFileName, string uploadId, CancellationToken ct)
	{
		var url = $"{_bucket}/{encodedFileName}?uploadId={uploadId}";

		HttpResponseMessage? response = null;
		using (var request = new HttpRequestMessage(HttpMethod.Delete, url))
		{
			try
			{
				response = await Send(request, EmptyPayloadHash, ct).ConfigureAwait(false);
			}
			catch
			{
				// ignored
			}
		}

		if (response is null)
		{
			return false;
		}

#pragma warning disable CA1508
		var result = response is
		{
			IsSuccessStatusCode: true,
			StatusCode: HttpStatusCode.NoContent,
		};
#pragma warning restore CA1508

		response.Dispose();

		return result;
	}

	internal async Task<bool> MultipartComplete(
		string encodedFileName,
		string uploadId,
		string[] partTags,
		int tagsCount,
		CancellationToken ct)
	{
		var builder = StringUtils.GetBuilder();

		builder.Append("<CompleteMultipartUpload>");
		for (var i = 0; i < partTags.Length; i++)
		{
			if (i == tagsCount)
			{
				break;
			}

			builder.Append("<Part>");
			builder.Append("<PartNumber>", i + 1, "</PartNumber>");
			builder.Append("<ETag>", partTags[i], "</ETag>");
			builder.Append("</Part>");
		}

		var data = builder
			.Append("</CompleteMultipartUpload>")
			.Flush();

		var payloadHash = GetPayloadHash(data);

		HttpResponseMessage response;
		using (var request = new HttpRequestMessage(
			       HttpMethod.Post,
			       $"{_bucket}/{encodedFileName}?uploadId={uploadId}"))
		{
			using (var content = new StringContent(data, Encoding.UTF8))
			{
				request.Content = content;
				response = await Send(request, payloadHash, ct).ConfigureAwait(false);
			}
		}

		var result = response is { IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK };

		response.Dispose();
		return result;
	}

	internal async Task<string?> MultipartPutPart(
		string encodedFileName,
		string uploadId,
		int partNumber,
		byte[] partData,
		int partSize,
		CancellationToken ct)
	{
		var payloadHash = GetPayloadHash(partData.AsSpan(0, partSize));
		var url = $"{_bucket}/{encodedFileName}?partNumber={partNumber}&uploadId={uploadId}";

		HttpResponseMessage response;
		using (var request = new HttpRequestMessage(HttpMethod.Put, url))
		{
			using (var content = new ByteArrayContent(partData, 0, partSize))
			{
				content.Headers.Add("content-length", partSize.ToString());
				request.Content = content;

				response = await Send(request, payloadHash, ct).ConfigureAwait(false);
			}
		}

		var result = response is { IsSuccessStatusCode: true, StatusCode: HttpStatusCode.OK }
			? response.Headers.ETag?.Tag
			: null;

		response.Dispose();
		return result;
	}

	private async Task<bool> ExecuteMultipartUpload(
		string fileName,
		string contentType,
		Stream data,
		CancellationToken ct)
	{
		using var upload = await UploadFile(fileName, contentType, ct).ConfigureAwait(false);

		if (await upload.AddParts(data, ct).ConfigureAwait(false) && await upload.Complete(ct).ConfigureAwait(false))
		{
			return true;
		}

		await upload.Abort(ct).ConfigureAwait(false);
		return false;
	}

	private async Task<bool> ExecuteMultipartUpload(
		string fileName,
		string contentType,
		byte[] data,
		CancellationToken ct)
	{
		using var upload = await UploadFile(fileName, contentType, ct).ConfigureAwait(false);

		if (await upload.AddParts(data, ct).ConfigureAwait(false) && await upload.Complete(ct).ConfigureAwait(false))
		{
			return true;
		}

		await upload.Abort(ct).ConfigureAwait(false);
		return false;
	}

	private async Task<string> MultipartStart(string encodedFileName, string contentType, CancellationToken ct)
	{
		HttpResponseMessage response;
		using (var request = new HttpRequestMessage(HttpMethod.Post, $"{_bucket}/{encodedFileName}?uploads"))
		{
			using (var content = new ByteArrayContent([]))
			{
				content.Headers.Add("content-type", contentType);
				request.Content = content;

				response = await Send(request, EmptyPayloadHash, ct).ConfigureAwait(false);
			}
		}

		if (response.StatusCode is HttpStatusCode.OK)
		{
			var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
			var result = XmlStreamReader.ReadString(responseStream, "UploadId");

			await responseStream.DisposeAsync().ConfigureAwait(false);
			response.Dispose();

			return result;
		}

		Errors.UnexpectedResult(response);
		return string.Empty;
	}
}
