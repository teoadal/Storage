using Storage.Utils;

namespace Storage.Tests.Utils;

public class UrlUtilsTests
{
	[Theory]
	[InlineData("my-bucket", null, "my-bucket")]
	[InlineData("my-bucket", "file.txt", "my-bucket/file.txt")]
	[InlineData("my-bucket", "folder/file.txt", "my-bucket/folder/file.txt")]
	[InlineData("my-bucket", "file with spaces.txt", "my-bucket/file%20with%20spaces.txt")]
	[InlineData("my-bucket", "file@name.txt", "my-bucket/file%40name.txt")]
	public void BuildFileUrl_ShouldReturnCorrectUrl(string bucket, string? fileName, string expectedUrl)
	{
		// Act
		var result = UrlUtils.BuildFileUrl(bucket, fileName);

		// Assert
		Assert.Equal(expectedUrl, result);
	}
}
