namespace Helios.Library;

/// <summary>Small, size-capped text logs in the app's data folder (activation and crash history).</summary>
public static class DiagnosticsLog
{
    public const long DefaultLimit = 32 * 1024;

    /// <summary>Appends one line. When the file has reached <paramref name="limit"/> bytes it starts over.</summary>
    /// <returns>False when the log could not be written. Logging never throws.</returns>
    public static bool Append(string path, string entry, long limit = DefaultLimit)
    {
        try
        {
            string line = entry.ReplaceLineEndings(" ") + Environment.NewLine;
            if (File.Exists(path) && new FileInfo(path).Length >= limit)
                File.WriteAllText(path, line);
            else
                File.AppendAllText(path, line);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
