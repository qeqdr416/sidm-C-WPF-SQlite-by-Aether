using Microsoft.Data.Sqlite;
using RollCall.Core.Abstractions;
using RollCall.Core.Models;

namespace RollCall.Core.Data;

/// <summary>统计与冷却池仓储（§A4.5）。结算 SQL 顺序不可颠倒（§12-8）。</summary>
public sealed class StatsRepository : IStatsRepository
{
    private readonly SqliteConnectionFactory _factory;

    public StatsRepository(SqliteConnectionFactory factory) => _factory = factory;

    public Task ApplySettlementAsync(IReadOnlyList<Candidate> drawn, int cooldownTimes) =>
        _factory.RunInWriteLockAsync(conn =>
        {
            using var tx = conn.BeginTransaction();

            // 规格自洽性修正：§A4.2 的 CHECK (remaining > 0) 会在 UPDATE 减到 0 时立即报错，
            // 直接执行 §A4.5 字面 SQL（先 UPDATE 后 DELETE）无法通过约束。
            // 等价实现：先删除本轮将归零者（remaining = 1），再对余下全体 -1。
            // 终态语义与“先全体 -1、再删除 ≤0”完全一致（§12-8）：
            //   - 原本 =1 的行：字面路径减到 0 后被删除；等价路径直接删除；
            //   - 原本 >1 的行：两者都变为 -1 后的值；
            //   - 抽中者随后被 UPSERT 置满 N（而非 N-1），验收 7 的语义不变。
            using (var del = conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM cooldown WHERE remaining <= 1";
                del.ExecuteNonQuery();
            }

            // ① 对余下的冷却行全体 remaining - 1
            using (var dec = conn.CreateCommand())
            {
                dec.Transaction = tx;
                dec.CommandText = "UPDATE cooldown SET remaining = remaining - 1";
                dec.ExecuteNonQuery();
            }

            // ③ 抽中者：draw_count +1、置 remaining = N（结算后恰好等于 N，而非 N-1）
            foreach (var c in drawn)
            {
                using var stat = conn.CreateCommand();
                stat.Transaction = tx;
                stat.CommandText = """
                    INSERT INTO draw_stats (name_id, draw_count, last_drawn_at) VALUES (@id, 1, @now)
                    ON CONFLICT(name_id) DO UPDATE SET
                        draw_count    = draw_count + 1,
                        last_drawn_at = excluded.last_drawn_at
                    """;
                stat.Parameters.AddWithValue("@id", c.Id);
                stat.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                stat.ExecuteNonQuery();

                using var cool = conn.CreateCommand();
                cool.Transaction = tx;
                cool.CommandText = """
                    INSERT INTO cooldown (name_id, remaining) VALUES (@id, @n)
                    ON CONFLICT(name_id) DO UPDATE SET remaining = excluded.remaining
                    """;
                cool.Parameters.AddWithValue("@id", c.Id);
                cool.Parameters.AddWithValue("@n", cooldownTimes);
                cool.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        });

    /// <summary>统计面板：LEFT JOIN，只显示 cnt &gt; 0，按次数降序、姓名升序。</summary>
    public Task<IReadOnlyList<StatsRow>> GetStatsAsync() =>
        _factory.RunAsync<IReadOnlyList<StatsRow>>(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT n.name, COALESCE(s.draw_count, 0) AS cnt
                FROM names n LEFT JOIN draw_stats s ON s.name_id = n.id
                WHERE COALESCE(s.draw_count, 0) > 0
                ORDER BY cnt DESC, n.name ASC
                """;
            using var r = cmd.ExecuteReader();
            var list = new List<StatsRow>();
            while (r.Read())
            {
                list.Add(new StatsRow(r.GetString(0), r.GetInt32(1)));
            }

            return list;
        });

    public Task<long> GetTotalDrawCountAsync() =>
        _factory.RunAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(draw_count), 0) FROM draw_stats";
            return Convert.ToInt64(cmd.ExecuteScalar());
        });

    /// <summary>F14 历史流水：一批抽选共用一个 batch_id。</summary>
    public Task WriteHistoryAsync(IReadOnlyList<Candidate> drawn, string batchId, string mode) =>
        _factory.RunInWriteLockAsync(conn =>
        {
            using var tx = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO draw_history (batch_id, drawn_at, name_id, mode)
                VALUES (@batch, @at, @id, @mode)
                """;
            cmd.Parameters.AddWithValue("@batch", batchId);
            cmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@mode", mode);
            var pId = cmd.Parameters.Add("@id", SqliteType.Integer);
            foreach (var c in drawn)
            {
                pId.Value = c.Id;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        });
}
