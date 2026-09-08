using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using ScreenBux.Shared.Models;

namespace ScreenBux.Agent.Services;

/// <summary>
/// Captures a JPEG screenshot of every connected monitor. Runs in the Agent because it is the
/// only component with access to the interactive desktop (the Service runs in Session 0).
/// Images are downscaled and JPEG-encoded to keep the named-pipe/REST payloads reasonably sized.
/// </summary>
public class ScreenCaptureService
{
    private const int MaxWidthPx = 1920;
    private const long JpegQuality = 80L;

    public List<CapturedImage> CaptureAllScreens()
    {
        var images = new List<CapturedImage>();
        var screens = Screen.AllScreens;

        for (var i = 0; i < screens.Length; i++)
        {
            try
            {
                images.Add(CaptureScreen(i, screens[i]));
            }
            catch
            {
                // Skip monitors that fail to capture (e.g. transiently disconnected) rather than
                // failing the whole request.
            }
        }

        return images;
    }

    private static CapturedImage CaptureScreen(int index, Screen screen)
    {
        var bounds = screen.Bounds;

        using var fullBitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(fullBitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        }

        using var scaledBitmap = ScaleDown(fullBitmap, MaxWidthPx);

        using var memoryStream = new MemoryStream();
        var jpegCodec = GetJpegCodec();
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
        scaledBitmap.Save(memoryStream, jpegCodec, encoderParams);

        return new CapturedImage
        {
            Index = index,
            WidthPx = scaledBitmap.Width,
            HeightPx = scaledBitmap.Height,
            ImageBytes = memoryStream.ToArray(),
        };
    }

    private static Bitmap ScaleDown(Bitmap source, int maxWidthPx)
    {
        if (source.Width <= maxWidthPx)
        {
            return new Bitmap(source);
        }

        var scale = maxWidthPx / (double)source.Width;
        var scaledWidth = maxWidthPx;
        var scaledHeight = (int)Math.Round(source.Height * scale);

        var scaledBitmap = new Bitmap(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(scaledBitmap);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, scaledWidth, scaledHeight);
        return scaledBitmap;
    }

    private static ImageCodecInfo GetJpegCodec()
    {
        return ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
    }
}
