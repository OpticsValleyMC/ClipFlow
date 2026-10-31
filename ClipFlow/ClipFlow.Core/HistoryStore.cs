namespace ClipFlow.Core;

public sealed class HistoryStore
{
    public const int SchemaVersion = 3;
    public string RootPath { get; }
    public string DatabasePath => Path.Combine(RootPath, "history.db");
    public string ImagesPath => Path.Combine(RootPath, "images");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _ready;
    public HistoryStore(string rootPath) => RootPath = Path.GetFullPath(rootPath);

    private async Task<T> RunAsync<T>(Func<NativeSqlite, T> work, bool initializing = false)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                if (!initializing && !_ready) throw new InvalidOperationException("数据库尚未就绪，请重试初始化。");
                Directory.CreateDirectory(RootPath);
                using var db = new NativeSqlite(DatabasePath);
                return work(db);
            }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
    public Task<bool> InitializeAsync() => RunAsync(db =>
    {
        db.Execute("PRAGMA journal_mode=WAL");
        db.Execute("PRAGMA synchronous=FULL");
        var version = db.Scalar("PRAGMA user_version");
        if (version > SchemaVersion) throw new InvalidDataException("数据库由较新版本创建，请升级 ClipFlow。原始数据已保留。");
        // Each migration and its version update commit together. Never delete/recreate a failed database.
        db.Transaction(() =>
        {
            if (version < 1)
            {
                db.Execute("CREATE TABLE entries(id TEXT PRIMARY KEY, kind INTEGER NOT NULL, text TEXT, hash TEXT NOT NULL, image_path TEXT, width INTEGER NOT NULL DEFAULT 0, height INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL, copied_at INTEGER NOT NULL, favorite INTEGER NOT NULL DEFAULT 0)");
                db.Execute("CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL)");
                db.Execute("PRAGMA user_version=1");
            }
            if (version < 2)
            {
                db.Execute("ALTER TABLE entries ADD COLUMN thumbnail_path TEXT");
                // Merge possible v1 duplicates while preserving favorite status on the newest row.
                db.Execute("UPDATE entries SET favorite=(SELECT MAX(e.favorite) FROM entries e WHERE e.kind=entries.kind AND e.hash=entries.hash)");
                db.Execute("DELETE FROM entries WHERE rowid NOT IN (SELECT (SELECT e.rowid FROM entries e WHERE e.kind=g.kind AND e.hash=g.hash ORDER BY e.copied_at DESC,e.rowid DESC LIMIT 1) FROM entries g GROUP BY g.kind,g.hash)");
                db.Execute("CREATE UNIQUE INDEX idx_content ON entries(kind,hash)");
                db.Execute("CREATE INDEX idx_copied ON entries(copied_at DESC)");
                db.Execute("CREATE INDEX idx_favorite ON entries(favorite,copied_at DESC)");
                db.Execute("CREATE INDEX idx_kind ON entries(kind,copied_at DESC)");
                db.Execute("PRAGMA user_version=2");
            }
            if (version < 3)
            {
                db.Execute("ALTER TABLE entries ADD COLUMN pinned INTEGER NOT NULL DEFAULT 0");
                db.Execute("CREATE INDEX idx_favorite_pinned ON entries(favorite,pinned DESC,copied_at DESC)");
                db.Execute("PRAGMA user_version=3");
            }
        });
        Directory.CreateDirectory(ImagesPath);
        _ready = true;
        SweepFiles(db);
        return true;
    }, true);

    private const string Columns = "id,kind,text,hash,image_path,thumbnail_path,width,height,created_at,copied_at,favorite,pinned";
    private static ClipboardRecord Record(object?[] r) => new((string)r[0]!, (ContentKind)(long)r[1]!, (string?)r[2], (string)r[3]!,
        (string?)r[4], (string?)r[5], (int)(long)r[6]!, (int)(long)r[7]!,
        DateTimeOffset.FromUnixTimeMilliseconds((long)r[8]!), DateTimeOffset.FromUnixTimeMilliseconds((long)r[9]!), (long)r[10]! != 0, (long)r[11]! != 0);
    public Task<List<ClipboardRecord>> LoadAsync() => RunAsync(db => db.Query($"SELECT {Columns} FROM entries ORDER BY copied_at DESC,id DESC").Select(Record).ToList());
    public Task<ClipboardRecord> SaveAsync(CapturedContent content, int limit) => RunAsync(db =>
    {
        if (content.Kind == ContentKind.Image)
        {
            if (content.ImagePng is not { Length: > 0 })
                throw new InvalidDataException("图片必须先读取为本地副本，不能仅保存来源路径。");
            if (content.Hash.Length != 64 || !content.Hash.All(Uri.IsHexDigit)) throw new InvalidDataException("无效内容哈希。");
            var original = Path.Combine("images", content.Hash + ".png");
            var thumbnail = Path.Combine("images", content.Hash + ".thumb.png");
            WriteAtomic(ResolveFile(original), content.ImagePng);
            if (content.ThumbnailPng is not null) WriteAtomic(ResolveFile(thumbnail), content.ThumbnailPng);
            content = content with { ImagePath = original, ThumbnailPath = content.ThumbnailPng is null ? null : thumbnail };
        }
        var now = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), db.Scalar("SELECT COALESCE(MAX(copied_at),0)+1 FROM entries"));
        var id = Guid.NewGuid().ToString("N");
        db.Transaction(() =>
        {
            db.Execute("INSERT INTO entries(id,kind,text,hash,image_path,thumbnail_path,width,height,created_at,copied_at,favorite) VALUES(?,?,?,?,?,?,?,?,?,?,0) ON CONFLICT(kind,hash) DO UPDATE SET copied_at=excluded.copied_at,image_path=COALESCE(excluded.image_path,entries.image_path),thumbnail_path=COALESCE(excluded.thumbnail_path,entries.thumbnail_path)",
                id, (int)content.Kind, content.Text, content.Hash, content.ImagePath, content.ThumbnailPath, content.Width, content.Height, now, now);
            Prune(db, limit);
        });
        var record = Record(db.Query($"SELECT {Columns} FROM entries WHERE kind=? AND hash=?", (int)content.Kind, content.Hash)[0]);
        SweepFiles(db);
        return record;
    });
    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
    }
    private static void Prune(NativeSqlite db, int limit) => db.Execute("DELETE FROM entries WHERE id IN (SELECT id FROM entries ORDER BY copied_at DESC,id DESC LIMIT -1 OFFSET ?)", Math.Clamp(limit, 10, 10000));
    public Task<bool> SetFavoriteAsync(string id, bool favorite) => RunAsync(db => { db.Execute("UPDATE entries SET favorite=?,pinned=CASE WHEN ? THEN pinned ELSE 0 END WHERE id=?", favorite, favorite, id); return true; });
    public Task<bool> SetPinnedAsync(string id, bool pinned) => RunAsync(db => { db.Execute("UPDATE entries SET pinned=? WHERE id=? AND favorite=1", pinned, id); return true; });
    public Task<bool> TouchAsync(string id) => RunAsync(db => { db.Execute("UPDATE entries SET copied_at=MAX(?,(SELECT COALESCE(MAX(copied_at),0)+1 FROM entries)) WHERE id=?", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id); return true; });
    public Task<bool> DeleteAsync(string id) => RunAsync(db => { db.Execute("DELETE FROM entries WHERE id=?", id); SweepFiles(db); return true; });
    public Task<AppSettings> LoadSettingsAsync() => RunAsync(db =>
    {
        var values = db.Query("SELECT key,value FROM settings").ToDictionary(r => (string)r[0]!, r => (string)r[1]!);
        var theme = values.TryGetValue("theme", out var t) && Enum.TryParse<ThemeMode>(t, out var mode) && Enum.IsDefined(mode) ? mode : ThemeMode.System;
        var enabled = !values.TryGetValue("recording", out var e) || e != "False";
        var limit = values.TryGetValue("limit", out var l) && int.TryParse(l, out var n) ? Math.Clamp(n, 10, 10000) : 500;
        var backdrop = values.TryGetValue("backdrop", out var b) && Enum.TryParse<BackdropLevel>(b, out var level) && Enum.IsDefined(level) ? level : BackdropLevel.Strong;
        var sort = values.TryGetValue("favorite_sort", out var f) && Enum.TryParse<FavoriteSortMode>(f, out var order) && Enum.IsDefined(order) ? order : FavoriteSortMode.RecentCopy;
        var modifiers = values.TryGetValue("hotkey_modifiers", out var hm) && uint.TryParse(hm, out var m) && m is > 0 and <= 7 ? m : 1u;
        var key = values.TryGetValue("hotkey_key", out var hk) && uint.TryParse(hk, out var k) && (k is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7A) ? k : 0x56u;
        return new AppSettings(theme, enabled, limit, backdrop, sort, modifiers, key);
    });
    public Task<bool> SaveSettingsAsync(AppSettings settings) => RunAsync(db =>
    {
        db.Transaction(() =>
        {
            db.Execute("INSERT INTO settings(key,value) VALUES('hotkey_modifiers',?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", settings.HotkeyModifiers.ToString());
            db.Execute("INSERT INTO settings(key,value) VALUES('hotkey_key',?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", settings.HotkeyKey.ToString());
            foreach (var pair in new[] { ("theme", settings.Theme.ToString()), ("recording", settings.RecordingEnabled.ToString()), ("limit", Math.Clamp(settings.HistoryLimit, 10, 10000).ToString()), ("backdrop", settings.Backdrop.ToString()), ("favorite_sort", settings.FavoriteSort.ToString()) })
                db.Execute("INSERT INTO settings(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value", pair.Item1, pair.Item2);
            Prune(db, settings.HistoryLimit);
        });
        SweepFiles(db);
        return true;
    });
    public Task<bool> ClearAsync(bool allData) => RunAsync(db =>
    {
        db.Execute("PRAGMA secure_delete=ON");
        db.Transaction(() => { db.Execute("DELETE FROM entries"); if (allData) db.Execute("DELETE FROM settings"); });
        SweepFiles(db);
        db.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
        db.Execute("VACUUM");
        db.Execute("PRAGMA wal_checkpoint(TRUNCATE)");
        return true;
    });
    // Used only after the user confirms clearing ALL data when initialization failed.
    // Migrations never call this method, and initialization never deletes existing data.
    public async Task ResetBrokenDataAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                _ready = false;
                foreach (var name in new[] { "history.db", "history.db-wal", "history.db-shm" })
                {
                    var path = Path.Combine(RootPath, name);
                    if (File.Exists(path)) File.Delete(path);
                }
                if (Directory.Exists(ImagesPath))
                    foreach (var file in Directory.EnumerateFiles(ImagesPath)) File.Delete(file);
            }).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        await InitializeAsync().ConfigureAwait(false);
    }
    public string ResolveFile(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(RootPath, relative));
        if (!full.StartsWith(ImagesPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("图片路径超出本地图片目录。");
        return full;
    }
    private void SweepFiles(NativeSqlite db)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in db.Query("SELECT image_path,thumbnail_path FROM entries"))
            foreach (var value in row)
                if (value is string path)
                    try { keep.Add(ResolveFile(path)); } catch (InvalidDataException) { }
        foreach (var file in Directory.EnumerateFiles(ImagesPath))
            if (!keep.Contains(Path.GetFullPath(file))) File.Delete(file);
    }
}
