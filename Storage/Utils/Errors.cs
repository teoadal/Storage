namespace Storage.Utils;

internal static class Errors
{
    public static void CantFormatToString<T>(T value) where T : struct
    {
        throw new Exception($"Can't format '{value}' to string");
    }
}