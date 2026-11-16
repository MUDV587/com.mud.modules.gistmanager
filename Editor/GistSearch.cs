using System;
using System.Linq;

namespace MUD.Modules.GistManager
{
    /// <summary>
    /// 左栏搜索过滤与空状态文案。
    /// 与窗口（UI Toolkit）解耦：harness 在无 Unity UI 环境下直接测试本类；
    /// 过滤语义与文案对齐编辑器仿生搜索组件（按作者/gist id/描述/文件名/路径匹配）。
    /// </summary>
    public static class GistSearch
    {
        /// <summary>空状态里搜索词的最大显示长度，超出截断为 "...''（官方 ListArea.k_MaxSearchTextToDisplay 同款）。</summary>
        public const int MaxDisplayQueryLength = 64;

        /// <summary>命中作者、Gist ID、描述或任一文件名/路径（忽略大小写）。</summary>
        public static bool Matches(GistEntry? entry, string? query)
        {
            if (entry == null)
                return false;

            // 不依赖 IsNullOrEmpty 的可空性流分析（Unity 2022.3 的引用程序集没带注解）
            if (query == null)
                return true;

            var needle = query.Trim();
            if (needle.Length == 0)
                return true;

            return ContainsIgnoreCase(entry.author, needle)
                   || ContainsIgnoreCase(entry.id, needle)
                   || ContainsIgnoreCase(entry.description, needle)
                   || entry.files.Any(f => ContainsIgnoreCase(f.filename, needle) || ContainsIgnoreCase(f.targetPath, needle));
        }

        /// <summary>
        /// 列表空状态文案（官方 ListArea.UpdateListVisibility 同款语义）：
        /// 无搜索词 → "No items to display."；有搜索词 → No results for "…&lt;截断&gt;"。
        /// </summary>
        public static string NoResultsMessage(string? query)
        {
            var trimmed = (query ?? string.Empty).Trim();
            if (trimmed.Length == 0)
                return GistLoc.T("没有可显示的条目。", "No items to display.");

            return GistLoc.T("没有找到 \"" + Truncate(trimmed) + "\"", "No results for \"" + Truncate(trimmed) + "\"");
        }

        /// <summary>按 MaxDisplayQueryLength 截断搜索词（超长时以 "..." 结尾）。</summary>
        public static string Truncate(string query)
        {
            var value = query ?? string.Empty;
            return value.Length <= MaxDisplayQueryLength
                ? value
                : value.Substring(0, MaxDisplayQueryLength) + "...";
        }

        private static bool ContainsIgnoreCase(string? value, string needle)
        {
            return value != null && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
