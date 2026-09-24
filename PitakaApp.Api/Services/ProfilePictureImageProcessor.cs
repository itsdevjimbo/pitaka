using System.Buffers.Binary;
using SkiaSharp;

namespace PitakaApp.Api.Services;

public enum ProfilePictureValidationError
{
    Empty,
    TooLarge,
    InvalidImage,
    UnsupportedFormat,
    Animated,
    DimensionsExceeded,
}

public sealed record ProcessedProfilePicture(byte[] Content, string MediaType);

public sealed record ProfilePictureImageProcessingResult(
    ProcessedProfilePicture? Picture,
    ProfilePictureValidationError? Error
);

public class ProfilePictureImageProcessor
{
    public const int MaxEncodedBytes = 2 * 1024 * 1024;
    public const int MaxDimension = 4096;
    public const int MaxMultipartOverheadBytes = 64 * 1024;

    public ProfilePictureImageProcessingResult Process(byte[] encoded)
    {
        if (encoded.Length == 0)
        {
            return Failure(ProfilePictureValidationError.Empty);
        }

        if (encoded.Length > MaxEncodedBytes)
        {
            return Failure(ProfilePictureValidationError.TooLarge);
        }

        try
        {
            using var input = new MemoryStream(encoded, writable: false);
            using var codec = SKCodec.Create(input);
            if (codec is null)
            {
                return Failure(ProfilePictureValidationError.InvalidImage);
            }

            var (format, mediaType) = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Jpeg => (SKEncodedImageFormat.Jpeg, "image/jpeg"),
                SKEncodedImageFormat.Png => (SKEncodedImageFormat.Png, "image/png"),
                SKEncodedImageFormat.Webp => (SKEncodedImageFormat.Webp, "image/webp"),
                _ => (SKEncodedImageFormat.Bmp, null),
            };

            if (mediaType is null)
            {
                return Failure(ProfilePictureValidationError.UnsupportedFormat);
            }

            var dimensions = codec.Info;
            if (
                dimensions.Width <= 0
                || dimensions.Height <= 0
                || dimensions.Width > MaxDimension
                || dimensions.Height > MaxDimension
            )
            {
                return Failure(ProfilePictureValidationError.DimensionsExceeded);
            }

            if (
                codec.FrameCount > 1
                || (format == SKEncodedImageFormat.Png && ContainsApngAnimationControl(encoded))
                || (format == SKEncodedImageFormat.Webp && ContainsWebpAnimationChunk(encoded))
            )
            {
                return Failure(ProfilePictureValidationError.Animated);
            }

            using var bitmap = new SKBitmap(dimensions);
            var decodeResult = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
            if (decodeResult != SKCodecResult.Success)
            {
                return Failure(ProfilePictureValidationError.InvalidImage);
            }

            using var image = SKImage.FromBitmap(bitmap);
            using var sanitized = image.Encode(format, quality: 100);

            return new ProfilePictureImageProcessingResult(
                new ProcessedProfilePicture(sanitized.ToArray(), mediaType),
                null
            );
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Failure(ProfilePictureValidationError.InvalidImage);
        }
    }

    private static bool ContainsApngAnimationControl(ReadOnlySpan<byte> image)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (image.Length < signature.Length || !image[..signature.Length].SequenceEqual(signature))
        {
            return false;
        }

        var offset = signature.Length;
        while (offset <= image.Length - 12)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(image[offset..]);
            var chunkType = image.Slice(offset + 4, 4);
            if (chunkType.SequenceEqual("acTL"u8))
            {
                return true;
            }

            var nextOffset = (long)offset + chunkLength + 12;
            if (nextOffset > image.Length)
            {
                return false;
            }

            offset = (int)nextOffset;
        }

        return false;
    }

    private static bool ContainsWebpAnimationChunk(ReadOnlySpan<byte> image)
    {
        if (
            image.Length < 12
            || !image[..4].SequenceEqual("RIFF"u8)
            || !image.Slice(8, 4).SequenceEqual("WEBP"u8)
        )
        {
            return false;
        }

        var offset = 12;
        while (offset <= image.Length - 8)
        {
            var chunkType = image.Slice(offset, 4);
            if (chunkType.SequenceEqual("ANIM"u8) || chunkType.SequenceEqual("ANMF"u8))
            {
                return true;
            }

            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(image[(offset + 4)..]);
            var nextOffset = (long)offset + 8 + chunkLength + (chunkLength & 1);
            if (nextOffset > image.Length)
            {
                return false;
            }

            offset = (int)nextOffset;
        }

        return false;
    }

    private static ProfilePictureImageProcessingResult Failure(
        ProfilePictureValidationError error
    ) => new(null, error);
}
