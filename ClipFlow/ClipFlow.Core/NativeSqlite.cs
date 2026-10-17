using System.Runtime.InteropServices;

namespace ClipFlow.Core;

// Windows ships SQLite as winsqlite3.dll. No third-party provider or network service is required.
public sealed class NativeSqlite : IDisposable
{
    private IntPtr _db;
    private const int Row = 100, Done = 101;
    public NativeSqlite(string path)
    {
        var result = sqlite3_open_v2(path, out _db, 0x2 | 0x4 | 0x10000, IntPtr.Zero);
        if (result != 0) { var message = Error(); Dispose(); throw new IOException(message); }
        sqlite3_busy_timeout(_db, 1000);
    }
    private string Error() => Marshal.PtrToStringUTF8(sqlite3_errmsg(_db)) ?? "SQLite error";
    public void Execute(string sql, params object?[] values)
    {
        using var statement = Prepare(sql, values);
        var result = sqlite3_step(statement.Handle);
        if (result != Done && result != Row) throw new IOException(Error());
    }
    public List<object?[]> Query(string sql, params object?[] values)
    {
        using var statement = Prepare(sql, values);
        var rows = new List<object?[]>();
        int result;
        while ((result = sqlite3_step(statement.Handle)) == Row)
        {
            var row = new object?[sqlite3_column_count(statement.Handle)];
            for (var i = 0; i < row.Length; i++)
                row[i] = sqlite3_column_type(statement.Handle, i) switch
                {
                    1 => sqlite3_column_int64(statement.Handle, i),
                    5 => null,
                    _ => Marshal.PtrToStringUTF8(sqlite3_column_text(statement.Handle, i), sqlite3_column_bytes(statement.Handle, i))
                };
            rows.Add(row);
        }
        if (result != Done) throw new IOException(Error());
        return rows;
    }
    public long Scalar(string sql, params object?[] values) => Convert.ToInt64(Query(sql, values)[0][0]);
    public void Transaction(Action action)
    {
        Execute("BEGIN IMMEDIATE");
        try { action(); Execute("COMMIT"); }
        catch { try { Execute("ROLLBACK"); } catch { } throw; }
    }
    private Statement Prepare(string sql, object?[] values)
    {
        if (sqlite3_prepare_v2(_db, sql, -1, out var handle, IntPtr.Zero) != 0) throw new IOException(Error());
        var statement = new Statement(handle);
        try
        {
            for (var i = 0; i < values.Length; i++)
            {
                var result = values[i] switch
                {
                    null => sqlite3_bind_null(handle, i + 1),
                    string text => sqlite3_bind_text(handle, i + 1, text, System.Text.Encoding.UTF8.GetByteCount(text), new IntPtr(-1)),
                    bool flag => sqlite3_bind_int64(handle, i + 1, flag ? 1 : 0),
                    _ => sqlite3_bind_int64(handle, i + 1, Convert.ToInt64(values[i]))
                };
                if (result != 0) throw new IOException(Error());
            }
            return statement;
        }
        catch { statement.Dispose(); throw; }
    }
    public void Dispose() { if (_db != IntPtr.Zero) { sqlite3_close_v2(_db); _db = IntPtr.Zero; } }
    private sealed class Statement(IntPtr handle) : IDisposable
    {
        public IntPtr Handle { get; } = handle;
        public void Dispose() => sqlite3_finalize(Handle);
    }
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close_v2(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_null(IntPtr statement, int index);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_text(IntPtr statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int bytes, IntPtr destructor);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_type(IntPtr statement, int index);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr statement, int index);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr statement, int index);
}
