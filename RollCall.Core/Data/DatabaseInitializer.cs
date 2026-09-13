using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace RollCall.Core.Data;

/// <summary>
/// 建库初始化与迁移（§A4.3 / §A4.4）：
/// 1. 确保目录存在；2. 幂等 DDL；3. schema 版本迁移；4. 空 names → 旧 JSON 迁移或内置 50 人；5. 空 app_config → 默认行。
/// 库文件损坏 → 改名保留 → 重建空库继续启动（§11），CorruptRecovered 供界面一次性提示。
/// </summary>
public sealed class DatabaseInitializer
{
    public const int CurrentSchemaVersion = 3;

    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS app_config (
            id                 INTEGER PRIMARY KEY CHECK (id = 1),
            refresh_interval   INTEGER NOT NULL DEFAULT 20,
            cooldown_times     INTEGER NOT NULL DEFAULT 5,
            window_width       INTEGER NOT NULL DEFAULT 600,
            window_height      INTEGER NOT NULL DEFAULT 450,
            window_title       TEXT    NOT NULL DEFAULT '综合高中252班随机点名程序',
            float_opacity      REAL    NOT NULL DEFAULT 0.95,
            name_label_height  INTEGER NOT NULL DEFAULT 180,
            name_font_size     INTEGER NOT NULL DEFAULT 45,
            name_label_width   INTEGER NOT NULL DEFAULT 0,
            control_btn_width  INTEGER NOT NULL DEFAULT 0,
            auto_mode          INTEGER NOT NULL DEFAULT 0,
            draw_count         INTEGER NOT NULL DEFAULT 1,
            read_aloud_enabled INTEGER NOT NULL DEFAULT 0,
            voice_name         TEXT    NOT NULL DEFAULT '',
            imported_file_path TEXT    NULL,
            float_position     TEXT    NOT NULL DEFAULT 'bottom-right',
            auto_start         INTEGER NOT NULL DEFAULT 0,
            updated_at         TEXT    NOT NULL
        );

        -- 名单表同时承载历史：不再使用的名单以 is_active=0 保留（§A4.2）
        CREATE TABLE IF NOT EXISTS names (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            name       TEXT    NOT NULL UNIQUE,
            sort_order INTEGER NOT NULL DEFAULT 0,
            source     TEXT    NOT NULL DEFAULT 'builtin',
            is_active  INTEGER NOT NULL DEFAULT 1
        );
        CREATE INDEX IF NOT EXISTS idx_names_active_order ON names(is_active, sort_order);

        -- ON DELETE RESTRICT 是刻意的：禁止物理删除有统计数据的姓名（§12-9）
        CREATE TABLE IF NOT EXISTS draw_stats (
            name_id       INTEGER PRIMARY KEY REFERENCES names(id) ON DELETE RESTRICT,
            draw_count    INTEGER NOT NULL DEFAULT 0,
            last_drawn_at TEXT    NULL
        );

        CREATE TABLE IF NOT EXISTS cooldown (
            name_id   INTEGER PRIMARY KEY REFERENCES names(id) ON DELETE CASCADE,
            remaining INTEGER NOT NULL CHECK (remaining > 0)
        );

        CREATE TABLE IF NOT EXISTS draw_history (
            id       INTEGER PRIMARY KEY AUTOINCREMENT,
            batch_id TEXT    NOT NULL,
            drawn_at TEXT    NOT NULL,
            name_id  INTEGER NOT NULL REFERENCES names(id) ON DELETE RESTRICT,
            mode     TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_history_time ON draw_history(drawn_at DESC);
        CREATE INDEX IF NOT EXISTS idx_history_name ON draw_history(name_id);

        CREATE TABLE IF NOT EXISTS schema_meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        INSERT INTO schema_meta(key, value) VALUES ('version', '1')
            ON CONFLICT(key) DO NOTHING;
        """;

    private readonly SqliteConnectionFactory _factory;
    private readonly ILogger<DatabaseInitializer>? _logger;

    /// <summary>库损坏已自动重建（用于一次性界面提示）。</summary>
    public bool CorruptRecovered { get; private set; }

    public DatabaseInitializer(SqliteConnectionFactory factory, ILogger<DatabaseInitializer>? logger = null)
    {
        _factory = factory;
        _logger = logger;
    }

    public void Initialize()
    {
        try
        {
            InitializeCore();
        }
        catch (SqliteException ex)
        {
            // 数据库文件损坏 / 无法打开：改名保留（不删除）→ 重建空库继续启动（§11）
            _logger?.LogError(ex, "数据库打开失败，按损坏处理并重建");
            TryRenameCorruptDatabase();
            CorruptRecovered = true;
            InitializeCore();
        }
    }

    private void TryRenameCorruptDatabase()
    {
        try
        {
            if (File.Exists(_factory.DbPath))
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Move(_factory.DbPath, $"{_factory.DbPath}.corrupt.{stamp}", overwrite: false);
            }

            // WAL / SHM 残留一并清掉，避免新库继续读到损坏页
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var side = _factory.DbPath + suffix;
                if (File.Exists(side))
                {
                    File.Delete(side);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "损坏库改名失败，尝试直接重建");
        }
    }

    private void InitializeCore()
    {
        using var conn = _factory.CreateOpenConnection();
        using (var ddl = conn.CreateCommand())
        {
            ddl.CommandText = Ddl;
            ddl.ExecuteNonQuery();
        }

        // 3) schema 版本迁移（当前只有 v1，占位以便未来按序执行迁移脚本）
        var version = ReadSchemaVersion(conn);
        if (version < CurrentSchemaVersion)
        {
            Migrate(conn, version);
            using var upd = conn.CreateCommand();
            upd.CommandText = "UPDATE schema_meta SET value = @v WHERE key = 'version'";
            upd.Parameters.AddWithValue("@v", CurrentSchemaVersion.ToString());
            upd.ExecuteNonQuery();
        }

        // 4) 空 names：旧 JSON 迁移，否则写内置默认名单
        if (CountRows(conn, "names") == 0)
        {
            if (!TryImportLegacyJson(conn))
            {
                InsertDefaultNames(conn);
            }
        }

        // 5) 空 app_config：插入一行全默认值
        if (CountRows(conn, "app_config") == 0)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO app_config (id, updated_at) VALUES (1, @updatedAt)
                """;
            cmd.Parameters.AddWithValue("@updatedAt", Now());
            cmd.ExecuteNonQuery();
        }
    }

    private static int ReadSchemaVersion(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM schema_meta WHERE key = 'version'";
        var v = cmd.ExecuteScalar() as string;
        return int.TryParse(v, out var n) ? n : 0;
    }

    private static void Migrate(SqliteConnection conn, int fromVersion)
    {
        // v1 → v2：app_config 增加 float_position（悬浮窗停靠位置，§用户需求）
        if (fromVersion < 2 && !ColumnExists(conn, "app_config", "float_position"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE app_config ADD COLUMN float_position TEXT NOT NULL DEFAULT 'bottom-right'";
            cmd.ExecuteNonQuery();
        }

        // v2 → v3：app_config 增加 auto_start（开机自启动记录位，§用户需求）
        if (fromVersion < 3 && !ColumnExists(conn, "app_config", "auto_start"))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE app_config ADD COLUMN auto_start INTEGER NOT NULL DEFAULT 0";
            cmd.ExecuteNonQuery();
        }

        // 未来版本迁移脚本按 fromVersion 顺序执行。
    }

    /// <summary>列是否存在（保证迁移幂等）。</summary>
    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountRows(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void InsertDefaultNames(SqliteConnection conn)
    {
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO names (name, sort_order, source, is_active) VALUES (@name, @order, 'builtin', 1)";
        var pName = cmd.Parameters.Add("@name", SqliteType.Text);
        var pOrder = cmd.Parameters.Add("@order", SqliteType.Integer);
        for (var i = 0; i < DefaultNames.List.Length; i++)
        {
            pName.Value = DefaultNames.List[i];
            pOrder.Value = i;
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    // ───────────────────────── 旧版 JSON 一次性迁移（§A4.4） ─────────────────────────

    /// <summary>旧 JSON 与数据库同目录（生产环境即 %LOCALAPPDATA%\RollCallApp\rollcall_data.json）。</summary>
    public string LegacyJsonPath => Path.Combine(
        Path.GetDirectoryName(_factory.DbPath) ?? ".", "rollcall_data.json");

    /// <summary>读取旧 rollcall_data.json 并整体迁移；任一异常 → 回滚并返回 false（随后走默认名单路径）。</summary>
    public bool TryImportLegacyJson(SqliteConnection conn)
    {
        var path = LegacyJsonPath;
        if (!File.Exists(path))
        {
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "旧 JSON 解析失败，忽略迁移");
            return false;
        }

        using (doc)
        {
            using var tx = conn.BeginTransaction();
            try
            {
                var root = doc.RootElement;

                // 1) name_list → names（source='builtin'，sort_order 取数组下标）
                var knownIds = new Dictionary<string, int>(StringComparer.Ordinal);
                if (root.TryGetProperty("name_list", out var nameList) && nameList.ValueKind == JsonValueKind.Array)
                {
                    using var ins = conn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT INTO names (name, sort_order, source, is_active) VALUES (@name, @order, 'builtin', 1)";
                    var pName = ins.Parameters.Add("@name", SqliteType.Text);
                    var pOrder = ins.Parameters.Add("@order", SqliteType.Integer);
                    var order = 0;
                    foreach (var item in nameList.EnumerateArray())
                    {
                        var name = item.GetString()?.Trim();
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        pName.Value = name;
                        pOrder.Value = order++;
                        ins.ExecuteNonQuery();
                        knownIds[name] = GetLastId(conn, tx);
                    }
                }

                // 2) draw_counts → draw_stats；姓名不在名单中则先插入并置 is_active=0（保留统计）
                if (root.TryGetProperty("draw_counts", out var drawCounts) && drawCounts.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in drawCounts.EnumerateObject())
                    {
                        var name = prop.Name.Trim();
                        if (string.IsNullOrEmpty(name) || !TryGetInt(prop.Value, out var count))
                        {
                            continue;
                        }

                        var id = ResolveNameId(conn, tx, knownIds, name);
                        using var up = conn.CreateCommand();
                        up.Transaction = tx;
                        up.CommandText = """
                            INSERT INTO draw_stats (name_id, draw_count, last_drawn_at) VALUES (@id, @cnt, NULL)
                            ON CONFLICT(name_id) DO UPDATE SET draw_count = excluded.draw_count
                            """;
                        up.Parameters.AddWithValue("@id", id);
                        up.Parameters.AddWithValue("@cnt", count);
                        up.ExecuteNonQuery();
                    }
                }

                // 3) cooldown → cooldown 表（remaining > 0）
                if (root.TryGetProperty("cooldown", out var cooldown) && cooldown.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in cooldown.EnumerateObject())
                    {
                        var name = prop.Name.Trim();
                        if (string.IsNullOrEmpty(name) || !TryGetInt(prop.Value, out var remaining) || remaining <= 0)
                        {
                            continue;
                        }

                        var id = ResolveNameId(conn, tx, knownIds, name);
                        using var up = conn.CreateCommand();
                        up.Transaction = tx;
                        up.CommandText = """
                            INSERT INTO cooldown (name_id, remaining) VALUES (@id, @remaining)
                            ON CONFLICT(name_id) DO UPDATE SET remaining = excluded.remaining
                            """;
                        up.Parameters.AddWithValue("@id", id);
                        up.Parameters.AddWithValue("@remaining", remaining);
                        up.ExecuteNonQuery();
                    }
                }

                // 4) app_config：16 个同名字段全部覆盖写入
                UpsertLegacyConfig(conn, tx, root);

                tx.Commit();
            }
            catch (Exception ex)
            {
                tx.Rollback();
                _logger?.LogError(ex, "旧 JSON 迁移失败，已回滚，将改用内置默认名单");
                return false;
            }
        }

        // 5) 迁移完成：旧文件重命名为 .migrated（不删除，便于回退）
        try
        {
            File.Move(path, path + ".migrated", overwrite: true);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "旧 JSON 重命名失败（迁移本身已完成）");
        }

        return true;
    }

    private static void UpsertLegacyConfig(SqliteConnection conn, SqliteTransaction tx, JsonElement root)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO app_config (id, refresh_interval, cooldown_times, window_width, window_height,
                                    window_title, float_opacity, name_label_height, name_font_size,
                                    name_label_width, control_btn_width, auto_mode, draw_count,
                                    read_aloud_enabled, voice_name, imported_file_path, updated_at)
            VALUES (1, @refreshInterval, @cooldownTimes, @windowWidth, @windowHeight,
                    @windowTitle, @floatOpacity, @nameLabelHeight, @nameFontSize,
                    @nameLabelWidth, @controlBtnWidth, @autoMode, @drawCount,
                    @readAloud, @voiceName, @importedFilePath, @updatedAt)
            ON CONFLICT(id) DO UPDATE SET
                refresh_interval   = excluded.refresh_interval,
                cooldown_times     = excluded.cooldown_times,
                window_width       = excluded.window_width,
                window_height      = excluded.window_height,
                window_title       = excluded.window_title,
                float_opacity      = excluded.float_opacity,
                name_label_height  = excluded.name_label_height,
                name_font_size     = excluded.name_font_size,
                name_label_width   = excluded.name_label_width,
                control_btn_width  = excluded.control_btn_width,
                auto_mode          = excluded.auto_mode,
                draw_count         = excluded.draw_count,
                read_aloud_enabled = excluded.read_aloud_enabled,
                voice_name         = excluded.voice_name,
                imported_file_path = excluded.imported_file_path,
                updated_at         = excluded.updated_at
            """;

        cmd.Parameters.AddWithValue("@refreshInterval", GetInt(root, "refresh_interval", 20));
        cmd.Parameters.AddWithValue("@cooldownTimes", GetInt(root, "cooldown_times", 5));
        cmd.Parameters.AddWithValue("@windowWidth", GetInt(root, "window_width", 600));
        cmd.Parameters.AddWithValue("@windowHeight", GetInt(root, "window_height", 450));
        cmd.Parameters.AddWithValue("@windowTitle", GetStr(root, "window_title", "综合高中252班随机点名程序"));
        cmd.Parameters.AddWithValue("@floatOpacity", GetDouble(root, "float_opacity", 0.95));
        cmd.Parameters.AddWithValue("@nameLabelHeight", GetInt(root, "name_label_height", 180));
        cmd.Parameters.AddWithValue("@nameFontSize", GetInt(root, "name_font_size", 45));
        cmd.Parameters.AddWithValue("@nameLabelWidth", GetInt(root, "name_label_width", 0));
        cmd.Parameters.AddWithValue("@controlBtnWidth", GetInt(root, "control_btn_width", 0));
        cmd.Parameters.AddWithValue("@autoMode", GetBoolAsInt(root, "auto_mode", false));
        cmd.Parameters.AddWithValue("@drawCount", GetInt(root, "draw_count", 1));
        cmd.Parameters.AddWithValue("@readAloud", GetBoolAsInt(root, "read_aloud_enabled", false));
        cmd.Parameters.AddWithValue("@voiceName", GetStr(root, "voice_name", string.Empty));
        cmd.Parameters.AddWithValue("@importedFilePath", root.TryGetProperty("imported_file_path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : DBNull.Value);
        cmd.Parameters.AddWithValue("@updatedAt", Now());
        cmd.ExecuteNonQuery();
    }

    /// <summary>draw_counts / cooldown 里的姓名不在 name_list 时：插入姓名并置 is_active=0（保留统计，§A4.4-2）。</summary>
    private static int ResolveNameId(SqliteConnection conn, SqliteTransaction tx, Dictionary<string, int> knownIds, string name)
    {
        if (knownIds.TryGetValue(name, out var id))
        {
            return id;
        }

        using var sel = conn.CreateCommand();
        sel.Transaction = tx;
        sel.CommandText = "SELECT id FROM names WHERE name = @name";
        sel.Parameters.AddWithValue("@name", name);
        var existing = sel.ExecuteScalar();
        if (existing is not null)
        {
            id = Convert.ToInt32(existing);
            knownIds[name] = id;
            return id;
        }

        using var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = "INSERT INTO names (name, sort_order, source, is_active) VALUES (@name, 0, 'builtin', 0)";
        ins.Parameters.AddWithValue("@name", name);
        ins.ExecuteNonQuery();
        id = GetLastId(conn, tx);
        knownIds[name] = id;
        return id;
    }

    private static int GetLastId(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int GetInt(JsonElement root, string key, int fallback) =>
        root.TryGetProperty(key, out var v) && TryGetInt(v, out var n) ? n : fallback;

    private static double GetDouble(JsonElement root, string key, double fallback) =>
        root.TryGetProperty(key, out var v) && TryGetDouble(v, out var d) ? d : fallback;

    private static string GetStr(JsonElement root, string key, string fallback) =>
        root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? fallback) : fallback;

    private static int GetBoolAsInt(JsonElement root, string key, bool fallback)
    {
        if (!root.TryGetProperty(key, out var v))
        {
            return fallback ? 1 : 0;
        }

        if (v.ValueKind == JsonValueKind.True)
        {
            return 1;
        }

        if (v.ValueKind == JsonValueKind.False)
        {
            return 0;
        }

        return TryGetInt(v, out var n) ? (n != 0 ? 1 : 0) : (fallback ? 1 : 0);
    }

    private static bool TryGetInt(JsonElement v, out int value)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                if (v.TryGetInt32(out value))
                {
                    return true;
                }

                value = (int)Math.Round(v.GetDouble());
                return true;
            case JsonValueKind.String:
                return int.TryParse(v.GetString(), out value);
            default:
                value = 0;
                return false;
        }
    }

    private static bool TryGetDouble(JsonElement v, out double value)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Number:
                value = v.GetDouble();
                return true;
            case JsonValueKind.String:
                return double.TryParse(v.GetString(), out value);
            default:
                value = 0;
                return false;
        }
    }

    private static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
}
