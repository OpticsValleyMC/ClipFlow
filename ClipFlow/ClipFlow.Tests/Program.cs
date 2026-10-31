using ClipFlow.Core;

if (args.Length == 3 && args[0] == "--hold-clipboard")
{
    ClipboardLockFixture.Run(args[1], int.Parse(args[2]));
    return;
}

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException("ClipFlow tests use Windows winsqlite3.dll; run on Windows 10 build 19041 or later.");
// Independent of the checkout location and the caller's working directory.
var testRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "artifacts", "tests"));
var parent = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(parent);
var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
CapturedContent Text(string value) => new(ContentKind.Text, value, ContentIdentity.TextHash(value));
try
{
    var store = new HistoryStore(Path.Combine(parent, "fresh 数据目录"));
    await store.InitializeAsync();
    var first = await store.SaveAsync(Text("你好 ClipFlow\nsecond line ' SQL"), 500);
    await store.SetFavoriteAsync(first.Id, true);
    await Task.Delay(20);
    await store.SaveAsync(Text("another"), 500);
    var updated = await store.SaveAsync(Text(first.Text!), 500);
    var records = await store.LoadAsync();
    Check(records.Count == 2 && records[0].Id == first.Id && updated.IsFavorite && updated.CopiedAt > first.CopiedAt, "duplicate moves to top, updates time and preserves favorite");
    Check(records[0].Text == first.Text, "Unicode and SQL quoting round trip");
    var nul = await store.SaveAsync(Text("before\0after"), 500);
    Check((await store.LoadAsync()).Single(r => r.Id == nul.Id).Text == "before\0after", "embedded NUL is preserved");
    const string longPrefix = "开始😀\r\n", longSuffix = "\r\n结束🚀";
    var longText = longPrefix + new string('文', 2_000_000 - longPrefix.Length - longSuffix.Length) + longSuffix;
    var longRecord = await store.SaveAsync(Text(longText), 500);
    var longRestart = new HistoryStore(store.RootPath);
    await longRestart.InitializeAsync();
    Check((await longRestart.LoadAsync()).Single(r => r.Id == longRecord.Id).Text == longText, "two-million-character text survives database restart without truncation");
    var pages = new TextPages(longText);
    Check(pages.Count > 1 && string.Concat(Enumerable.Range(0, pages.Count).Select(pages.GetPage)) == longText, "long-text pages preserve every character and final content");
    var boundaries = new TextPages("abc😀\r\nxyz", 4);
    Check(string.Concat(Enumerable.Range(0, boundaries.Count).Select(boundaries.GetPage)) == "abc😀\r\nxyz"
        && Enumerable.Range(0, boundaries.Count).All(i => !char.IsLowSurrogate(boundaries.GetPage(i)[0]) && !char.IsHighSurrogate(boundaries.GetPage(i)[^1])), "page boundaries preserve surrogate pairs and CRLF");
    Check(TextPages.Preview(longText).Length <= 513 && new TextPages("").GetPage(0) == "", "list preview is bounded and empty details remain valid");
    await store.SaveSettingsAsync(new AppSettings(ThemeMode.Dark, false, 20));
    var restarted = new HistoryStore(store.RootPath);
    await restarted.InitializeAsync();
    Check((await restarted.LoadAsync()).Any(r => r.Id == first.Id && r.IsFavorite), "restart persistence");
    Check(await restarted.LoadSettingsAsync() == new AppSettings(ThemeMode.Dark, false, 20), "settings persistence");
    var pixels = new byte[] { 1, 2, 3, 255 };
    var hash = ContentIdentity.ImageHash(pixels, 1, 1);
    Check(hash != ContentIdentity.ImageHash(pixels, 1, 2), "image identity includes dimensions");
    var externalRejected = false;
    try { await store.SaveAsync(new CapturedContent(ContentKind.Image, null, hash, ImagePath: Path.Combine(Path.GetTempPath(), "external-screenshot.png")), 500); }
    catch (InvalidDataException) { externalRejected = true; }
    Check(externalRejected && !(await store.LoadAsync()).Any(r => r.Kind == ContentKind.Image), "image source paths cannot be persisted without an owned copy");
    var image = await store.SaveAsync(new CapturedContent(ContentKind.Image, null, hash, Width: 1, Height: 1, ImagePng: [1, 2, 3], ThumbnailPng: [4, 5]), 500);
    Check(File.Exists(store.ResolveFile(image.ImagePath!)) && File.Exists(store.ResolveFile(image.ThumbnailPath!)), "image and thumbnail saved outside SQLite");
    await store.SaveAsync(new CapturedContent(ContentKind.Image, null, hash, Width: 1, Height: 1, ImagePng: [1, 2, 3], ThumbnailPng: [4, 5]), 500);
    Check((await store.LoadAsync()).Count(r => r.Kind == ContentKind.Image) == 1, "image deduplication");
    await store.DeleteAsync(image.Id);
    Check(!Directory.EnumerateFiles(store.ImagesPath).Any(), "image delete removes original and thumbnail");
    for (var i = 0; i < 25; i++) await store.SaveAsync(Text("prune " + i), 10);
    Check((await store.LoadAsync()).Count == 10, "retention cap");
    await store.ClearAsync(false);
    Check((await store.LoadAsync()).Count == 0 && (await store.LoadSettingsAsync()).Theme == ThemeMode.Dark, "clear history preserves settings");
    await store.ClearAsync(true);
    Check(await store.LoadSettingsAsync() == new AppSettings(), "clear all resets settings");
    var migration = new HistoryStore(Path.Combine(parent, "migration"));
    Directory.CreateDirectory(migration.RootPath);
    using (var db = new NativeSqlite(migration.DatabasePath))
    {
        db.Execute("CREATE TABLE entries(id TEXT PRIMARY KEY, kind INTEGER NOT NULL, text TEXT, hash TEXT NOT NULL, image_path TEXT, width INTEGER NOT NULL DEFAULT 0, height INTEGER NOT NULL DEFAULT 0, created_at INTEGER NOT NULL, copied_at INTEGER NOT NULL, favorite INTEGER NOT NULL DEFAULT 0)");
        db.Execute("CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL)");
        db.Execute("INSERT INTO entries VALUES('old',0,'legacy','legacy-hash',NULL,0,0,1,2,1)");
        db.Execute("INSERT INTO entries VALUES('new',0,'legacy','legacy-hash',NULL,0,0,1,3,0)");
        db.Execute("INSERT INTO settings VALUES('theme','Dark')");
        db.Execute("PRAGMA user_version=1");
    }
    await migration.InitializeAsync();
    var migrated = await migration.LoadAsync();
    Check(migrated.Count == 1 && migrated[0].Id == "new" && migrated[0].IsFavorite && migrated[0].Text == "legacy", "v1 migration preserves newest content and merges favorite");
    Check((await migration.LoadSettingsAsync()).Theme == ThemeMode.Dark, "migration preserves settings");
    using (var db = new NativeSqlite(migration.DatabasePath)) Check(db.Scalar("PRAGMA user_version") == HistoryStore.SchemaVersion, "schema version");
    var v2 = new HistoryStore(Path.Combine(parent, "v2 migration"));
    Directory.CreateDirectory(v2.ImagesPath);
    File.WriteAllBytes(Path.Combine(v2.ImagesPath, "old.png"), [1, 2, 3]);
    File.WriteAllBytes(Path.Combine(v2.ImagesPath, "old.thumb.png"), [4, 5]);
    using (var db = new NativeSqlite(v2.DatabasePath))
    {
        db.Execute("CREATE TABLE entries(id TEXT PRIMARY KEY,kind INTEGER NOT NULL,text TEXT,hash TEXT NOT NULL,image_path TEXT,width INTEGER NOT NULL,height INTEGER NOT NULL,created_at INTEGER NOT NULL,copied_at INTEGER NOT NULL,favorite INTEGER NOT NULL,thumbnail_path TEXT)");
        db.Execute("CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL)");
        db.Execute("CREATE UNIQUE INDEX idx_content ON entries(kind,hash)");
        db.Execute("INSERT INTO entries VALUES('text',0,'旧版收藏','text',NULL,0,0,1,2,1,NULL)");
        db.Execute("INSERT INTO entries VALUES('image',1,NULL,'image','images/old.png',2,3,1,3,1,'images/old.thumb.png')");
        db.Execute("INSERT INTO settings VALUES('theme','Dark')");
        db.Execute("PRAGMA user_version=2");
    }
    await v2.InitializeAsync();
    Check((await v2.LoadAsync()) is var oldRows && oldRows.Count == 2 && oldRows.All(r => r.IsFavorite && !r.IsPinned)
        && oldRows.Single(r => r.Id == "text").Text == "旧版收藏" && File.Exists(Path.Combine(v2.ImagesPath, "old.png"))
        && File.Exists(Path.Combine(v2.ImagesPath, "old.thumb.png")), "v2 to v3 preserves favorites, text and owned image files");
    Check((await v2.LoadSettingsAsync()) is { Theme: ThemeMode.Dark, Backdrop: BackdropLevel.Strong, FavoriteSort: FavoriteSortMode.RecentCopy }, "old settings receive compatible backdrop and sort defaults");
    var favoriteStore = new HistoryStore(Path.Combine(parent, "favorites"));
    await favoriteStore.InitializeAsync();
    var alpha = await favoriteStore.SaveAsync(Text("Alpha"), 500);
    var beta = await favoriteStore.SaveAsync(Text("Beta"), 500);
    var other = await favoriteStore.SaveAsync(Text("not favorite"), 500);
    await favoriteStore.SetFavoriteAsync(alpha.Id, true); await favoriteStore.SetFavoriteAsync(beta.Id, true);
    await favoriteStore.SetPinnedAsync(alpha.Id, true); await favoriteStore.SetPinnedAsync(other.Id, true);
    var favorites = await favoriteStore.LoadAsync();
    Check(FavoriteOrdering.Apply(favorites, FavoriteSortMode.RecentCopy).First().Id == alpha.Id
        && favorites.First().Id == other.Id && !favorites.Single(r => r.Id == other.Id).IsPinned, "pinning affects favorites only and cannot pin non-favorites");
    await favoriteStore.SaveAsync(Text("Alpha"), 500);
    Check((await favoriteStore.LoadAsync()).Single(r => r.Id == alpha.Id).IsPinned, "duplicate copy preserves favorite pin");
    await favoriteStore.SetPinnedAsync(alpha.Id, false);
    favorites = await favoriteStore.LoadAsync();
    Check(FavoriteOrdering.Apply(favorites, FavoriteSortMode.RecentCopy).First().Id == alpha.Id
        && FavoriteOrdering.Apply(favorites, FavoriteSortMode.OldestCopy).First().Id == beta.Id
        && FavoriteOrdering.Apply(favorites, FavoriteSortMode.Content).First().Id == alpha.Id, "favorite time and content ordering use actual content and copy times");
    await favoriteStore.SetPinnedAsync(beta.Id, true);
    await favoriteStore.SaveSettingsAsync(new AppSettings(Backdrop: BackdropLevel.Solid, FavoriteSort: FavoriteSortMode.Content));
    var featureRestart = new HistoryStore(favoriteStore.RootPath); await featureRestart.InitializeAsync();
    Check((await featureRestart.LoadAsync()).Single(r => r.Id == beta.Id).IsPinned
        && (await featureRestart.LoadSettingsAsync()) is { Backdrop: BackdropLevel.Solid, FavoriteSort: FavoriteSortMode.Content }, "pin, backdrop and favorite sort survive restart");
    await favoriteStore.SetFavoriteAsync(beta.Id, false);
    await favoriteStore.SetFavoriteAsync(beta.Id, true);
    Check(!(await favoriteStore.LoadAsync()).Single(r => r.Id == beta.Id).IsPinned, "removing a favorite clears its pin before re-favoriting");
    using (var db = new NativeSqlite(favoriteStore.DatabasePath))
    { db.Execute("UPDATE settings SET value='999' WHERE key IN ('backdrop','favorite_sort')"); }
    Check((await favoriteStore.LoadSettingsAsync()) is { Backdrop: BackdropLevel.Strong, FavoriteSort: FavoriteSortMode.RecentCopy }, "invalid appearance and sort settings fall back safely");
    using (var db = new NativeSqlite(migration.DatabasePath)) db.Execute("PRAGMA user_version=99");
    var rejected = false;
    try { await new HistoryStore(migration.RootPath).InitializeAsync(); } catch (InvalidDataException) { rejected = true; }
    using (var db = new NativeSqlite(migration.DatabasePath)) Check(rejected && db.Scalar("SELECT COUNT(*) FROM entries") == 1, "future schema rejected without data loss");
    var broken = new HistoryStore(Path.Combine(parent, "broken"));
    Directory.CreateDirectory(broken.RootPath);
    var damaged = new byte[] { 0, 1, 2, 3, 4 };
    await File.WriteAllBytesAsync(broken.DatabasePath, damaged);
    var failed = false;
    try { await broken.InitializeAsync(); } catch (IOException) { failed = true; }
    Check(failed && File.ReadAllBytes(broken.DatabasePath).SequenceEqual(damaged), "initialization failure retains corrupt database");
    await broken.ResetBrokenDataAsync();
    Check((await broken.LoadAsync()).Count == 0 && await broken.LoadSettingsAsync() == new AppSettings(), "explicit clear-all can recover failed initialization");
    var attempts = 0;
    var result = await RetryPolicy.RunAsync(() => { if (++attempts < 3) throw new IOException(); return Task.FromResult(42); }, e => e is IOException);
    Check(result == 42 && attempts == 3, "temporary failure recovers");
    attempts = 0;
    try { await RetryPolicy.RunAsync<int>(() => { attempts++; throw new IOException(); }, _ => true); } catch (IOException) { }
    Check(attempts == 4, "persistent failure has bounded retries");
    attempts = 0;
    try { await RetryPolicy.RunAsync<int>(() => { attempts++; throw new InvalidDataException(); }, _ => false); } catch (InvalidDataException) { }
    Check(attempts == 1, "unsupported format is not retried");
    var canceled = new CancellationTokenSource(); canceled.Cancel();
    try { await RetryPolicy.RunAsync(() => Task.FromResult(0), _ => true, canceled.Token); } catch (OperationCanceledException) { passed++; Console.WriteLine("PASS: cancellation"); }
    Console.WriteLine($"{passed} checks passed.");
}
finally
{
    // Only this generated test directory is removed; application history is never touched.
    if (Path.GetFullPath(parent).StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(parent, true);
}
