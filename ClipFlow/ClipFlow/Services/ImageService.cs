using ClipFlow.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClipFlow.Services;

public sealed class ImageService
{
    public const int ThumbnailMaxEdge = 1024;
    public async Task<CapturedContent> DecodeAsync(IRandomAccessStream stream, CancellationToken cancellationToken)
    {
        if (stream.Size > 64 * 1024 * 1024) throw new InvalidDataException("图片超过 64 MB，已跳过。");
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        var width = decoder.PixelWidth;
        var height = decoder.PixelHeight;
        if (width == 0 || height == 0 || (ulong)width * height > 16_000_000) throw new InvalidDataException("图片超过 1600 万像素，已跳过。");
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(cancellationToken);
        var pixels = data.DetachPixelData();
        var hash = await Task.Run(() => ContentIdentity.ImageHash(pixels, (int)width, (int)height), cancellationToken);
        var original = await EncodeAsync(pixels, width, height, false, cancellationToken);
        var thumbnail = await EncodeAsync(pixels, width, height, true, cancellationToken);
        return new CapturedContent(ContentKind.Image, null, hash, Width: (int)width, Height: (int)height,
            ImagePng: original, ThumbnailPng: thumbnail);
    }
    private static async Task<byte[]> EncodeAsync(byte[] pixels, uint width, uint height, bool thumbnail, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream).AsTask(token);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, width, height, 96, 96, pixels);
        if (thumbnail)
        {
            var scale = Math.Min(1d, ThumbnailMaxEdge / (double)Math.Max(width, height));
            encoder.BitmapTransform.ScaledWidth = Math.Max(1, (uint)(width * scale));
            encoder.BitmapTransform.ScaledHeight = Math.Max(1, (uint)(height * scale));
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        await encoder.FlushAsync().AsTask(token);
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size).AsTask(token);
        var bytes = new byte[(int)stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }
}
