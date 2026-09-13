namespace RollCall.Core.Models;

/// <summary>候选者（含数据库主键，后续结算与显示都基于 Id，避免按姓名字符串反查，§A5.1）。</summary>
public sealed record Candidate(int Id, string Name);

/// <summary>一轮抽选结果。</summary>
public sealed record RollResult(IReadOnlyList<Candidate> Drawn, string Mode);
