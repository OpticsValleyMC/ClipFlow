using System.Security.Cryptography;
using System.Text;

namespace ClipFlow.Core;

public enum ContentKind { Text, Image }
public enum ThemeMode { System, Light, Dark }
public enum BackdropLevel { Solid, Subtle, Standard, Strong }
public enum FavoriteSortMode { RecentCopy, OldestCopy, Content }
public sealed record AppSettings(ThemeMode Theme = ThemeMode.System, bool RecordingEnabled = true, int HistoryLimit = 500,
    BackdropLevel Backdrop = BackdropLevel.Strong, FavoriteSortMode FavoriteSort = FavoriteSortMode.RecentCopy,
    uint HotkeyModifiers = 1, uint HotkeyKey = 0x56);
public sealed record ClipboardRecord(string Id, ContentKind Kind, string? Text, string Hash,
    string? ImagePath, string? ThumbnailPath, int Width, int Height,
    DateTimeOffset CreatedAt, DateTimeOffset CopiedAt, bool IsFavorite, bool IsPinned = false);
public sealed record CapturedContent(ContentKind Kind, string? Text, string Hash,
    string? ImagePath = null, string? ThumbnailPath = null, int Width = 0, int Height = 0,
    byte[]? ImagePng = null, byte[]? ThumbnailPng = null);

public static class FavoriteOrdering
{
    public static IEnumerable<ClipboardRecord> Apply(IEnumerable<ClipboardRecord> records, FavoriteSortMode mode)
    {
        var pinned = records.Where(r => r.IsFavorite).OrderByDescending(r => r.IsPinned);
        var ordered = mode switch
        {
            FavoriteSortMode.OldestCopy => pinned.ThenBy(r => r.CopiedAt),
            FavoriteSortMode.Content => pinned.ThenBy(r => r.Text ?? "图片", StringComparer.CurrentCultureIgnoreCase).ThenByDescending(r => r.CopiedAt),
            _ => pinned.ThenByDescending(r => r.CopiedAt)
        };
        return ordered.ThenBy(r => r.Id, StringComparer.Ordinal);
    }
}

public static class ContentIdentity
{
    public static string TextHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    // Hash decoded BGRA pixels plus dimensions, so image container metadata does not produce duplicates.
    public static string ImageHash(byte[] pixels, int width, int height)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(BitConverter.GetBytes(width));
        hash.AppendData(BitConverter.GetBytes(height));
        hash.AppendData(pixels);
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

public static class RetryPolicy
{
    public static async Task<T> RunAsync<T>(Func<Task<T>> action, Func<Exception, bool> transient,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return await action(); }
            catch (Exception ex) when (attempt < 3 && transient(ex))
            {
                await Task.Delay(40 * (attempt + 1), cancellationToken);
            }
        }
    }
}
