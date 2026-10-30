namespace WidBarWidget1.ExtensionApp;

// Small diagnostic log: %LOCALAPPDATA%\LoggyBat\loggybat.log. Inside MSIX Windows
// redirects it to %LOCALAPPDATA%\Packages\<package>\LocalCache\Local\LoggyBat\.
internal static class Log
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LoggyBat",
        "loggybat.log");

    public static void Write(string text)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);

                File.AppendAllText(
                    FilePath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never break the widget.
        }
    }
}
