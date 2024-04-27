namespace Storage.Tests;

public sealed class BucketShould(StorageFixture fixture) : IClassFixture<StorageFixture>
{
	private readonly CancellationToken _cancellation = CancellationToken.None;
	private readonly S3Client _client = fixture.S3Client;

	[Fact]
	public async Task CreateBucket()
	{
		// don't dispose it
		var client = TestHelper.CloneClient(fixture);

		var bucketCreateResult = await client.CreateBucket(_cancellation);

		bucketCreateResult
			.Should().BeTrue();

		await DeleteTestBucket(client);
	}

	[Fact]
	public async Task BeExists()
	{
		var bucketExistsResult = await _client.IsBucketExists(_cancellation);

		bucketExistsResult
			.Should().BeTrue();
	}

	[Fact]
	public async Task BeNotExists()
	{
		// don't dispose it
		var client = TestHelper.CloneClient(fixture);

		var bucketExistsResult = await client.IsBucketExists(_cancellation);

		bucketExistsResult
			.Should().BeFalse();
	}

	[Fact]
	public Task NotThrowIfCreateBucketAlreadyExists()
	{
		return _client
			.Invoking(client => client.CreateBucket(_cancellation))
			.Should().NotThrowAsync();
	}

	[Fact]
	public Task NotThrowIfDeleteNotExistsBucket()
	{
		// don't dispose it
		var client = TestHelper.CloneClient(fixture);

		return client
			.Invoking(c => c.DeleteBucket(_cancellation))
			.Should().NotThrowAsync();
	}

	private async Task DeleteTestBucket(S3Client client)
	{
		var bucketDeleteResult = await client.DeleteBucket(_cancellation);

		bucketDeleteResult
			.Should().BeTrue();
	}
}
