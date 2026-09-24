using SkiaSharp;

namespace PitakaApp.Api.Tests.Fixtures;

internal static class ProfilePictureTestImages
{
    public static byte[] CreatePng() =>
        Create(SKEncodedImageFormat.Png, SKColors.CornflowerBlue, 2, 2);

    public static byte[] Create(
        SKEncodedImageFormat format,
        SKColor color,
        int width = 3,
        int height = 2
    )
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, quality: 100);
        return encoded.ToArray();
    }

    public static byte[] CreateAnimatedWebp()
    {
        using var first = new SKBitmap(1, 1);
        using (var canvas = new SKCanvas(first))
        {
            canvas.Clear(SKColors.Red);
        }

        using var second = new SKBitmap(1, 1);
        using (var canvas = new SKCanvas(second))
        {
            canvas.Clear(SKColors.Blue);
        }

        var frames = new[]
        {
            new SKWebpEncoderFrame(first, TimeSpan.FromMilliseconds(100)),
            new SKWebpEncoderFrame(second, TimeSpan.FromMilliseconds(100)),
        };
        using var encoded = SKWebpEncoder.EncodeAnimated(frames, SKWebpEncoderOptions.Default);
        return encoded!.ToArray();
    }

    public static byte[] AddExifMarker(byte[] jpeg)
    {
        byte[] exifSegment = [0xff, 0xe1, 0x00, 0x10, .. "Exif\0\0GPSInfo"u8.ToArray()];
        var result = new byte[jpeg.Length + exifSegment.Length];
        jpeg.AsSpan(0, 2).CopyTo(result);
        exifSegment.CopyTo(result, 2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + exifSegment.Length));
        return result;
    }
}
