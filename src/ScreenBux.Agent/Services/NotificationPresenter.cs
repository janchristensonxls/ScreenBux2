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
    /// Returns the overlay window if it was successfully shown, or null if it could not be (e.g.
    /// blocked by an exclusive-fullscreen app) - the audio cue is attempted either way.
    /// </summary>
    public NotificationOverlayWindow? Present(PendingNotification notification)
    {
        var window = TryShowOverlay(notification);
        PlayAudioCue(notification.Severity);
        return window;
    }

    private static NotificationOverlayWindow? TryShowOverlay(PendingNotification notification)
    {
        try
        {
            var window = new NotificationOverlayWindow(notification);
            window.Show();
            return window;
        }
        catch
        {
            // Best-effort only - e.g. exclusive-fullscreen compositing blocked it.
            return null;
        }
    }

    /// <summary>
    /// Peak sine amplitude used for every tone (0-1 of full scale). Deliberately close to 1.0 -
    /// at the old 0.3 the cue was easy to miss under other apps' audio (games, calls, music).
    /// </summary>
    private const double ToneGain = 0.9;

    private static void PlayAudioCue(NotificationSeverity severity)
    {
        try
        {
            switch (severity)
            {
                case NotificationSeverity.Warning:
                    // "5 minutes left": three rising beeps read as more urgent than a single
                    // flat tone and stand out more from background game/media audio.
                    PlayTone(660, TimeSpan.FromMilliseconds(150));
                    Thread.Sleep(80);
                    PlayTone(880, TimeSpan.FromMilliseconds(150));
                    Thread.Sleep(80);
                    PlayTone(1175, TimeSpan.FromMilliseconds(220));
                    break;
                case NotificationSeverity.ParentMessage:
                    // A friendly "ding-dong" chime, played twice so it's heard over game audio -
                    // distinct from the rising "time is running out" beeps.
                    for (var i = 0; i < 2; i++)
                    {
                        PlayTone(1047, TimeSpan.FromMilliseconds(180));
                        Thread.Sleep(40);
                        PlayTone(784, TimeSpan.FromMilliseconds(320));
                        Thread.Sleep(250);
                    }
                    break;
                case NotificationSeverity.TimeUp:
                    PlayTone(440, TimeSpan.FromMilliseconds(300));
                    break;
                default:
                    PlayTone(660, TimeSpan.FromMilliseconds(300));
                    break;
            }
        }
        catch
        {
            // Best-effort only - no audio output device, etc.
        }
    }

    private static void PlayTone(double frequencyHz, TimeSpan duration)
    {
        var signalGenerator = new SignalGenerator
        {
            Gain = ToneGain,
            Frequency = frequencyHz,
            Type = SignalGeneratorType.Sin
        };

        using var wave = new WaveOut();
        wave.Volume = 1.0f;
        wave.Init(signalGenerator.Take(duration));
        wave.Play();

        while (wave.PlaybackState == PlaybackState.Playing)
        {
            Thread.Sleep(20);
        }
    }
}
