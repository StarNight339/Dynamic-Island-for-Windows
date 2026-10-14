using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace DynamicIsland.Core;

/// <summary>
/// Catches exceptions nobody else handled and appends them to <see cref="FilePath"/>. Without this, an error in an
/// async void handler (timers, button clicks) closes the tray app with no trace of why.
/// </summary>
public static class CrashLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland", "error.log");

    /// <summary>Hooks the dispatcher, the task scheduler and the app domain. Call once at startup.</summary>
    public static void Install(Application app)
    {
        app.DispatcherUnhandledException += OnDispatcherException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Unobserved task exception", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write(e.IsTerminating ? "Fatal error" : "Unhandled error", e.ExceptionObject as Exception);
    }

    private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Write("UI thread exception", e.Exception);
        // The island is a background overlay; one failed tick or click shouldn't take the whole app down.
        // Out-of-memory and similar leave the process in an unknown state, so those still crash.
        e.Handled = e.Exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);
    }

    public static void Write(string what, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxBytes) File.Move(path, path + ".1", overwrite: true);

                var entry = new StringBuilder()
                    .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                    .Append(what).Append(" (v").Append(Updater.Current.ToString(3)).AppendLine(")")
                    .AppendLine(ex?.ToString() ?? "(no exception object)")
                    .AppendLine();
                File.AppendAllText(path, entry.ToString());
            }
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Nowhere to write; nothing more we can do.
        }
    }
}
