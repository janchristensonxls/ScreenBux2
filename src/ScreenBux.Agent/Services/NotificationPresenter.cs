using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using ScreenBux.Shared.Messages;

namespace ScreenBux.Agent.Services;

/// <summary>
/// Shows a topmost overlay window + plays a short audio cue for a queued
/// <see cref="PendingNotification"/>. The audio cue is the primary, reliable channel - it works
/// even when the overlay can't be composited over an exclusive-fullscreen game; the overlay is
/// best-effort on top of that for borderless/windowed apps and the desktop.
/// </summary>
public class NotificationPresenter
{
    /// <summary>
    /// Shows the overlay (best-effort) and plays the audio cue for the given notification.
    /// Returns true if the overlay was successfully shown, false if it could not be (e.g. blocked
    /// by an exclusive-fullscreen app) - the audio cue is attempted either way.
    /// </summary>
    public bool Present(PendingNotification notification)
    {
        var shown = TryShowOverlay(notification);
        PlayAudioCue(notification.Severity);
        return shown;
    }

    private static bool TryShowOverlay(PendingNotification notification)
    {
        try
        {
            var window = new NotificationOverlayWindow(notification);
            window.Show();
            return true;
        }
        catch
        {
            // Best-effort only - e.g. exclusive-fullscreen compositing blocked it.
            return false;
        }
    }

    private static void PlayAudioCue(NotificationSeverity severity)
    {
        try
        {
            var toneHz = severity switch
            {
                NotificationSeverity.Warning => 880,
                NotificationSeverity.TimeUp => 440,
                _ => 660
            };

            var signalGenerator = new SignalGenerator
            {
                Gain = 0.3,
                Frequency = toneHz,
                Type = SignalGeneratorType.Sin
            };

            using var wave = new WaveOutEvent();
            wave.Init(signalGenerator.Take(TimeSpan.FromMilliseconds(300)));
            wave.Play();

            while (wave.PlaybackState == PlaybackState.Playing)
            {
                Thread.Sleep(50);
            }
        }
        catch
        {
            // Best-effort only - no audio output device, etc.
        }
    }
}
