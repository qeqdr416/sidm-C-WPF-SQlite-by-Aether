using Microsoft.Data.Sqlite;
using RollCall.Core.Abstractions;
using RollCall.Core.Models;

namespace RollCall.Core.Data;

/// <summary>单行配置仓储（§A4.5）：Load 读取 id=1，Save 用 UPSERT。</summary>
public sealed class ConfigRepository : IConfigRepository
{
    private readonly SqliteConnectionFactory _factory;

    public ConfigRepository(SqliteConnectionFactory factory) => _factory = factory;

    public Task<AppConfig> LoadAsync() => _factory.RunAsync(Load);

    internal static AppConfig Load(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT refresh_interval, cooldown_times, window_width, window_height, window_title,
                   float_opacity, name_label_height, name_font_size, name_label_width,
                   control_btn_width, auto_mode, draw_count, read_aloud_enabled, voice_name,
                   imported_file_path, float_position, auto_start
            FROM app_config WHERE id = 1
            """;
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return new AppConfig();
        }

        return new AppConfig
        {
            RefreshInterval = r.GetInt32(0),
            CooldownTimes = r.GetInt32(1),
            WindowWidth = r.GetInt32(2),
            WindowHeight = r.GetInt32(3),
            WindowTitle = r.IsDBNull(4) ? string.Empty : r.GetString(4),
            FloatOpacity = r.GetDouble(5),
            NameLabelHeight = r.GetInt32(6),
            NameFontSize = r.GetInt32(7),
            NameLabelWidth = r.GetInt32(8),
            ControlButtonWidth = r.GetInt32(9),
            AutoMode = r.GetInt32(10) != 0,
            DrawCount = r.GetInt32(11),
            ReadAloudEnabled = r.GetInt32(12) != 0,
            VoiceName = r.IsDBNull(13) ? string.Empty : r.GetString(13),
            ImportedFilePath = r.IsDBNull(14) ? null : r.GetString(14),
            FloatPosition = r.IsDBNull(15) ? "bottom-right" : r.GetString(15),
            AutoStart = !r.IsDBNull(16) && r.GetInt32(16) != 0,
        };
    }

    public Task SaveAsync(AppConfig config) => _factory.RunInWriteLockAsync(conn =>
    {
        Save(conn, config);
        return true;
    });

    internal static void Save(SqliteConnection conn, AppConfig config)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO app_config (id, refresh_interval, cooldown_times, window_width, window_height,
                                    window_title, float_opacity, name_label_height, name_font_size,
                                    name_label_width, control_btn_width, auto_mode, draw_count,
                                    read_aloud_enabled, voice_name, imported_file_path, float_position,
                                    auto_start, updated_at)
            VALUES (1, @refreshInterval, @cooldownTimes, @windowWidth, @windowHeight,
                    @windowTitle, @floatOpacity, @nameLabelHeight, @nameFontSize,
                    @nameLabelWidth, @controlBtnWidth, @autoMode, @drawCount,
                    @readAloud, @voiceName, @importedFilePath, @floatPosition,
                    @autoStart, @updatedAt)
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
                float_position     = excluded.float_position,
                auto_start         = excluded.auto_start,
                updated_at         = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@refreshInterval", config.RefreshInterval);
        cmd.Parameters.AddWithValue("@cooldownTimes", config.CooldownTimes);
        cmd.Parameters.AddWithValue("@windowWidth", config.WindowWidth);
        cmd.Parameters.AddWithValue("@windowHeight", config.WindowHeight);
        cmd.Parameters.AddWithValue("@windowTitle", string.IsNullOrWhiteSpace(config.WindowTitle)
            ? AppConstants.DefaultWindowTitle
            : config.WindowTitle);
        cmd.Parameters.AddWithValue("@floatOpacity", config.FloatOpacity);
        cmd.Parameters.AddWithValue("@nameLabelHeight", config.NameLabelHeight);
        cmd.Parameters.AddWithValue("@nameFontSize", config.NameFontSize);
        cmd.Parameters.AddWithValue("@nameLabelWidth", config.NameLabelWidth);
        cmd.Parameters.AddWithValue("@controlBtnWidth", config.ControlButtonWidth);
        cmd.Parameters.AddWithValue("@autoMode", config.AutoMode ? 1 : 0);
        cmd.Parameters.AddWithValue("@drawCount", config.DrawCount);
        cmd.Parameters.AddWithValue("@readAloud", config.ReadAloudEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@voiceName", config.VoiceName);
        cmd.Parameters.AddWithValue("@importedFilePath", (object?)config.ImportedFilePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@floatPosition", config.FloatPosition);
        cmd.Parameters.AddWithValue("@autoStart", config.AutoStart ? 1 : 0);
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();
    }
}
