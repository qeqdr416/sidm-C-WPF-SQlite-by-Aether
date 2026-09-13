using System.Text.RegularExpressions;

namespace RollCall.Core.Algorithms;

/// <summary>版本比较（§A5.4）：只看数字，"4.10" &gt; "4.9"、"10.0" &gt; "4.0"；空串/非数字 → false（§12-14）。</summary>
public static partial class VersionComparer
{
    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsRegex();

    public static bool IsNewer(string? latest, string? current)
    {
        var a = Extract(latest);
        var b = Extract(current);
        if (a is null || b is null)
        {
            return false;
        }

        var len = Math.Max(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var x = i < a.Length ? a[i] : 0L;   // 位数不足补 0
            var y = i < b.Length ? b[i] : 0L;
            if (x != y)
            {
                return x > y;
            }
        }

        return false;
    }

    private static long[]? Extract(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var matches = DigitsRegex().Matches(version);
        if (matches.Count == 0)
        {
            return null;
        }

        var result = new long[matches.Count];
        for (var i = 0; i < matches.Count; i++)
        {
            if (!long.TryParse(matches[i].Value, out var n))
            {
                return null;
            }

            result[i] = n;
        }

        return result;
    }
}
