using System.IO.Compression;

namespace ScreenBux.Updater.Services;

/// <summary>
/// Extracts an update zip over an install directory, retrying a few times on <see cref="IOException"/>.
/// Right after a Windows Service is reported <c>Stopped</c> (or a process has exited), the OS can take
/// a brief moment to fully release the file locks on its loaded modules - observed in practice as
/// "the process cannot access the file ...\System.Diagnostics.EventLog.dll because it is being used by
/// another process" immediately after <see cref="ServiceUpdater"/> stops "ScreenBux Parental Control
/// Service" and starts extracting over it. Without a retry, that transient lock aborts the whole
/// update for the cycle and the component silently stays on its old version. Only <see cref="IOException"/>
/// is retried; anything else (corrupt zip, permissions, disk full, ...) fails immediately since a delay
/// won't fix it.
/// </summary>
internal static class ZipExtraction
{
    public static void ExtractWithRetry(
        string zipPath,
        string destinationDirectory,
        ILogger logger,
        string componentName,
        int maxAttempts = 6,
        int retryDelayMilliseconds = 500)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                ZipFile.ExtractToDirectory(zipPath, destinationDirectory, overwriteFiles: true);
                if (attempt > 1)
                {
                    logger.LogInformation(
                        "Extracted {Component} update to {Directory} on attempt {Attempt}/{MaxAttempts}, after {PriorAttempts} earlier attempt(s) blocked by a locked file.",
                        componentName, destinationDirectory, attempt, maxAttempts, attempt - 1);
                }

                return;
            }
            catch (IOException ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(
                    ex,
                    "Attempt {Attempt}/{MaxAttempts} to extract {Component} update to {Directory} failed because a file was still locked (likely the just-stopped process hasn't fully released it yet); retrying in {DelayMs}ms.",
                    attempt, maxAttempts, componentName, destinationDirectory, retryDelayMilliseconds);
                Thread.Sleep(retryDelayMilliseconds);
            }
        }
    }
}
