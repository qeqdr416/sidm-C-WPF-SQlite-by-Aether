using RollCall.Core.Models;

namespace RollCall.Core.Abstractions;

/// <summary>单行配置仓储（§A4.5）。</summary>
public interface IConfigRepository
{
    Task<AppConfig> LoadAsync();
    Task SaveAsync(AppConfig config);
}

/// <summary>名单仓储（§A4.5）。禁止物理删除姓名，改名单只做 is_active 翻转（§12-9）。</summary>
public interface INameRepository
{
    /// <summary>第一优先候选：启用中且不在冷却池。</summary>
    Task<IReadOnlyList<Candidate>> GetCandidatesOutsideCooldownAsync(int take);

    /// <summary>第二优先候选：全部启用名单。</summary>
    Task<IReadOnlyList<Candidate>> GetActiveCandidatesAsync(int take);

    /// <summary>兜底候选（有放回）：全部启用名单。</summary>
    Task<IReadOnlyList<Candidate>> GetAllActiveAsync();

    /// <summary>替换名单（导入 / 恢复默认）：旧行 is_active=0、清空冷却，单事务内 UPSERT 新名单。</summary>
    Task<int> ReplaceAllAsync(IReadOnlyList<string> names, string source);

    /// <summary>手动追加（保持顺序，忽略已存在）。</summary>
    Task<int> AddNamesAsync(IReadOnlyList<string> names);

    /// <summary>当前启用名单（按 sort_order）。</summary>
    Task<IReadOnlyList<Candidate>> GetActiveOrderedListAsync();

    /// <summary>启用名单数量（用于抽取前的空判断）。</summary>
    Task<int> CountActiveAsync();
}

/// <summary>抽取统计与冷却池仓储（§A4.5）。结算 SQL 顺序不可颠倒（§12-8）。</summary>
public interface IStatsRepository
{
    /// <summary>
    /// 结算：单事务内 先全体冷却 remaining-1 并删除 ≤0，再给抽中者 draw_count+1、置 remaining=N。
    /// </summary>
    Task ApplySettlementAsync(IReadOnlyList<Candidate> drawn, int cooldownTimes);

    /// <summary>统计面板：只显示 cnt &gt; 0，按次数降序、姓名升序。</summary>
    Task<IReadOnlyList<StatsRow>> GetStatsAsync();

    /// <summary>总次数 = SUM(draw_stats.draw_count)。</summary>
    Task<long> GetTotalDrawCountAsync();

    /// <summary>可选 F14：写抽取历史流水（batch 内共用一个 batch_id）。</summary>
    Task WriteHistoryAsync(IReadOnlyList<Candidate> drawn, string batchId, string mode);
}
