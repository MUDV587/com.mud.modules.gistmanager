using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace MUD.Modules.GistManager
{
    public class GistApiException : Exception
    {
        public long StatusCode { get; private set; }

        public GistApiException(long statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }
    }

    public class GistFileMetadata
    {
        public string Filename = string.Empty;
        public string RawUrl = string.Empty;
        public string Language = string.Empty;
    }

    public class GistMetadata
    {
        public string Id = string.Empty;
        public string Author = string.Empty;
        public string Description = string.Empty;
        public List<GistFileMetadata> Files = new List<GistFileMetadata>();
    }

    /// <summary>
    /// GitHub Token 存在 EditorPrefs（本机），不写入 gists.json，避免随清单提交到仓库。
    /// 未认证限额 60 次/小时，认证后 5000 次/小时。
    /// </summary>
    public static class GistTokenStore
    {
        /// <summary>主键（0.3.0+）；旧键保留作迁移入口。</summary>
        private const string PrefsKey = "GistManager.GitHubToken";
        private const string LegacyPrefsKey = "GistScriptManager.GitHubToken";

        static GistTokenStore()
        {
            // 一次性迁移：旧键内容搬到新键，然后删旧键——避免同一台机器上的工程中残留两个 token 副本
            try
            {
                if (UnityEditor.EditorPrefs.HasKey(LegacyPrefsKey) && !UnityEditor.EditorPrefs.HasKey(PrefsKey))
                    UnityEditor.EditorPrefs.SetString(PrefsKey, UnityEditor.EditorPrefs.GetString(LegacyPrefsKey, string.Empty));
                if (UnityEditor.EditorPrefs.HasKey(LegacyPrefsKey))
                    UnityEditor.EditorPrefs.DeleteKey(LegacyPrefsKey);
            }
            catch (Exception)
            {
                // 迁移失败不影响主流程
            }
        }

        public static string Token
        {
            get { return UnityEditor.EditorPrefs.GetString(PrefsKey, string.Empty); }
            set { UnityEditor.EditorPrefs.SetString(PrefsKey, value ?? string.Empty); }
        }

        public static bool HasToken
        {
            get { return !string.IsNullOrEmpty(Token); }
        }

        public static string RateLimitDescription
        {
            get
            {
                return HasToken
                    ? GistLoc.T("5,000 次/小时（已认证）", "5,000 requests/hour (authenticated)")
                    : GistLoc.T("60 次/小时（匿名）", "60 requests/hour (anonymous)");
            }
        }
    }

    /// <summary>
    /// GitHub Gist API：GET /gists/{id} 取元数据（owner、description、files 与 raw_url），
    /// 以及按 raw_url 下载文件内容。
    /// </summary>
    public static class GistApiClient
    {
        /// <summary>API 根地址。默认 GitHub 官方，测试或代理可覆盖。</summary>
        public static string ApiRoot = "https://api.github.com";

        /// <summary>请求超时（秒）：网络黑洞（代理挂死 / 防火墙吞包）时请求必须快速失败，避免 Sync All 永远停在 busy 状态。</summary>
        public const int RequestTimeoutSeconds = 30;

        public static async Task<GistMetadata> FetchGistAsync(string id)
        {
            var text = await GetTextAsync(ApiRoot + "/gists/" + id);
            var root = JObject.Parse(text);

            var metadata = new GistMetadata
            {
                Id = root.Value<string>("id") ?? id,
                Description = root.Value<string>("description") ?? string.Empty,
                Author = ReadOwner(root) ?? GistUrlParser.AnonymousAuthor
            };

            var files = root["files"] as JObject;
            if (files != null)
            {
                foreach (var property in files.Properties())
                {
                    var value = property.Value as JObject;
                    if (value == null)
                        continue;

                    var rawUrl = value.Value<string>("raw_url");
                    if (string.IsNullOrEmpty(rawUrl))
                        continue;

                    metadata.Files.Add(new GistFileMetadata
                    {
                        Filename = value.Value<string>("filename") ?? property.Name,
                        RawUrl = rawUrl ?? string.Empty,
                        Language = value.Value<string>("language") ?? string.Empty
                    });
                }
            }

            if (metadata.Files.Count == 0)
                throw new GistApiException(200, GistLoc.T("Gist " + id + " 不含任何文件。", "Gist " + id + " contains no files."));

            return metadata;
        }

        public static Task<string> DownloadTextAsync(string rawUrl)
        {
            return GetTextAsync(rawUrl);
        }

        private static string? ReadOwner(JObject root)
        {
            var owner = root["owner"];
            if (owner == null || owner.Type != JTokenType.Object)
                return null;

            var login = owner.Value<string>("login");
            return string.IsNullOrEmpty(login) ? null : login;
        }

        private static async Task<string> GetTextAsync(string url)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = RequestTimeoutSeconds;
                request.SetRequestHeader("Accept", "application/vnd.github+json");

                var token = GistTokenStore.Token;
                if (!string.IsNullOrEmpty(token))
                    request.SetRequestHeader("Authorization", "token " + token);

                await SendAsync(request);

                if (request.result != UnityWebRequest.Result.Success)
                    throw new GistApiException(request.responseCode, DescribeFailure(request));

                return request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            }
        }

        /// <summary>
        /// 在编辑器主线程上把 UnityWebRequest 变成可 await 的 Task：
        /// completed 回调在 EditorApplication.update 期间触发，续体回到 Unity 同步上下文。
        /// </summary>
        private static Task SendAsync(UnityWebRequest request)
        {
            var completion = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            operation.completed += _ => completion.TrySetResult(true);
            return completion.Task;
        }

        private static string DescribeFailure(UnityWebRequest request)
        {
            var body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            string? apiMessage = null;

            if (!string.IsNullOrEmpty(body))
            {
                try
                {
                    apiMessage = JObject.Parse(body).Value<string>("message");
                }
                catch (Exception)
                {
                    // 非 JSON 响应体，忽略
                }
            }

            var description = apiMessage ?? request.error ?? GistLoc.T("请求失败", "request failed");

            if (request.responseCode == 403 && body.Length > 0
                && body.IndexOf("rate limit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                description += GistLoc.T(
                    "。未认证请求限制 60 次/小时——在窗口设置里填入 GitHub Token 可提升到 5000。",
                    ". Unauthenticated requests are limited to 60/hour — set a GitHub token in the window settings.");
            }

            // responseCode 为 0 表示请求没拿到 HTTP 响应（超时 / 连接中断），
            // 这时 Unity 的 error 文本（如 "Request timed out"）即是最有用的信息，不加 "HTTP 0" 前缀
            var prefix = request.responseCode > 0 ? "HTTP " + request.responseCode + ": " : string.Empty;
            return prefix + description;
        }
    }
}