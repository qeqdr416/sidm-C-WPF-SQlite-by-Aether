using Microsoft.Extensions.Logging;
using RollCall.Core.Abstractions;
using RollCall.Core.Models;

namespace RollCall.Core.Algorithms;

/// <summary>
/// 抽选结算（§A5.3）：单事务结算冷却与统计 → 保存配置（含窗口实际尺寸）→ 语音播报 → 可选历史流水。
/// 写库失败不中断抽选流程（§11），仅记日志。
/// </summary>
public sealed class CooldownSettlement
{
    /// <summary>F14 抽取历史：表已建，默认不启用 UI 与写入（§14 自由度）。</summary>
    public static bool EnableDrawHistory { get; set; }

    private readonly IStatsRepository _stats;
    private readonly IConfigRepository _configRepo;
    private readonly ISpeechService _speech;
    private readonly ILogger<CooldownSettlement>? _logger;

    public CooldownSettlement(
        IStatsRepository stats,
        IConfigRepository configRepo,
        ISpeechService speech,
        ILogger<CooldownSettlement>? logger = null)
    {
        _stats = stats;
        _configRepo = configRepo;
        _speech = speech;
        _logger = logger;
    }

    /// <param name="drawn">本轮抽中者（基于 Candidate.Id，不按姓名反查）。</param>
    /// <param name="config">调用方已把窗口实际尺寸同步进该对象（§A7.2 / §12-13）。</param>
    /// <param name="mode">manual | auto。</param>
    public async Task ApplyAsync(IReadOnlyList<Candidate> drawn, AppConfig config, string mode)
    {
        // 1) 结算（单事务；先全体 -1、再置满 N，顺序不可颠倒，§12-8）
        try
        {
            await _stats.ApplySettlementAsync(drawn, config.CooldownTimes);
        }
        catch (Exception ex)
        {
            // SQLite 写入失败（磁盘满/占用）：记录日志，不中断抽选流程（§11）
            _logger?.LogWarning(ex, "抽选结算写库失败，内存态继续运行");
        }

        // 2) 持久化配置（窗口尺寸已在调用前同步进 config）
        try
        {
            await _configRepo.SaveAsync(config);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "配置保存失败，内存态继续运行");
        }

        // 3) 语音播报（朗读开关开启且有抽中者）
        if (config.ReadAloudEnabled && drawn.Count > 0 && _speech.IsAvailable)
        {
            try
            {
                await _speech.EnqueueAsync(drawn.Select(x => x.Name).ToArray());
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "语音播报失败");
            }
        }

        // 4) 可选：抽取历史流水（F14）
        if (EnableDrawHistory && drawn.Count > 0)
        {
            try
            {
                await _stats.WriteHistoryAsync(drawn, Guid.NewGuid().ToString(), mode);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "抽取历史写入失败");
            }
        }
    }
}
