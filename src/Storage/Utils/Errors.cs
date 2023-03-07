namespace Storage.Utils;

internal static class Errors
{
    public static void CantFormatToString<T>(T value) where T : struct
    {
        throw new Exception($"Can't format '{value}' to string");
    }

    public static void UnexpectedResult(HttpResponseMessage response)
    {
        var reason = response.ReasonPhrase ?? response.ToString();
        var exception = new HttpRequestException("Storage has returned an unexpected result: " +
                                                 $"{response.StatusCode} ({reason})");

        response.Dispose();

        throw exception;
    }
}