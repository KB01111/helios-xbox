using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Helios_Launcher.Windows;

internal static class XboxMode
{
    internal static bool? IsActive()
    {
        try
        {
            return IsGamingFullScreenExperienceActive();
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    internal static void WriteActivation(string dataDirectory, bool startedAtSignIn, long elapsedMilliseconds)
    {
        try
        {
            string path = Path.Combine(dataDirectory, "activation.log");
            string state = IsActive() is { } active ? (active ? "active" : "inactive") : "unknown";
            string entry = $"{DateTimeOffset.UtcNow:O} signIn={startedAtSignIn} xboxMode={state} windowMs={elapsedMilliseconds}";
            if (File.Exists(path) && new FileInfo(path).Length >= 32 * 1024)
                File.WriteAllText(path, entry + Environment.NewLine);
            else
                File.AppendAllText(path, entry + Environment.NewLine);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine(error.Message);
        }
    }

    [DllImport("api-ms-win-gaming-experience-l1-1-0.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsGamingFullScreenExperienceActive();
}
