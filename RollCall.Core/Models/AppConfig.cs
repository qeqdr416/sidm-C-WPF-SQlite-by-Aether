namespace RollCall.Core.Models;

/// <summary>
/// 单行配置（对应 app_config 表，§A4.2）。字段与旧版 JSON 同名同义（§9）。
/// </summary>
public sealed class AppConfig
{
    public int RefreshInterval { get; set; } = AppConstants.DefaultRefreshInterval;
    public int CooldownTimes { get; set; } = AppConstants.DefaultCooldownTimes;
    public int WindowWidth { get; set; } = AppConstants.DefaultWindowWidth;
    public int WindowHeight { get; set; } = AppConstants.DefaultWindowHeight;
    public string WindowTitle { get; set; } = AppConstants.DefaultWindowTitle;
    public double FloatOpacity { get; set; } = AppConstants.DefaultFloatOpacity;
    public int NameLabelHeight { get; set; } = AppConstants.DefaultNameLabelHeight;
    public int NameFontSize { get; set; } = AppConstants.DefaultNameFontSize;
    public int NameLabelWidth { get; set; } = AppConstants.AutoWidth;
    public int ControlButtonWidth { get; set; } = AppConstants.AutoWidth;
    public bool AutoMode { get; set; }
    public int DrawCount { get; set; } = AppConstants.DefaultDrawCount;
    public bool ReadAloudEnabled { get; set; }
    /// <summary>存音色“关键字”（如 Huihui），不是完整音色名（§12-10）。</summary>
    public string VoiceName { get; set; } = string.Empty;
    /// <summary>仅作记录，一致性校验逻辑已废弃（§9）。</summary>
    public string? ImportedFilePath { get; set; }

    /// <summary>悬浮窗停靠位置：bottom-right（默认）/ bottom-left / top-right / top-left。</summary>
    public string FloatPosition { get; set; } = "bottom-right";

    /// <summary>开机自启动（写库记录；实际开关以 HKCU Run 注册表键为准）。</summary>
    public bool AutoStart { get; set; }
}
