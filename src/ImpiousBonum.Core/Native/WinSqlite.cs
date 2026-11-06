using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

/// <summary>
/// Read-only queries on a SQLite database with the copy of SQLite that comes with Windows (<c>winsqlite3.dll</c>), so
/// reading another program's database, such as GOG Galaxy's, needs no library of the app's own. The file is opened
/// read-only alongside the program that writes it, and a query waits at most a second while it's busy writing.
/// </summary>
internal sealed partial class WinSqlite : IDisposable
{
    private const string Library = "winsqlite3.dll";
    private const int Ok = 0;
    private const int Row = 100;
    private const int Done = 101;
    private const int OpenReadOnly = 0x1;
    private const int Null = 5;

    /// <summary>SQLITE_TRANSIENT: SQLite copies a bound string, so it needn't outlive the call.</summary>
    private static readonly IntPtr Transient = new(-1);

    private IntPtr _db;

    private WinSqlite(IntPtr db) => _db = db;

    /// <summary>The database at <paramref name="path"/>, read-only. Throws <see cref="IOException"/> if it can't be opened.</summary>
    public static WinSqlite Open(string path)
    {
        var result = sqlite3_open_v2(path, out var db, OpenReadOnly, IntPtr.Zero);
        if (result != Ok)
        {
            sqlite3_close_v2(db);
            throw new IOException($"SQLite couldn't open {path} ({result}).");
        }
        sqlite3_busy_timeout(db, 1000);
        return new WinSqlite(db);
    }

    /// <summary>
    /// Each row <paramref name="sql"/> gives, with <paramref name="parameters"/> bound to its <c>?</c>s in order, as
    /// <paramref name="read"/> makes of it. Throws <see cref="IOException"/> if SQLite can't run it, as when the database
    /// is busy for too long or isn't laid out as expected.
    /// </summary>
    public List<T> Query<T>(string sql, IReadOnlyList<string> parameters, Func<SqliteRow, T> read)
    {
        ObjectDisposedException.ThrowIf(_db == IntPtr.Zero, this);
        if (sqlite3_prepare16_v2(_db, sql, -1, out var statement, IntPtr.Zero) is var prepared and not Ok)
            throw Failed("prepare", prepared);
        try
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                if (sqlite3_bind_text16(statement, i + 1, parameters[i], -1, Transient) is var bound and not Ok)
                    throw Failed("bind", bound);
            }
            var rows = new List<T>();
            while (true)
            {
                var step = sqlite3_step(statement);
                if (step == Done)
                    return rows;
                if (step != Row)
                    throw Failed("step", step);
                rows.Add(read(new SqliteRow(statement)));
            }
        }
        finally
        {
            sqlite3_finalize(statement);
        }
    }

    private IOException Failed(string what, int result) =>
        new($"SQLite {what} failed ({result}): {Marshal.PtrToStringUni(sqlite3_errmsg16(_db))}");

    public void Dispose()
    {
        if (_db == IntPtr.Zero)
            return;
        sqlite3_close_v2(_db);
        _db = IntPtr.Zero;
    }

    /// <summary>The row a query is on, read by column number from 0.</summary>
    internal readonly struct SqliteRow(IntPtr statement)
    {
        public bool IsNull(int column) => sqlite3_column_type(statement, column) == Null;

        public string? Text(int column) => IsNull(column) ? null : Marshal.PtrToStringUni(sqlite3_column_text16(statement, column));

        public long? Long(int column) => IsNull(column) ? null : sqlite3_column_int64(statement, column);
    }

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int sqlite3_open_v2(string filename, out IntPtr db, int flags, IntPtr vfs);

    [LibraryImport(Library)]
    private static partial int sqlite3_close_v2(IntPtr db);

    [LibraryImport(Library)]
    private static partial int sqlite3_busy_timeout(IntPtr db, int milliseconds);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int sqlite3_prepare16_v2(IntPtr db, string sql, int bytes, out IntPtr statement, IntPtr tail);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int sqlite3_bind_text16(IntPtr statement, int index, string text, int bytes, IntPtr destructor);

    [LibraryImport(Library)]
    private static partial int sqlite3_step(IntPtr statement);

    [LibraryImport(Library)]
    private static partial int sqlite3_column_type(IntPtr statement, int column);

    [LibraryImport(Library)]
    private static partial long sqlite3_column_int64(IntPtr statement, int column);

    [LibraryImport(Library)]
    private static partial IntPtr sqlite3_column_text16(IntPtr statement, int column);

    [LibraryImport(Library)]
    private static partial int sqlite3_finalize(IntPtr statement);

    [LibraryImport(Library)]
    private static partial IntPtr sqlite3_errmsg16(IntPtr db);
}
