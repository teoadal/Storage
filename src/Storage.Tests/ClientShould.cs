namespace Storage.Tests;

public sealed class ClientShould(StorageFixture fixture) : IClassFixture<StorageFixture>
{
	private readonly CancellationToken _ct = CancellationToken.None;
	private readonly S3BucketClient _client = fixture.S3Client;

	[Fact]
	public void DeserializeSettingsJson()
	{
		var expected = fixture.Settings;

		var json = JsonSerializer.Serialize(expected);
		var actual = JsonSerializer.Deserialize<S3BucketSettings>(json);

		actual.Should().BeEquivalentTo(expected);
	}

	[Fact]
	public void HasValidInfo()
	{
		_client
			.Bucket
			.Should().Be(fixture.Settings.Bucket);
	}

	[Fact]
	public Task ThrowIfDisposed()
	{
		var client = TestHelper.CloneClient(fixture, null, new HttpClient());

		client.Dispose();

		return client
			.Invoking(c => c.CreateBucket(_ct))
			.Should().ThrowExactlyAsync<ObjectDisposedException>();
	}
}
