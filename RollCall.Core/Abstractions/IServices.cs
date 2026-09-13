using RollCall.Core.Models;

namespace RollCall.Core.Abstractions;

/// <summary>语音播报（§A5.5）。实现方在 App 层（WinRT 或 System.Speech），接口不变。</summary>
public interface ISpeechService
{
    /// <summary>语音引擎是否可用（AllVoices 为空或合成异常 → false，§A5.5）。</summary>
    bool IsAvailable { get; }

    /// <summary>入队朗读：Stop → ApplyVoice → 逐句播放。</summary>
    Task EnqueueAsync(IReadOnlyList<string> names);

    /// <summary>停止并清空队列。</summary>
    void Stop();

    /// <summary>按关键字（Id/DisplayName 精确或子串、忽略大小写）选择音色；空则用默认选音规则。</summary>
    bool ApplyVoice(string voiceKey);

    /// <summary>音色关键字是否已安装在系统中（用于配置下拉框标记“需安装”）。</summary>
    bool IsVoiceInstalled(string voiceKey);
}

/// <summary>点击音效（§A6.7）。与朗读各持一个 MediaPlayer，绝不共用（§12-17）。</summary>
public interface ISoundService
{
    bool IsAvailable { get; }
    void Play();
}

/// <summary>版本更新检查（F12，可选；失败静默，§11）。</summary>
public interface IUpdateService
{
    /// <summary>返回远端最新版本号；网络失败 / JSON 非法返回 null（静默）。</summary>
    Task<string?> FetchLatestVersionAsync();

    /// <summary>下载链接协议校验（仅 http/https）后打开浏览器，非法返回 false 并记 Warning。</summary>
    bool OpenDownloadPage(string url);
}

/// <summary>文件导入（txt / xlsx / xls，§A6.8）。ExcelDataReader 缺失时不崩溃而是返回失败原因。</summary>
public interface IFileImportService
{
    /// <summary>导入结果：成功时 Names 非空；失败时 Error 非空。</summary>
    Task<FileImportResult> ImportAsync(string filePath);
}

/// <summary>文件导入结果。</summary>
public sealed class FileImportResult
{
    public bool Success { get; init; }
    public IReadOnlyList<string> Names { get; init; } = Array.Empty<string>();
    /// <summary>失败种类：null / "excelMissing" / "empty" / "error"。</summary>
    public string? FailureKind { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>单实例守卫（§A6.5）。AppInstance 为主方案，Mutex + 命名管道为降级。</summary>
public interface ISingleInstanceService
{
    /// <summary>尝试成为主实例；false 表示已有实例在运行。</summary>
    bool TryAcquirePrimary();

    /// <summary>把激活请求重定向给主实例（主实例将还原窗口）。</summary>
    Task RedirectActivationAsync();

    /// <summary>主实例订阅“被再次激活”事件，回调在主实例侧触发（需自行调度回 UI 线程）。</summary>
    void Subscribe(Action onRedirected);
}
