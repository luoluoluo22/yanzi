using System.Text;

namespace Yanzi.Shared;

/// <summary>
/// 高性能中文拼音首字母与全拼检索匹配助手
/// </summary>
public static class PinyinHelper
{
    private static readonly Dictionary<char, string> CustomPinyinDict = new()
    {
        { '微', "wei" }, { '信', "xin" }, { '终', "zhong" }, { '端', "duan" },
        { '访', "fang" }, { '达', "da" }, { '备', "bei" }, { '忘', "wang" }, { '录', "lu" },
        { '截', "jie" }, { '图', "tu" }, { '屏', "ping" }, { '幕', "mu" },
        { '音', "yin" }, { '乐', "yue" }, { '设', "she" }, { '置', "zhi" },
        { '偏', "pian" }, { '好', "hao" }, { '废', "fei" }, { '纸', "zhi" }, { '篓', "lou" },
        { '剪', "jian" }, { '切', "qie" }, { '粘', "zhan" }, { '贴', "tie" },
        { '复', "fu" }, { '制', "zhi" }, { '全', "quan" }, { '选', "xuan" },
        { '苹', "ping" }, { '果', "guo" }, { '邮', "you" }, { '箱', "xiang" }, { '件', "jian" },
        { '腾', "teng" }, { '讯', "xun" }, { '网', "wang" }, { '易', "yi" }, { '云', "yun" },
        { '百', "bai" }, { '度', "du" }, { '阿', "a" }, { '里', "li" }, { '谷', "gu" }, { '歌', "ge" },
        { '飞', "fei" }, { '书', "shu" }, { '钉', "ding" }, { '哔', "bi" }, { '哩', "li" },
        { '迅', "xun" }, { '雷', "lei" }, { '播', "bo" }, { '放', "fang" }, { '器', "qi" },
        { '浏', "liu" }, { '览', "lan" }, { '计', "ji" }, { '算', "suan" },
        { '日', "ri" }, { '历', "li" }, { '照', "zhao" }, { '片', "pian" },
        { '地', "di" }, { '息', "xi" }, { '锁', "suo" }, { '定', "ding" },
        { '钥', "yao" }, { '匙', "shi" }, { '串', "chuan" }, { '监', "jian" }, { '视', "shi" },
        { '磁', "ci" }, { '盘', "pan" }, { '工', "gong" }, { '具', "ju" },
        { '控', "kong" }, { '台', "tai" }, { '下', "xia" }, { '载', "zai" },
        { '文', "wen" }, { '稿', "gao" }, { '小', "xiao" }, { '程', "cheng" }, { '序', "xu" },
        { '仓', "cang" }, { '库', "ku" }, { '燕', "yan" }, { '子', "zi" }
    };

    /// <summary>
    /// 获取单个中文字符的拼音首字母（若为英文字符或数字则返回小写本身）
    /// </summary>
    public static char GetCharInitial(char ch)
    {
        if (ch >= 'a' && ch <= 'z') return ch;
        if (ch >= 'A' && ch <= 'Z') return char.ToLowerInvariant(ch);
        if (ch >= '0' && ch <= '9') return ch;

        if (CustomPinyinDict.TryGetValue(ch, out var pinyin) && pinyin.Length > 0)
        {
            return pinyin[0];
        }

        // Standard GB2312 Pinyin boundary algorithm
        try
        {
            var gbBytes = Encoding.GetEncoding("GB2312").GetBytes(new[] { ch });
            if (gbBytes.Length == 2)
            {
                int code = (gbBytes[0] << 8) + gbBytes[1];
                if (code >= 0xB0A1 && code <= 0xB0C4) return 'a';
                if (code >= 0xB0C5 && code <= 0xB2C0) return 'b';
                if (code >= 0xB2C1 && code <= 0xB4ED) return 'c';
                if (code >= 0xB4EE && code <= 0xB6E9) return 'd';
                if (code >= 0xB6EA && code <= 0xB7A1) return 'e';
                if (code >= 0xB7A2 && code <= 0xB8C0) return 'f';
                if (code >= 0xB8C1 && code <= 0xB9FD) return 'g';
                if (code >= 0xB9FE && code <= 0xBBF6) return 'h';
                if (code >= 0xBBF7 && code <= 0xBFA5) return 'j';
                if (code >= 0xBFA6 && code <= 0xC0AB) return 'k';
                if (code >= 0xC0AC && code <= 0xC2E7) return 'l';
                if (code >= 0xC2E8 && code <= 0xC4C2) return 'm';
                if (code >= 0xC4C3 && code <= 0xC5B5) return 'n';
                if (code >= 0xC5B6 && code <= 0xC5BD) return 'o';
                if (code >= 0xC5BE && code <= 0xC6D9) return 'p';
                if (code >= 0xC6DA && code <= 0xC8BA) return 'q';
                if (code >= 0xC8BB && code <= 0xC8F5) return 'r';
                if (code >= 0xC8F6 && code <= 0xCBF9) return 's';
                if (code >= 0xCBFA && code <= 0xCDD9) return 't';
                if (code >= 0xCDDA && code <= 0xCEF3) return 'w';
                if (code >= 0xCEF4 && code <= 0xD1B8) return 'x';
                if (code >= 0xD1B9 && code <= 0xD4D0) return 'y';
                if (code >= 0xD4D1 && code <= 0xD7F9) return 'z';
            }
        }
        catch
        {
            // Encoding fallback
        }

        return '\0';
    }

    /// <summary>
    /// 获取字符串的拼音首字母缩写（例如 "微信" -> "wx", "终端" -> "zd"）
    /// </summary>
    public static string GetPinyinInitials(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            var initial = GetCharInitial(ch);
            if (initial != '\0')
                sb.Append(initial);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 检查 text 是否匹配用户输入的 query（支持中文字符、原文字符串包含、拼音首字母匹配）
    /// </summary>
    public static bool Matches(string? text, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (string.IsNullOrWhiteSpace(text)) return false;

        query = query.Trim();

        // 1. 原文包含（忽略大小写）
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        // 2. 拼音首字母包含匹配 (如 "wx" -> "微信")
        var initials = GetPinyinInitials(text);
        if (!string.IsNullOrEmpty(initials) && initials.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
