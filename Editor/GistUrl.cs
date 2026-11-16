using System;

namespace MUD.Modules.GistManager
{
    /// <summary>
    /// 解析 Gist 链接：
    ///   https://gist.github.com/{author}/{id}   → (author, id)
    ///   https://gist.github.com/{id}            → (anonymous, id)
    /// 也接受省略协议的完整地址、裸的 author/id 与裸 id。
    /// </summary>
    public static class GistUrlParser
    {
        public const string AnonymousAuthor = "anonymous";

        public static bool IsAnonymous(string author)
        {
            return string.IsNullOrEmpty(author)
                   || string.Equals(author, AnonymousAuthor, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>条目在浏览器里打开的真实链接：匿名 Gist 没有作者段。</summary>
        public static string GistWebUrl(string author, string id)
        {
            return IsAnonymous(author)
                ? "https://gist.github.com/" + id
                : "https://gist.github.com/" + author + "/" + id;
        }

        public static bool TryParse(string input, out string author, out string id, out string error)
        {
            author = string.Empty;
            id = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                error = GistLoc.T("请先粘贴 Gist 链接。", "Paste a Gist URL first.");
                return false;
            }

            var text = input.Trim();

            if (text.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                text = text.StartsWith("gist.github.com", StringComparison.OrdinalIgnoreCase)
                    ? "https://" + text
                    : "https://gist.github.com/" + text.TrimStart('/');
            }

            Uri? uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri))
            {
                error = GistLoc.T("'" + input + "' 不是有效的 URL。", "'" + input + "' is not a valid URL.");
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                error = GistLoc.T("'" + input + "' 不是 http(s) URL。", "'" + input + "' is not an http(s) URL.");
                return false;
            }

            var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                error = GistLoc.T("'" + input + "' 中未找到 Gist id。", "No Gist id found in '" + input + "'.");
                return false;
            }

            // /author/id[/...] → author + id；/id[/...] → 匿名
            var rawId = segments.Length >= 2 ? segments[1] : segments[0];
            author = segments.Length >= 2 ? segments[0] : AnonymousAuthor;

            var dot = rawId.IndexOf('.');
            if (dot > 0)
                rawId = rawId.Substring(0, dot);

            id = rawId.Trim();
            if (!IsGistId(id))
            {
                error = GistLoc.T("'" + input + "' 不像 Gist 链接（应为 gist.github.com/author/id）。",
                                  "'" + input + "' does not look like a Gist URL (expected gist.github.com/author/id).");
                return false;
            }

            return true;
        }

        private static bool IsGistId(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (var c in value)
            {
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-';
                if (!ok)
                    return false;
            }

            return true;
        }
    }
}