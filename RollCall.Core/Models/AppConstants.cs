namespace RollCall.Core.Models;

/// <summary>
/// 全局具名常量（§10，数值必须与规格一致）。
/// </summary>
public static class AppConstants
{
    public const string Version = "4.1";
    public const string UpdateUrl = "https://raw.githubusercontent.com/qeqdr416/random_name/refs/heads/main/version.json";
    public const int MaxDrawCount = 12;
    public const int NamesPerLine = 4;
    public const int AutoWidth = 0;              // 宽度 0 表示“自动”（§12-11）
    public const int NetworkTimeoutMs = 8000;
    public const int AutoStopMinMs = 400;
    public const int AutoStopMaxMs = 1200;       // 随机区间 [400, 1200]
    public const string ServerName = "RollCallAppServer";
    public const string PipeName = "RollCallAppServerPipe";

    public const int DefaultRefreshInterval = 20;
    public const int DefaultCooldownTimes = 5;
    public const int DefaultDrawCount = 1;
    public const int DefaultNameLabelHeight = 180;
    public const int DefaultNameFontSize = 45;
    public const int DefaultWindowWidth = 600;
    public const int DefaultWindowHeight = 525;
    public const string DefaultWindowTitle = "综合高中252班随机点名程序";
    public const double DefaultFloatOpacity = 0.95;

    /// <summary>预设音色（显示标签，关键字）——关键字存库而非完整音色名（§12-10）。</summary>
    public static readonly (string Label, string Key)[] PresetVoices =
    {
        ("Microsoft 慧慧（女声）", "Huihui"),
        ("Microsoft 瑶瑶（女声）", "Yaoyao"),
        ("Microsoft 康康（男声）", "Kangkang"),
    };

    /// <summary>试听文案（§5 A5.5，逐字一致）。</summary>
    public const string PreviewSentence = "准备好了吗？现在开始点名。";
}
