namespace DeepDrftCli.Utils;

internal static class CliUtils
{
    /// <summary>
    /// Truncates a string to fit within the specified column width,
    /// appending "..." when the string is longer.
    /// </summary>
    internal static string TruncateString(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return input.Length <= maxLength ? input : input.Substring(0, maxLength - 3) + "...";
    }
}
