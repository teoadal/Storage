namespace Storage.Tests;

public sealed class BucketShould(StorageFixture fixture) : IClassFixture<StorageFixture>
{
	private readonly CancellationToken _ct = CancellationToken.None;
	private readonly S3Client _client = fixture.S3Client;

	[Fact]
	public async Task CreateBucket()
	{
		// don't dispose it
		var client = TestHelper.CloneClient(fixture);

		var bucketCreateResult = await client.CreateBucket(_ct);

		bucketCreateResult
			.Should().BeTrue();

		await DeleteTestBucket(client);
	}

	[Fact]
	public async Task BeExists()
	{
		var bucketExistsResult = await _client.IsBucketExists(_ct);

		bucketExistsResult
			.Should().BeTrue();
	}

	[Fact]
	public async Task BeNotExists()
	{
		// don't dispose it
		var client = TestHelper.CloneClient(fixture);

		var bucketExistsResult = await client.IsBucketExists(_ct);

		bucketExistsResult
			.Should().BeFalse();
	}

	[Fact]
	public Task NotThrowIfCreateBucketAlreadyExists()
	{
		return _client
			.Invoking(client => client.CreateBucket(_ct))
			.Should().NotThrowAsync();
	}

	[Fact]
	public Task NotThrowIfDeleteNotExistsBucket()
	{
		// don't dispose it
		var client = TestHelper.CloneClient(fixture);

		return client
			.Invoking(c => c.DeleteBucket(_ct))
			.Should().NotThrowAsync();
	}

	private async Task DeleteTestBucket(S3Client client)
	{
		var bucketDeleteResult = await client.DeleteBucket(_ct);

		bucketDeleteResult
			.Should().BeTrue();
	}
}
