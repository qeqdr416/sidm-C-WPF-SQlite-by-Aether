using RollCall.Core.Models;

namespace RollCall.Core.Algorithms;

/// <summary>姓名显示排版（§A5.2）：每行 4 个；超过 8 字的姓名字符间插零宽空格防折行。</summary>
public static class NameFormatter
{
    public const int NamesPerLine = AppConstants.NamesPerLine;
    private const char ZeroWidthSpace = '\u200B';

    public static string Format(IEnumerable<string> names)
    {
        var lines = new List<string>();
        var group = new List<string>();

        foreach (var raw in names)
        {
            group.Add(Spread(raw));
            if (group.Count == NamesPerLine)
            {
                lines.Add(string.Join(' ', group));
                group.Clear();
            }
        }

        if (group.Count > 0)
        {
            lines.Add(string.Join(' ', group));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>长度 &gt; 8 时在每个字符之间插入 U+200B（零宽空格），显示时不会把一个姓名折成多行。</summary>
    public static string Spread(string name)
    {
        if (name.Length <= 8)
        {
            return name;
        }

        var sb = new System.Text.StringBuilder(name.Length * 2);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(ZeroWidthSpace);
            }

            sb.Append(name[i]);
        }

        return sb.ToString();
    }
}
