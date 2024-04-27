using System.Net;
using Storage.Tests.Utils;
using static Storage.Tests.Utils.StorageFixture;

namespace Storage.Tests;

public sealed class ObjectShould : IClassFixture<StorageFixture>
{
	private readonly S3Client _client;
	private readonly CancellationToken _ct;
	private readonly StorageFixture _fixture;
	private readonly S3Client _notExistsBucketClient; // don't dispose it

	public ObjectShould(StorageFixture fixture)
	{
		_ct = CancellationToken.None;
		_client = fixture.S3Client;
		_fixture = fixture;

		var settings = _fixture.Settings;
		_notExistsBucketClient = new S3Client(
			new S3Settings
			{
				AccessKey = settings.AccessKey,
				Bucket = _fixture.Create<string>(),
				EndPoint = settings.EndPoint,
				Port = settings.Port,
				SecretKey = settings.SecretKey,
				UseHttps = settings.UseHttps,
			},
			_fixture.HttpClient);
	}

	[Fact]
	public async Task AllowParallelUploadMultipleFiles()
	{
		const int parallelization = 10;

		var file = _fixture.Create<string>();
		var tasks = new Task<bool>[parallelization];
		for (var i = 0; i < parallelization; i++)
		{
			var fileData = GetByteStream(12 * 1024 * 1024);
			var fileName = $"{file}-{i}";
			tasks[i] = Task.Run(
				async () =>
			{
				await _client.UploadFile(fileName, StreamContentType, fileData, _ct);
				if (!await _client.IsFileExists(fileName, _ct))
				{
					return false;
				}

				await _client.DeleteFile(fileName, _ct);
				return true;
			},
				_ct);
		}

		await Task.WhenAll(tasks);

		foreach (var task in tasks)
		{
			task
				.IsCompletedSuccessfully
				.Should().BeTrue();

			task
				.Result
				.Should().BeTrue();

			task.Dispose();
		}
	}

	[Fact]
	public void BuildUrl()
	{
		var fileName = _fixture.Create<string>();

		_client
			.Invoking(client => client.BuildFileUrl(fileName, TimeSpan.FromSeconds(100)))
			.Should().NotThrow()
			.Which.Should().NotBeNull();
	}

	[Fact]
	public async Task BeExists()
	{
		var fileName = await CreateTestFile();
		var fileExistsResult = await _client.IsFileExists(fileName, _ct);

		fileExistsResult
			.Should().BeTrue();

		await DeleteTestFile(fileName);
	}

	[Fact]
	public async Task BeNotExists()
	{
		var fileExistsResult = await _client.IsFileExists(_fixture.Create<string>(), _ct);

		fileExistsResult
			.Should().BeFalse();
	}

	[Fact]
	public async Task DeleteFile()
	{
		var fileName = await CreateTestFile();

		await _client
			.Invoking(client => client.DeleteFile(fileName, _ct))
			.Should().NotThrowAsync();
	}

	[Fact]
	public async Task DisposeFileStream()
	{
		var fileName = await CreateTestFile();
		using var fileGetResult = await _client.GetFile(fileName, _ct);

		var fileStream = await fileGetResult.GetStream(_ct);
		await fileStream.DisposeAsync();

		await DeleteTestFile(fileName);
	}

	[Fact]
	public async Task DisposeStorageFile()
	{
		var fileName = await CreateTestFile();
		using var fileGetResult = await _client.GetFile(fileName, _ct);

		// ReSharper disable once MethodHasAsyncOverload
		fileGetResult.Dispose();

		await DeleteTestFile(fileName);
	}

	[Fact]
	public async Task GetFileUrl()
	{
		var fileName = await CreateTestFile();

		var url = await _client.GetFileUrl(fileName, TimeSpan.FromSeconds(600), _ct);

		url.Should().NotBeNull();

		using var response = await _fixture.HttpClient.GetAsync(url, _ct);

		await DeleteTestFile(fileName);

		response
			.IsSuccessStatusCode
			.Should().BeTrue(response.ReasonPhrase);
	}

	[Fact]
	public async Task GetFileUrlWithCyrillicName()
	{
		var fileName = await CreateTestFile($"при(ве)+т_как23дела{Guid.NewGuid()}.pdf");

		var url = await _client.GetFileUrl(fileName, TimeSpan.FromSeconds(600), _ct);

		url.Should().NotBeNull();

		using var response = await _fixture.HttpClient.GetAsync(url, _ct);

		await DeleteTestFile(fileName);

		response
			.IsSuccessStatusCode
			.Should().BeTrue(response.ReasonPhrase);
	}

	[Fact]
	public async Task HasValidInformation()
	{
		const int length = 1 * 1024 * 1024;
		const string contentType = "video/mp4";

		var fileName = await CreateTestFile(contentType: contentType);
		using var fileGetResult = await _client.GetFile(fileName, _ct);

		((bool)fileGetResult).Should().BeTrue();

		fileGetResult
			.ContentType
			.Should().Be(contentType);

		fileGetResult
			.Exists
			.Should().BeTrue();

		fileGetResult
			.Length
			.Should().Be(length);

		fileGetResult
			.StatusCode
			.Should().Be(HttpStatusCode.OK);

		await DeleteTestFile(fileName);
	}

	[Fact]
	public async Task HasValidStreamInformation()
	{
		const int length = 1 * 1024 * 1024;
		var fileName = await CreateTestFile(size: length);
		using var fileGetResult = await _client.GetFile(fileName, _ct);

		var fileStream = await fileGetResult.GetStream(_ct);

		fileStream.CanRead.Should().BeTrue();
		fileStream.CanSeek.Should().BeFalse();
		fileStream.CanWrite.Should().BeFalse();
		fileStream.Length.Should().Be(length);

		fileStream
			.Invoking(stream => stream.Position)
			.Should().Throw<NotSupportedException>();

		await fileStream.DisposeAsync();
		await fileStream.DisposeAsync();

		await DeleteTestFile(fileName);
	}

	[Fact]
	public async Task ListFiles()
	{
		const int count = 2;
		var noiseFiles = new List<string>();
		var expectedFileNames = new string[count];
		var prefix = _fixture.Create<string>();

		for (var i = 0; i < count; i++)
		{
			var fileName = $"{prefix}#{_fixture.Create<string>()}";
			expectedFileNames[i] = await CreateTestFile(fileName, size: 1024);
			noiseFiles.Add(await CreateTestFile());
		}

		var actualFileNames = new List<string>();
		await foreach (var file in _client.List(prefix, _ct))
		{
			actualFileNames.Add(file);
		}

		actualFileNames
			.Should().Contain(expectedFileNames);

		foreach (var fileName in expectedFileNames.Concat(noiseFiles))
		{
			await DeleteTestFile(fileName);
		}
	}

	[Fact]
	public async Task PutByteArray()
	{
		var fileName = _fixture.Create<string>();
		var data = GetByteArray(15000);
		var filePutResult = await _client.UploadFile(fileName, StreamContentType, data, _ct);

		filePutResult
			.Should().BeTrue();

		await EnsureFileSame(fileName, data);
		await DeleteTestFile(fileName);
	}

	[Fact]
	public async Task PutStream()
	{
		var fileName = _fixture.Create<string>();
		var data = GetByteStream(15000);
		var filePutResult = await _client.UploadFile(fileName, StreamContentType, data, _ct);

		filePutResult
			.Should().BeTrue();

		await EnsureFileSame(fileName, data);
		await DeleteTestFile(fileName);
	}

	[Fact]
	public async Task Upload()
	{
		var fileName = _fixture.Create<string>();
		using var data = GetByteStream(12 * 1024 * 1024); // 12 Mb
		var filePutResult = await _client.UploadFile(fileName, StreamContentType, data, _ct);

		filePutResult
			.Should().BeTrue();

		await EnsureFileSame(fileName, data.ToArray());
		await DeleteTestFile(fileName);
	}

	[Fact]
	public async Task UploadCyrillicName()
	{
		var fileName = $"при(ве)+т_как23дела{Guid.NewGuid()}.pdf";
		using var data = GetByteStream();
		var uploadResult = await _client.UploadFile(fileName, StreamContentType, data, _ct);

		await DeleteTestFile(fileName);

		uploadResult
			.Should().BeTrue();
	}

	[Fact]
	public async Task NotThrowIfFileAlreadyExists()
	{
		var fileName = await CreateTestFile();
		await _client
			.Invoking(client => client.UploadFile(fileName, StreamContentType, GetByteStream(), _ct))
			.Should().NotThrowAsync();

		await DeleteTestFile(fileName);
	}

	[Fact]
	public Task NotThrowIfFileExistsWithNotExistsBucket()
	{
		return _notExistsBucketClient
			.Invoking(client => client.IsFileExists(_fixture.Create<string>(), _ct))
			.Should().NotThrowAsync();
	}

	[Fact]
	public async Task NotThrowIfFileGetUrlWithNotExistsBucket()
	{
		var result = await _notExistsBucketClient
			.Invoking(client => client.GetFileUrl(_fixture.Create<string>(), TimeSpan.FromSeconds(100), _ct))
			.Should().NotThrowAsync();

		result
			.Which
			.Should().BeNull();
	}

	[Fact]
	public async Task NotThrowIfFileGetWithNotExistsBucket()
	{
		var result = await _notExistsBucketClient
			.Invoking(client => client.GetFile(_fixture.Create<string>(), _ct))
			.Should().NotThrowAsync();

		var getFileResult = result.Which;

		((bool)getFileResult).Should().BeFalse();

		getFileResult
			.Exists
			.Should().BeFalse();
	}

	[Fact]
	public async Task NotThrowIfGetNotExistsFile()
	{
		var fileName = _fixture.Create<string>();
		await _client
			.Invoking(client => client.GetFile(fileName, _ct))
			.Should().NotThrowAsync();
	}

	[Fact]
	public Task NotThrowIfDeleteFileNotExists()
	{
		return _client
			.Invoking(client => client.DeleteFile(_fixture.Create<string>(), _ct))
			.Should().NotThrowAsync();
	}

	[Fact]
	public async Task ThrowIfBucketNotExists()
	{
		var fileArray = GetByteArray();
		var fileName = _fixture.Create<string>();
		var fileStream = GetByteStream();

		await _notExistsBucketClient
			.Invoking(client => client.DeleteFile(fileName, _ct))
			.Should().ThrowAsync<HttpRequestException>();

		await _notExistsBucketClient
			.Invoking(client => client.UploadFile(fileName, StreamContentType, fileStream, _ct))
			.Should().ThrowAsync<HttpRequestException>();

		await _notExistsBucketClient
			.Invoking(client => client.UploadFile(fileName, StreamContentType, fileArray, _ct))
			.Should().ThrowAsync<HttpRequestException>();
	}

	private async Task<string> CreateTestFile(
		string? fileName = null,
		string contentType = StreamContentType,
		int? size = null)
	{
		fileName ??= _fixture.Create<string>();
		using var data = GetByteStream(size ?? 1 * 1024 * 1024); // 1 Mb

		var uploadResult = await _client.UploadFile(fileName, contentType, data, _ct);

		uploadResult
			.Should().BeTrue();

		return fileName;
	}

	private async Task EnsureFileSame(string fileName, byte[] expectedBytes)
	{
		using var getFileResult = await _client.GetFile(fileName, _ct);

		using var memoryStream = GetEmptyByteStream(getFileResult.Length);
		var stream = await getFileResult.GetStream(_ct);
		await stream.CopyToAsync(memoryStream, _ct);

		memoryStream
			.ToArray().SequenceEqual(expectedBytes)
			.Should().BeTrue();
	}

	private async Task EnsureFileSame(string fileName, MemoryStream expectedBytes)
	{
		expectedBytes.Seek(0, SeekOrigin.Begin);

		using var getFileResult = await _client.GetFile(fileName, _ct);

		using var memoryStream = GetEmptyByteStream(getFileResult.Length);
		var stream = await getFileResult.GetStream(_ct);
		await stream.CopyToAsync(memoryStream, _ct);

		memoryStream
			.ToArray().SequenceEqual(expectedBytes.ToArray())
			.Should().BeTrue();
	}

	private Task DeleteTestFile(string fileName)
	{
		return _client.DeleteFile(fileName, _ct);
	}
}
