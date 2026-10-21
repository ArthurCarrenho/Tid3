using System;
using System.IO;

namespace TidalUi3.Helpers;

/// <summary>
/// Writes the app's diagnostic logs (exceptions.log, playtrace.log). Safe to call from the
/// first-chance exception handler: it never throws, and it ignores exceptions raised while
/// it is itself writing, which would otherwise re-enter that handler without end.
/// </summary>
public static class AppLog
{
    // Past this size a log is moved to .old, so a busy log can't grow without bound.
    private const long MaxLogBytes = 5 * 1024 * 1024;

    private static readonly object _lock = new();
    private static string? _directory;

    [ThreadStatic] private static bool _isWriting;

    /// <summary>True while this thread is inside the logger.</summary>
    public static bool IsWriting => _isWriting;

    public static void Exception(string tag, Exception ex) => Exception(tag, ex.ToString());

    public static void Exception(string tag, string details) =>
        Append("exceptions.log",
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}\n{details}\n\n----------------------------------------\n\n");

    public static void Trace(string message) =>
        Append("playtrace.log", $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");

    private static void Append(string fileName, string text)
    {
        if (_isWriting) return;
        _isWriting = true;
        try
        {
            lock (_lock)
            {
                var directory = _directory ??= FindDirectory();
                if (directory is null) return;

                var path = Path.Combine(directory, fileName);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxLogBytes)
                    File.Move(path, path + ".old", overwrite: true);

                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.Write(text);
            }
        }
        catch { /* logging must never take the app down */ }
        finally
        {
            _isWriting = false;
        }
    }

    // Logs/ next to the app, where they always were. That folder is writable when the app is
    // registered from the build output, but read-only once installed from an MSIX, so fall
    // back to the app's LocalState there.
    private static string? FindDirectory()
    {
        foreach (var root in new Func<string>[]
        {
            () => AppDomain.CurrentDomain.BaseDirectory,
            () => Windows.Storage.ApplicationData.Current.LocalFolder.Path,
        })
        {
            try
            {
                var directory = Path.Combine(root(), "Logs");
                Directory.CreateDirectory(directory);
                using (new FileStream(Path.Combine(directory, "exceptions.log"),
                    FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { }
                return directory;
            }
            catch { }
        }
        return null;
    }
}
