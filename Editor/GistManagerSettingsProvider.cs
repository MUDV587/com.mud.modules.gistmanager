using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MUD.Modules.GistManager
{
    /// <summary>
    /// Project Settings 页面（Project/MUD/Gist Manager）：
    /// 把原 GistSettingsWindow 弹窗里的“界面语言 / Target Root / 清单 / GitHub Token / 上游检查”集中到这里。
    /// 参照 MUD/Menu Redirect：Tools 菜单直接打开该页面，不再需要独立的 Utility 弹窗。
    /// </summary>
    internal sealed class GistManagerSettingsProvider : SettingsProvider
    {
        internal const string SettingsPath = "Project/MUD/Gist Manager";
        private static readonly string[] k_TargetRootOptions = { "Packages/gist-scripts", "Assets/GistScripts" };

        private GistManifest? _manifest;
        private string? _loadError;
        private DateTime _manifestWriteTimeUtc = DateTime.MinValue;

        private GistManagerSettingsProvider(string path, SettingsScope scope)
            : base(path, scope)
        {
        }

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new GistManagerSettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Gist Manager",
                keywords = new[] { "gist", "gistmanager", "github", "script", "snippet", "Gist管理器" }
            };
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            base.OnActivate(searchContext, rootElement);
            ReloadManifest();
        }

        public override void OnDeactivate()
        {
            _manifest = null;
            base.OnDeactivate();
        }


        public override void OnGUI(string searchContext)
        {
            // 外部修改 gists.json 后自动读回（与主窗口 OnFocus 行为一致）
            if (ChangeWriteStamp())
                ReloadManifest();

            EditorGUILayout.LabelField("Gist Manager Settings", EditorStyles.boldLabel);
            GUILayout.Space(8);

            if (!string.IsNullOrEmpty(_loadError))
            {
                EditorGUILayout.HelpBox(
                    _loadError + "\nFix or delete " + GistManifestStore.ManifestPath + ", and the page will reload.",
                    MessageType.Error);
                return;
            }

            DrawLanguage();
            GUILayout.Space(8);

            if (_manifest != null)
                DrawTargetRoot(_manifest);

            GUILayout.Space(8);
            DrawManifestRow();
            GUILayout.Space(8);
            DrawToken();
            GUILayout.Space(8);
            DrawUpstreamCheck();

            GUILayout.FlexibleSpace();
            GUILayout.Label(
                GistLoc.T("项目级设置（不含 Token）存于 ProjectSettings/GistManager.asset；Token 存 EditorPrefs（本机）。",
                          "Project-level settings (except Token) live in ProjectSettings/GistManager.asset; the Token lives in EditorPrefs (machine-local)."),
                EditorStyles.miniLabel);
        }

        private void DrawLanguage()
        {
            var current = GistLoc.GetLanguage();
            var options = new[]
            {
                GistLoc.T("跟随系统（自动）", "System (automatic)"),
                "中文",
                "English"
            };

            var picked = EditorGUILayout.Popup(
                GistLoc.T("界面语言", "Interface Language"),
                (int)current,
                options);
            if (picked == (int)current)
                return;

            GistLoc.SetLanguage((GistLoc.GistLanguage)picked);
            RebuildOpenWindows();
        }

        private void DrawTargetRoot(GistManifest manifest)
        {
            var currentRoot = manifest.NormalizedTargetRoot;
            var options = new List<string>(k_TargetRootOptions);
            var index = options.IndexOf(currentRoot);
            if (index < 0)
            {
                options.Add(currentRoot);
                index = options.Count - 1;
            }

            // Unity 的 Popup/GenericMenu 把选项文本里的 “/” 识别为子菜单分隔符——
            // 显示层换成 “\”（一级），内部仍以 “/” 存储，行为不变。
            var displayOptions = options.Select(o => o.Replace('/', '\\')).ToArray();
            var picked = EditorGUILayout.Popup(
                GistLoc.T("目标根目录", "Target Root"),
                index,
                displayOptions);
            if (picked < 0)
                return;

            var newRoot = options[picked];
            if (string.Equals(newRoot, currentRoot, StringComparison.OrdinalIgnoreCase))
                return;

            var choice = EditorUtility.DisplayDialogComplex(
                GistLoc.T("切换 Target Root", "Switch Target Root"),
                GistLoc.T(
                    "目标根目录将从 " + currentRoot + " 切换为 " + newRoot + "。\n\n" +
                    "· 切换并移动：清单（gists.json）、嵌入式包标记（package.json）、全部 gist 脚本及 .meta 一起搬到新位置；旧根清空后整体删除。\n" +
                    "· 仅切换（不移动）：只改清单里的 targetRoot，旧文件原地不动——之后 Sync 会按新根重新下载，新旧两边同名 gist 会产生同名 asmdef，导致工程无法编译！\n" +
                    "· 取消：不做任何改动。\n\n" +
                    "提示：目标位置已存在同名文件 / 非空 gist 目录时，将直接报错中止，不做任何改动。",
                    "Switch from " + currentRoot + " to " + newRoot + ".\n\n" +
                    "- Switch and Move: the manifest (gists.json), the embedded-package marker (package.json) and every gist script + .meta move to the new location; the emptied old root is deleted.\n" +
                    "- Switch Only (no move): only the manifest's targetRoot changes; files stay in place — the next sync re-downloads into the new root and leaves the old copies behind, producing duplicate asmdefs and breaking compilation!\n" +
                    "- Cancel: no changes.\n\n" +
                    "Note: if the destination already has same-named files or non-empty gist folders, the switch aborts with an error and makes NO changes."),
                GistLoc.T("切换并移动", "Switch and Move"),
                GistLoc.T("取消", "Cancel"),
                GistLoc.T("仅切换（不移动）", "Switch Only"));

            if (choice == 0)
            {
                RelocateAndApply(manifest, newRoot);
                return;
            }

            if (choice == 2)
            {
                // 仅切换：只改清单，磁盘原地不动（见上方警告）
                GistCatalog.Retarget(manifest, newRoot);
                SaveManifest();
            }
            // choice == 1（取消）：不做任何改动；下拉在下一帧自动回弹到旧值
        }

        /// <summary>与 GistManagerWindow.RefreshAssets 同款，但 provider 是独立的 IMGUI 宿主；延迟一帧做资产导入。</summary>
        private static void DelayedRefreshAssets()
        {
            UnityEditor.AssetDatabase.Refresh();
        }

        /// <summary>执行「切换并移动」：搬运 → 设置资产记入新清单路径 → 原子保存；失败时什么都没留下（已回滚）。</summary>
        private void RelocateAndApply(GistManifest manifest, string newRoot)
        {
            var outcome = GistRelocateService.Relocate(manifest, newRoot);

            switch (outcome.Status)
            {
                case GistRelocateService.RelocateStatus.Success:
                    try
                    {
                        // 清单已经搬去新根：设置资产先记入新位置，ManifestPath 才会指向它
                        GistManagerSettings.instance.manifestRelativePath =
                            manifest.NormalizedTargetRoot + "/" + GistManifestStore.ManifestFileName;
                        GistManagerSettings.instance.Save();
                        SaveManifest();
                        UnityEditor.AssetDatabase.Refresh();
                        // 公开 API：强制包管理器以当前磁盘为准重解析——嵌入式包在两个根之间搬迁时调一次，
                        // 否则 packages-lock.json 仍记录旧路径，UPM 不知道 com.gistscripts.gists 回来了。
                        // 本地即可，无外部网络请求。
                        try { UnityEditor.PackageManager.Client.Resolve(); } catch (Exception) { }

                        // 域重载/编译稳定后再导入一次（新嵌入式包在那时对包发现器可见）
                        EditorApplication.delayCall += DelayedRefreshAssets;

                        var message = GistLoc.T(
                            "搬运完成：已移动 " + outcome.MovedFileCount + " 个文件到 " + newRoot + "。",
                            "Relocated: moved " + outcome.MovedFileCount + " file(s) to " + newRoot + ".");
                        if (outcome.Warnings.Count > 0)
                            message += "\n\n" + string.Join("\n", outcome.Warnings);
                        EditorUtility.DisplayDialog(
                            GistLoc.T("Target Root 已切换", "Target Root switched"),
                            message, "OK");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                        EditorUtility.DisplayDialog(
                            GistLoc.T("搬运后保存失败", "Post-relocate save failed"),
                            GistLoc.T("文件系统已搬迁，但清单写盘失败：" + ex.Message + "\n文件本身已搬到新根，请检查 ProjectSettings/GistManager.asset 与 " + newRoot + "/gists.json。",
                                      "The file system was moved but saving the manifest failed: " + ex.Message + "\nThe files are at the new root; check ProjectSettings/GistManager.asset and " + newRoot + "/gists.json."),
                            "OK");
                    }
                    ReloadManifest();
                    return;

                case GistRelocateService.RelocateStatus.InvalidRoot:
                    EditorUtility.DisplayDialog(
                        GistLoc.T("无法切换", "Cannot switch"),
                        string.Join("\n", outcome.Blockers), "OK");
                    return;

                case GistRelocateService.RelocateStatus.Blocked:
                    EditorUtility.DisplayDialog(
                        GistLoc.T("无法切换：目标位置已被占用", "Cannot switch: destination occupied"),
                        string.Join("\n", outcome.Blockers) +
                        "\n\n" + GistLoc.T("为避免覆盖已有文件，本次切换未做任何改动。", "To avoid overwriting existing files, nothing was changed."),
                        "OK");
                    return;

                case GistRelocateService.RelocateStatus.IoError:
                    EditorUtility.DisplayDialog(
                        GistLoc.T("搬运失败（已回滚）", "Relocation failed (rolled back)"),
                        GistLoc.T("错误：", "Error: ") + (outcome.Error ?? string.Empty) +
                        "\n" + GistLoc.T("所有文件已回到切换前的位置，清单未改动。", "All files were moved back to their original locations; the manifest was not changed."),
                        "OK");
                    return;
            }
        }

        private void DrawManifestRow()
        {
            EditorGUILayout.LabelField(GistLoc.T("清单", "Manifest"), EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(
                GistManifestStore.ManifestPath,
                EditorStyles.miniLabel,
                GUILayout.Height(18));
        }

        private void DrawToken()
        {
            var token = GistTokenStore.Token;
            var newToken = EditorGUILayout.PasswordField(
                GistLoc.T("GitHub Token", "GitHub Token"),
                token);
            if (!string.Equals(newToken, token, StringComparison.Ordinal))
                GistTokenStore.Token = newToken;

            GUILayout.Label(
                GistLoc.T("限流：", "Rate limit: ") + GistTokenStore.RateLimitDescription,
                EditorStyles.miniLabel);
        }

        private void DrawUpstreamCheck()
        {
            var check = GistSyncService.CheckUpstreamOnSync;
            var newCheck = EditorGUILayout.Toggle(
                GistLoc.T("同步时检查上游更新", "Check upstream on sync"),
                check);
            if (newCheck != check)
                GistSyncService.CheckUpstreamOnSync = newCheck;

            GUILayout.Label(
                GistLoc.T("注意：每条目同步多调用 1 次 API；匿名限流 60 次/小时（配置 Token 后 5,000 次/小时）",
                          "Note: 1 extra API call per entry on sync; 60/hr anonymous (5,000 with a token)"),
                EditorStyles.miniLabel);
        }

        private static void RebuildOpenWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<GistManagerWindow>())
            {
                if (window != null)
                    window.CreateGUI();
            }
        }

        private bool ChangeWriteStamp()
        {
            var stamp = GistManifestStore.ManifestWriteTimeUtc;
            if (stamp == _manifestWriteTimeUtc)
                return false;
            _manifestWriteTimeUtc = stamp;
            return true;
        }

        private void ReloadManifest()
        {
            string? error;
            _manifest = GistManifestStore.LoadOrCreate(out error);
            _loadError = error;
            _manifestWriteTimeUtc = GistManifestStore.ManifestWriteTimeUtc;

            if (string.IsNullOrEmpty(error) && GistCatalog.Normalize(_manifest))
                SaveManifest();
        }

        private void SaveManifest()
        {
            if (_manifest == null)
                return;

            try
            {
                GistManifestStore.Save(_manifest);
                _manifestWriteTimeUtc = GistManifestStore.ManifestWriteTimeUtc;
                _loadError = null;
            }
            catch (Exception ex)
            {
                _loadError = "Failed to write gists.json: " + ex.Message;
                Debug.LogException(ex);
            }
        }
    }
}
