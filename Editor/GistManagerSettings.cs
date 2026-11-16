using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MUD.Modules.GistManager
{
    /// <summary>
    /// Gist Manager 项目级设置。唯一配置，存放于 ProjectSettings/GistManager.asset，
    /// 由 Unity 的 ScriptableSingleton 管理，可提交进版本库。
    /// 非敏感项目配置（界面语言、折叠状态、上游检查、最近一次 Sync）入库共享；
    /// GitHub Token 仍然存在 EditorPrefs（本机，不入版本库）。
    /// </summary>
    [FilePath("ProjectSettings/GistManager.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class GistManagerSettings : ScriptableSingleton<GistManagerSettings>
    {
        public const string ProjectSettingsPath = "ProjectSettings/GistManager.asset";

        public const string LanguageAuto = "auto";
        public const string LanguageZh = "zh";
        public const string LanguageEn = "en";

        /// <summary>界面语言：auto（跟随系统）/ zh / en。</summary>
        public string language = LanguageAuto;

        /// <summary>同步时是否检查上游更新（需要一次 GitHub API 调用/条目）。</summary>
        public bool checkUpstreamOnSync = true;

        /// <summary>上次 Sync 的本地时间（yyyy-MM-dd HH:mm:ss），空 = 从未同步。</summary>
        public string lastSyncUtc = string.Empty;

        /// <summary>左栏折叠的作者分组（JsonUtility 兼容的 List&lt;string&gt;）。</summary>
        public List<string> collapsedAuthors = new List<string>();

        /// <summary>
        /// 清单 `gists.json` 相对工程的位置（如 `Packages/gist-scripts/gists.json`）。
        /// 切换 Target Root 时清单随之移动，此处记录新位置；空 / 缺失 = 老项目，回落固定默认位置。
        /// </summary>
        public string manifestRelativePath = string.Empty;

        /// <summary>把当前设置写入 ProjectSettings/GistManager.asset（文本格式，便于提交版本库）。</summary>
        public void Save()
        {
            Save(true);
        }

        private void OnEnable()
        {
            if (collapsedAuthors == null)
                collapsedAuthors = new List<string>();

            if (string.IsNullOrEmpty(language))
                language = LanguageAuto;

            MigrateLegacyEditorPrefs();
        }

        /// <summary>一次性迁移：把 0.2.0 引入 Language/上游检查/折叠状态/最近同步的 EditorPrefs 值转进资产。</summary>
        private void MigrateLegacyEditorPrefs()
        {
            var migrated = false;

            if (EditorPrefs.HasKey(LegacyLanguageKey))
            {
                var raw = EditorPrefs.GetString(LegacyLanguageKey, LanguageAuto);
                if (!string.IsNullOrEmpty(raw))
                    language = raw;
                EditorPrefs.DeleteKey(LegacyLanguageKey);
                migrated = true;
            }

            if (EditorPrefs.HasKey(LegacyCheckUpstreamKey))
            {
                checkUpstreamOnSync = EditorPrefs.GetBool(LegacyCheckUpstreamKey, true);
                EditorPrefs.DeleteKey(LegacyCheckUpstreamKey);
                migrated = true;
            }

            if (EditorPrefs.HasKey(LegacyLastSyncKey))
            {
                var raw = EditorPrefs.GetString(LegacyLastSyncKey, string.Empty);
                if (!string.IsNullOrEmpty(raw))
                    lastSyncUtc = raw;
                EditorPrefs.DeleteKey(LegacyLastSyncKey);
                migrated = true;
            }

            if (EditorPrefs.HasKey(LegacyCollapsedKey))
            {
                var json = EditorPrefs.GetString(LegacyCollapsedKey, string.Empty);
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var state = JsonUtility.FromJson<FoldoutStateMirror>(json);
                        if (state != null && state.collapsed != null && state.collapsed.Count > 0)
                        {
                            collapsedAuthors = new List<string>(state.collapsed);
                            migrated = true;
                        }
                    }
                    catch (System.Exception)
                    {
                        // 旧值损坏时忽略，不中断加载
                    }
                }
                EditorPrefs.DeleteKey(LegacyCollapsedKey);
                migrated = true;
            }

            if (migrated)
                Save();
        }

        private const string LegacyLanguageKey = "GistScriptManager.Language";
        private const string LegacyCheckUpstreamKey = "GistScriptManager.CheckUpstreamOnSync";
        private const string LegacyLastSyncKey = "GistScriptManager.LastSyncUtc";
        private const string LegacyCollapsedKey = "GistScriptManager.CollapsedAuthors";

        [System.Serializable]
        private sealed class FoldoutStateMirror
        {
            public List<string> collapsed = new List<string>();
        }
    }
}
