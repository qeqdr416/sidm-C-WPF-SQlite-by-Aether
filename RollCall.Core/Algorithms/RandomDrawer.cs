using RollCall.Core.Abstractions;
using RollCall.Core.Models;

namespace RollCall.Core.Algorithms;

/// <summary>
/// 三级降级随机抽取（§A5.1）：
/// 1. 第一优先候选（不在冷却池）足够 → 无放回抽样；
/// 2. 第二优先候选（全部启用名单）足够 → 无放回抽样；
/// 3. 兜底：有放回重复抽样。
/// </summary>
public sealed class RandomDrawer
{
    public const int MaxDrawCount = AppConstants.MaxDrawCount;

    private readonly INameRepository _names;

    public RandomDrawer(INameRepository names) => _names = names;

    public async Task<IReadOnlyList<Candidate>> DrawAsync(int drawCount)
    {
        // 期望人数上限 12（具名常量）
        var total = Math.Clamp(drawCount, 1, MaxDrawCount);

        // 启用名单为空 → 返回空集合，显示区不变、不抛异常（§11）
        if (await _names.CountActiveAsync() == 0)
        {
            return Array.Empty<Candidate>();
        }

        // 第一优先：启用中且不在冷却池
        var first = await _names.GetCandidatesOutsideCooldownAsync(total);
        if (first.Count >= total)
        {
            return SampleNoRepeat(first, total);
        }

        // 第二优先：全部启用名单
        var second = await _names.GetActiveCandidatesAsync(total);
        if (second.Count >= total)
        {
            return SampleNoRepeat(second, total);
        }

        // 兜底：有放回（允许重复）
        var all = await _names.GetAllActiveAsync();
        if (all.Count == 0)
        {
            return Array.Empty<Candidate>();
        }

        var result = new List<Candidate>(total);
        for (var i = 0; i < total; i++)
        {
            result.Add(all[Random.Shared.Next(all.Count)]);
        }

        return result;
    }

    /// <summary>无放回抽样：部分 Fisher-Yates，只洗前 <paramref name="take"/> 个（§A5.1）。</summary>
    public static IReadOnlyList<Candidate> SampleNoRepeat(IReadOnlyList<Candidate> source, int take)
    {
        var arr = source.ToArray();
        var n = Math.Min(take, arr.Length);
        for (var i = 0; i < n; i++)
        {
            var j = Random.Shared.Next(i, arr.Length);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }

        return arr.Take(n).ToArray();
    }
}
