using Microsoft.Data.Sqlite;

namespace RollCall.Core.Data;

/// <summary>
/// SQLite 连接工厂（§A4.1）：
/// - 库路径：%LOCALAPPDATA%\RollCallApp\rollcall.db
/// - 每次连接建立后必须执行三条 PRAGMA；
/// - 短连接 + 内置连接池；写操作经 WriteLock 串行化；
/// - Microsoft.Data.Sqlite 的 *Async 是同步实现，所有访问经 Task.Run 放线程池（§12-7）。
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _dbDirectory;
    private readonly string _dbPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqliteConnectionFactory(string? dbDirectory = null)
    {
        _dbDirectory = dbDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RollCallApp");
        Directory.CreateDirectory(_dbDirectory);
        _dbPath = Path.Combine(_dbDirectory, "rollcall.db");
    }

    public string DbPath => _dbPath;

    public static string ConnectionStringFor(string dbPath) =>
        $"Data Source={dbPath};Cache=Shared;Foreign Keys=True";

    public string ConnectionString => ConnectionStringFor(_dbPath);

    /// <summary>新建并打开连接，执行 PRAGMA journal_mode=WAL / foreign_keys=ON / synchronous=NORMAL。</summary>
    public SqliteConnection CreateOpenConnection()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL;";
            cmd.ExecuteNonQuery();
        }

        return conn;
    }

    /// <summary>把同步 DB 访问放到线程池（§A4.1 线程约束）。</summary>
    public Task<T> RunAsync<T>(Func<SqliteConnection, T> action) => Task.Run(() =>
    {
        using var conn = CreateOpenConnection();
        return action(conn);
    });

    public Task RunAsync(Action<SqliteConnection> action) => Task.Run(() =>
    {
        using var conn = CreateOpenConnection();
        action(conn);
    });

    /// <summary>写操作：SemaphoreSlim(1,1) 串行化（§A4.1）。</summary>
    public async Task<T> RunInWriteLockAsync<T>(Func<SqliteConnection, T> action)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var conn = CreateOpenConnection();
            return action(conn);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RunInWriteLockAsync(Action<SqliteConnection> action)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            using var conn = CreateOpenConnection();
            action(conn);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
