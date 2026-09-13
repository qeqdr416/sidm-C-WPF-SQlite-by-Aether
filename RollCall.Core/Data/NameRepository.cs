using Microsoft.Data.Sqlite;
using RollCall.Core.Abstractions;
using RollCall.Core.Models;

namespace RollCall.Core.Data;

/// <summary>名单仓储（§A4.5）。改名单一律 is_active 翻转，绝不物理删除（§12-9）。</summary>
public sealed class NameRepository : INameRepository
{
    private readonly SqliteConnectionFactory _factory;

    public NameRepository(SqliteConnectionFactory factory) => _factory = factory;

    public Task<IReadOnlyList<Candidate>> GetCandidatesOutsideCooldownAsync(int take) =>
        _factory.RunAsync<IReadOnlyList<Candidate>>(conn => QueryList(conn,
            """
            SELECT id, name FROM names
            WHERE is_active = 1
              AND NOT EXISTS (SELECT 1 FROM cooldown c WHERE c.name_id = names.id)
            ORDER BY RANDOM() LIMIT @take
            """, take));

    public Task<IReadOnlyList<Candidate>> GetActiveCandidatesAsync(int take) =>
        _factory.RunAsync<IReadOnlyList<Candidate>>(conn => QueryList(conn,
            "SELECT id, name FROM names WHERE is_active = 1 ORDER BY RANDOM() LIMIT @take", take));

    public Task<IReadOnlyList<Candidate>> GetAllActiveAsync() =>
        _factory.RunAsync<IReadOnlyList<Candidate>>(conn => QueryList(conn,
            "SELECT id, name FROM names WHERE is_active = 1 ORDER BY sort_order"));

    public Task<IReadOnlyList<Candidate>> GetActiveOrderedListAsync() =>
        _factory.RunAsync<IReadOnlyList<Candidate>>(conn => QueryList(conn,
            "SELECT id, name FROM names WHERE is_active = 1 ORDER BY sort_order"));

    public Task<int> CountActiveAsync() =>
        _factory.RunAsync(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM names WHERE is_active = 1";
            return Convert.ToInt32(cmd.ExecuteScalar());
        });

    /// <summary>替换名单（导入 / 恢复默认）：单事务内旧行全部 is_active=0、清空冷却池，再逐个 UPSERT。</summary>
    public Task<int> ReplaceAllAsync(IReadOnlyList<string> names, string source) =>
        _factory.RunInWriteLockAsync(conn =>
        {
            using var tx = conn.BeginTransaction();

            using (var deactivate = conn.CreateCommand())
            {
                deactivate.Transaction = tx;
                deactivate.CommandText = "UPDATE names SET is_active = 0";
                deactivate.ExecuteNonQuery();
            }

            // 冷却池随名单一起失效（§A4.5）
            using (var clear = conn.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "DELETE FROM cooldown";
                clear.ExecuteNonQuery();
            }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO names (name, sort_order, source, is_active)
                VALUES (@name, @order, @source, 1)
                ON CONFLICT(name) DO UPDATE SET
                    sort_order = excluded.sort_order,
                    source     = excluded.source,
                    is_active  = 1
                """;
            var pName = cmd.Parameters.Add("@name", SqliteType.Text);
            var pOrder = cmd.Parameters.Add("@order", SqliteType.Integer);
            var pSource = cmd.Parameters.Add("@source", SqliteType.Text);
            for (var i = 0; i < names.Count; i++)
            {
                pName.Value = names[i];
                pOrder.Value = i;
                pSource.Value = source;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return names.Count;
        });

    /// <summary>手动追加：保持顺序、忽略已存在；返回实际新增数。</summary>
    public Task<int> AddNamesAsync(IReadOnlyList<string> names) =>
        _factory.RunInWriteLockAsync(conn =>
        {
            using var tx = conn.BeginTransaction();

            int baseOrder;
            using (var max = conn.CreateCommand())
            {
                max.Transaction = tx;
                max.CommandText = "SELECT COALESCE(MAX(sort_order), -1) FROM names";
                baseOrder = Convert.ToInt32(max.ExecuteScalar()) + 1;
            }

            var added = 0;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO names (name, sort_order, source, is_active)
                VALUES (@name, @order, 'manual', 1)
                ON CONFLICT(name) DO NOTHING
                """;
            var pName = cmd.Parameters.Add("@name", SqliteType.Text);
            var pOrder = cmd.Parameters.Add("@order", SqliteType.Integer);
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i].Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                pName.Value = name;
                pOrder.Value = baseOrder + i;
                added += cmd.ExecuteNonQuery();   // ON CONFLICT DO NOTHING 时返回 0
            }

            tx.Commit();
            return added;
        });

    private static List<Candidate> QueryList(SqliteConnection conn, string sql, int? take = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (take.HasValue)
        {
            cmd.Parameters.AddWithValue("@take", take.Value);
        }

        using var r = cmd.ExecuteReader();
        var list = new List<Candidate>();
        while (r.Read())
        {
            list.Add(new Candidate(r.GetInt32(0), r.GetString(1)));
        }

        return list;
    }
}
