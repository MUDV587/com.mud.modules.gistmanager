using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace MUD.Modules.GistManager
{
    [Serializable]
    public class GistAsmdefJson
    {
        public string name = string.Empty;
        public string rootNamespace = string.Empty;
        public string[] references = new string[0];
        public string[] includePlatforms = new string[0];
        public string[] excludePlatforms = new string[0];
        public bool allowUnsafeCode;
        public bool overrideReferences;
        public string[] precompiledReferences = new string[0];
        public bool autoReferenced = true;
        public string[] defineConstraints = new string[0];
        public string[] versionDefines = new string[0];
        public bool noEngineReferences;
    }

    [Serializable]
    public class GistPackageJson
    {
        public string name = string.Empty;
        public string version = "1.0.0";
        public string displayName = string.Empty;
        public string description = string.Empty;
    }

    /// <summary>
    /// 同步 = 把清单里 enabled 的文件按 raw_url 下载并写入 targetPath（写入前先恢复记录的 .meta），
    /// 把 disabled 的文件移入 Backup~/Disabled（文件与 .meta 一并，Unity 忽略该目录，启用时恢复），
    /// 目标目录位于 Packages/ 时在 targetRoot 根上生成唯一的 package.json（Unity 只识别
    /// Packages/ 直接子目录的包，所以必须是单个根包，不能一个 Gist 一个包），最后 AssetDatabase.Refresh()。
    /// 与编辑器 GUI 的“同步”按钮、未来 CI 的恢复步骤是同一套逻辑。
    /// </summary>
    public static class GistSyncService
    {
        /// <summary>根包名：targetRoot/package.json 对应的唯一嵌入式包。</summary>
        public const string RootgistName = "com.gistscripts.gists";

        /// <summary>
        /// 禁用文件的本地备份子目录（`{targetRoot}/Backup~/Disabled/`，Unity 完全忽略的 Backup~ 目录内）。
        /// 保存禁用前的文件与 .meta，启用时恢复——本地改动不会因为禁用而丢失。
        /// </summary>
        internal const string DisabledBackupDirName = "Disabled";

        /// <summary>覆盖下载前的本地保险子目录（`{targetRoot}/Backup~/Overwritten/<yyyyMMdd-HHmmss>/`）。</summary>
        internal const string OverwrittenBackupDirName = "Overwritten";

        /// <summary>同步时顺带检查上游是否有更新（每条目多 1 次 GitHub API 调用；匿名限流 60 次/小时，Token 5,000 次/小时）。</summary>
        public static bool CheckUpstreamOnSync
        {
            get => GistManagerSettings.instance.checkUpstreamOnSync;
            set
            {
                var settings = GistManagerSettings.instance;
                if (settings.checkUpstreamOnSync != value)
                {
                    settings.checkUpstreamOnSync = value;
                    settings.Save();
                }
            }
        }

        /// <summary>单个文件的同步结果。</summary>
        public enum SyncFileResult
        {
            /// <summary>已下载写出并更新 sha256。</summary>
            Written = 0,
            /// <summary>磁盘内容与清单记录的指纹不一致（本地改动），已跳过以避免覆盖。</summary>
            SkippedLocal = 1,
            /// <summary>raw_url 缺失/未知（上游可能改名或删除），已跳过。</summary>
            SkippedNoUrl = 2
        }

        public static async Task<int> SyncAsync(GistManifest manifest, ICollection<GistEntry>? only, Action<string>? report)
        {
            if (manifest == null)
                throw new ArgumentNullException("manifest");

            if (report == null)
                report = delegate { };

            var targets = only ?? new List<GistEntry>(manifest.gists);
            var written = 0;
            var removed = 0;
            var skippedLocal = 0;
            var emptyGists = new List<string>();

            foreach (var entry in targets)
            {
                // 整包禁用：把整棵目录搬到 Backup~/Disabled（启用时整体恢复）
                if (entry.files.All(file => file == null || !file.enabled))
                {
                    var moved = GistSyncService.BackupGistPackageAt(
                        GistManifestStore.ProjectRoot, manifest, entry, report);
                    if (moved)
                        removed++;
                }

                var enabled = entry.files.Where(IsCandidate).ToList();
                if (enabled.Count == 0)
                {
                    emptyGists.Add(entry.author + "/" + entry.id);
                    continue;
                }

                // 清单里没有 raw_url（手工新建的条目）时，先拉一次上游元数据补齐
                GistMetadata? upstreamForThisEntry = null;
                if (enabled.Any(file => string.IsNullOrEmpty(file.rawUrl)))
                {
                    report(GistLoc.T("正在刷新 " + entry.id + " 的元数据 …", "Refreshing metadata for " + entry.id + " ..."));
                    var metadata = await GistApiClient.FetchGistAsync(entry.id);
                    GistCatalog.BackfillRawUrls(entry, metadata);
                    upstreamForThisEntry = metadata;
                    enabled = entry.files.Where(IsCandidate).ToList();
                }

                // 逐文件同步（本地有改动的会跳过）
                foreach (var file in enabled)
                {
                    var result = await SyncOneFileAsync(manifest, entry, file, report);
                    if (result == SyncFileResult.Written)
                        written++;
                    else if (result == SyncFileResult.SkippedLocal)
                        skippedLocal++;
                }

                // 上游更新检查（可选；失败不影响同步本身，只报告）
                if (CheckUpstreamOnSync)
                {
                    try
                    {
                        var fresh = upstreamForThisEntry ?? await GistApiClient.FetchGistAsync(entry.id);
                        var upstreamNames = new HashSet<string>(fresh.Files.Select(f => f.Filename), StringComparer.Ordinal);

                        var changed = 0;
                        foreach (var f in entry.files)
                        {
                            var upstream = fresh.Files.Find(x => x.Filename == f.filename);
                            if (upstream != null
                                && !string.IsNullOrEmpty(f.rawUrl)
                                && !string.Equals(upstream.RawUrl, f.rawUrl, StringComparison.Ordinal))
                                changed++;
                        }

                        var added = fresh.Files.Count(f => !entry.files.Any(ef => ef.filename == f.Filename));
                        var gone = entry.files.Count(f => !upstreamNames.Contains(f.filename));

                        if (changed > 0 || added > 0 || gone > 0)
                        {
                            report(GistLoc.T(
                                "上游有更新：" + entry.author + "/" + entry.id + "：" +
                                changed + " 个文件已更新" +
                                (added > 0 ? "，新增 " + added + " 个文件" : string.Empty) +
                                (gone > 0 ? "，" + gone + " 个文件已被上游删除" : string.Empty) +
                                "（重新 Add 可刷新清单）",
                                "Upstream updated: " + entry.author + "/" + entry.id + ": " +
                                changed + " file(s) changed" +
                                (added > 0 ? ", " + added + " new file(s)" : string.Empty) +
                                (gone > 0 ? ", " + gone + " file(s) removed upstream" : string.Empty) +
                                " — re-add to refresh"));
                        }
                    }
                    catch (Exception)
                    {
                        // 检查失败不中断同步（网络/限流/权限等）
                    }
                }

                // 每个 Gist 目录下按实际用到的归属生成 Editor/Runtime asmdef（包内脚本必须挂 asmdef 才会编译）
                EnsureAsmdefs(manifest, entry);
            }

            EnsureRootPackage(manifest);

            AssetDatabase.Refresh();
            var removedPart = removed > 0
                ? GistLoc.T("，已移除 " + removed + " 个禁用文件", ", " + removed + " disabled file(s) removed")
                : string.Empty;
            var skippedPart = skippedLocal > 0
                ? GistLoc.T("，" + skippedLocal + " 个跳过（本地有改动）", ", " + skippedLocal + " skipped (locally modified)")
                : string.Empty;
            var emptyPart = emptyGists.Count > 0
                ? GistLoc.T(
                    "，" + emptyGists.Count + " 个 gist 没有文件可同步" +
                    (emptyGists.Count <= 2 ? "（" + string.Join("、", emptyGists) + "）" : string.Empty) +
                    "——上游若已有新文件，重新 Add 可刷新文件清单",
                    ", " + emptyGists.Count + " gist(s) have no files to sync" +
                    (emptyGists.Count <= 2 ? " (" + string.Join(", ", emptyGists) + ")" : string.Empty) +
                    " — re-add to pick up upstream files")
                : string.Empty;
            report(GistLoc.T(
                "同步完成：已写入 " + written + " 个文件" + removedPart + skippedPart + emptyPart + "。",
                "Sync finished: " + written + " file(s) written" + removedPart + skippedPart + emptyPart + "."));
            return written;
        }

        /// <summary>
        /// 同步单个文件（force=true 时忽略“本地有改动”保护，用上游内容强制覆盖）。
        /// 返回写出/跳过结果。由 SyncAsync 调用；强制覆盖按钮也可直接调用。
        /// </summary>
        public static Task<SyncFileResult> SyncOneFileAsync(
            GistManifest manifest, GistEntry entry, GistFileEntry file, Action<string>? report = null, bool force = false)
        {
            return SyncOneFileAsyncAt(GistManifestStore.ProjectRoot, manifest, entry, file, report, force);
        }

        internal static async Task<SyncFileResult> SyncOneFileAsyncAt(
            string projectRoot, GistManifest manifest, GistEntry entry, GistFileEntry file, Action<string>? report = null, bool force = false)
        {
            if (report == null)
                report = delegate { };

            // 禁用备份优先（离线、字节一致）：在 Backup~/Disabled 里有被禁用前的文件就直接恢复，
            // 无需网络——包含禁用前的本地修改。找不到才按下面的正常流程走。
            if (TryRestoreFromBackupAt(projectRoot ?? string.Empty, manifest, entry, file, report))
                return SyncFileResult.Written;

            if (string.IsNullOrEmpty(file.rawUrl))
            {
                report(GistLoc.T(
                    "跳过 " + file.filename + "（raw_url 未知——上游文件可能已改名或删除）",
                    "Skipped " + file.filename + " (raw_url unknown — upstream file may have been renamed or removed)"));
                return SyncFileResult.SkippedNoUrl;
            }

            if (!force && IsLocallyModifiedAt(projectRoot ?? string.Empty, file))
            {
                report(GistLoc.T(
                    "跳过 " + file.filename + "（本地有改动，未覆盖）",
                    "Skipped " + file.filename + " (locally modified)"));
                return SyncFileResult.SkippedLocal;
            }

            report(GistLoc.T(
                "正在下载 " + entry.author + "/" + file.filename + " …",
                "Downloading " + entry.author + "/" + file.filename + " ..."));
            var content = await GistApiClient.DownloadTextAsync(file.rawUrl);

            // 确定 Editor/Runtime 归属（显式 > 按内容探测），必要时把旧位置的文件连 meta 一起搬过去
            var placement = ResolvePlacement(file, content);
            file.placement = placement;
            if (placement == GistCatalog.RuntimePlacement && LooksLikeEditorScript(content))
            {
                report(GistLoc.T(
                    "警告：" + file.filename + " 引用了 UnityEditor，但被归入 Runtime——打包会失败，请把它切到 Editor。",
                    "Warning: " + file.filename + " references UnityEditor but is placed in Runtime — player builds will fail; switch it to Editor placement."));
            }
            var expected = GistCatalog.BuildTargetPath(manifest, entry, file.filename, placement);
            if (!string.Equals(file.targetPath, expected, StringComparison.Ordinal))
            {
                MoveFile(file.targetPath, expected);
                file.targetPath = expected;
            }

            // 文件不存在 .meta 时，先把清单里记录的 meta 写回去，保住 guid
            if (!string.IsNullOrEmpty(file.meta))
            {
                var metaPath = GistManifestStore.ToFullPath(file.targetPath) + ".meta";
                if (!File.Exists(metaPath))
                    WriteFile(file.targetPath + ".meta", file.meta);
            }

            WriteFile(file.targetPath, content);
            file.sha256 = GistHash.Sha256Hex(Encoding.UTF8.GetBytes(content));
            return SyncFileResult.Written;
        }

        /// <summary>
        /// 磁盘上的文件内容与清单记录的 sha256 不一致 = 用户本地改动过。
        /// 记录为空（旧清单/手写条目）或磁盘无文件时一律视为可覆盖。读失败按“改动过”处理（不覆盖，防丢数据）。
        /// </summary>
        public static bool IsLocallyModified(GistFileEntry file)
        {
            return IsLocallyModifiedAt(GistManifestStore.ProjectRoot, file);
        }

        internal static bool IsLocallyModifiedAt(string projectRoot, GistFileEntry file)
        {
            if (file == null || string.IsNullOrEmpty(file.sha256))
                return false;

            string fullPath;
            try { fullPath = GistManifestStore.ToFullPathAt(projectRoot ?? string.Empty, file.targetPath); }
            catch (Exception) { return false; }
            if (!File.Exists(fullPath))
                return false;

            try
            {
                return GistHash.Sha256Hex(File.ReadAllBytes(fullPath)) != file.sha256;
            }
            catch (Exception)
            {
                return true;
            }
        }

        public static void WriteFile(string targetPath, string content)
        {
            var fullPath = GistManifestStore.ToFullPath(targetPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                return;

            Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, content ?? string.Empty, new UTF8Encoding(false));
        }

        /// <summary>显式 Editor/Runtime 优先；Auto 时按内容探测（用到 UnityEditor 就归 Editor）。</summary>
        public static string ResolvePlacement(GistFileEntry file, string content)
        {
            var explicitPlacement = GistCatalog.NormalizePlacement(file != null ? file.placement : string.Empty);
            if (explicitPlacement.Length > 0)
                return explicitPlacement;

            return LooksLikeEditorScript(content) ? GistCatalog.EditorPlacement : GistCatalog.RuntimePlacement;
        }

        public static bool LooksLikeEditorScript(string content)
        {
            if (string.IsNullOrEmpty(content))
                return false;

            return content.IndexOf("using UnityEditor", StringComparison.Ordinal) >= 0
                   || content.IndexOf("UnityEditor.", StringComparison.Ordinal) >= 0;
        }

        /// <summary>把文件（连同 .meta）从旧路径搬到新路径；旧文件不存在时什么都不做（还没落盘）。</summary>
        public static void MoveFile(string fromTargetPath, string toTargetPath)
        {
            var from = GistManifestStore.ToFullPath(fromTargetPath);
            if (!File.Exists(from))
                return;

            var to = GistManifestStore.ToFullPath(toTargetPath);
            var directory = Path.GetDirectoryName(to);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var fromMeta = from + ".meta";
            var toMeta = to + ".meta";
            if (File.Exists(fromMeta) && !File.Exists(toMeta))
                File.Move(fromMeta, toMeta);
            else if (File.Exists(fromMeta))
                File.Delete(fromMeta);

            if (File.Exists(to))
                File.Delete(to);
            File.Move(from, to);
        }

        /// <summary>
        /// 每个 Gist 目录按实际用到的归属生成 asmdef：包内脚本不挂 asmdef 就不会被编译。
        /// Editor 版本 includePlatforms=["Editor"]，Runtime 版本不限平台；某层没有 .cs 时删掉对应 asmdef。
        /// </summary>
        public static void EnsureAsmdefs(GistManifest manifest, GistEntry entry)
        {
            if (manifest == null || entry == null || string.IsNullOrEmpty(entry.packageDir))
                return;

            var baseDir = GistManifestStore.ToFullPath(manifest.NormalizedTargetRoot + "/" + entry.packageDir);
            foreach (var placement in new[] { GistCatalog.EditorPlacement, GistCatalog.RuntimePlacement })
            {
                var folder = Path.Combine(baseDir, placement);
                var asmdefPath = Path.Combine(folder, AsmdefFileName(entry.packageDir, placement));
                var hasScripts = Directory.Exists(folder) && Directory.GetFiles(folder, "*.cs").Length > 0;

                // 迁移：清掉旧命名（GistScripts.* 前缀）的 asmdef，只留期望的那份
                if (Directory.Exists(folder))
                {
                    foreach (var stale in Directory.GetFiles(folder, "*.asmdef"))
                    {
                        var name = Path.GetFileName(stale);
                        if (!string.Equals(name, Path.GetFileName(asmdefPath), StringComparison.OrdinalIgnoreCase)
                            && name.StartsWith("GistScripts.", StringComparison.Ordinal))
                        {
                            File.Delete(stale);
                            if (File.Exists(stale + ".meta"))
                                File.Delete(stale + ".meta");
                        }
                    }
                }

                if (hasScripts)
                {
                    if (File.Exists(asmdefPath))
                        continue;

                    Directory.CreateDirectory(folder);
                    File.WriteAllText(asmdefPath, JsonUtility.ToJson(CreateAsmdef(entry.packageDir, placement), true) + "\n", new UTF8Encoding(false));
                }
                else
                {
                    if (File.Exists(asmdefPath))
                    {
                        File.Delete(asmdefPath);
                        if (File.Exists(asmdefPath + ".meta"))
                            File.Delete(asmdefPath + ".meta");
                    }

                    // 归属文件夹空了（含非 .cs 文件都没有）就连目录带 meta 一起清掉
                    if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0)
                    {
                        Directory.Delete(folder);
                        if (File.Exists(folder + ".meta"))
                            File.Delete(folder + ".meta");
                    }
                }
            }
        }

        /// <summary>asmdef 文件/程序集名：{packageDir 里 - 换成 _}.{placement}，不加额外前缀。</summary>
        public static string AsmdefFileName(string packageDir, string placement)
        {
            return AssemblySafe(packageDir) + "." + placement + ".asmdef";
        }

        private static string AssemblySafe(string packageDir)
        {
            return packageDir.Replace('-', '_');
        }

        private static GistAsmdefJson CreateAsmdef(string packageDir, string placement)
        {
            var editorOnly = placement == GistCatalog.EditorPlacement;
            return new GistAsmdefJson
            {
                name = AssemblySafe(packageDir) + "." + placement,
                rootNamespace = string.Empty,
                includePlatforms = editorOnly ? new[] { "Editor" } : new string[0],
                autoReferenced = true
            };
        }

        /// <summary>
        /// 禁用整个包：把整棵 gist 目录（.cs / .meta / asmdef / 所有子目录）移入
        /// `{targetRoot}/Backup~/Disabled/`（Unity 忽略的 Backup~ 目录）。
        /// 原地清空并记录 `files[].enabled=false` 语义不变；启用时整体恢复（含本地修改与 GUID）。
        /// 目标 Backup~ 已有同名非空目录时自动加 `-old-<时间戳>` 后缀，绝不覆盖已有备份。
        /// </summary>
        public static bool BackupGistPackage(GistManifest manifest, GistEntry entry, Action<string>? report = null)
        {
            return BackupGistPackageAt(GistManifestStore.ProjectRoot, manifest, entry, report);
        }

        internal static bool BackupGistPackageAt(string projectRoot, GistManifest manifest, GistEntry entry, Action<string>? report = null)
        {
            if (manifest == null || entry == null)
                return false;

            var root = GistManifestStore.NormalizeRelative(manifest.NormalizedTargetRoot);
            if (string.IsNullOrEmpty(entry.packageDir))
            {
                foreach (var file in entry.files)
                    file.enabled = false;
                return true;   // 没有目录可搬：只记录关闭（子目录不动）
            }

            // 收集仍落在旧根下的包目录（含自定义路径的文件也挪到 Backup~/Disabled/）
            var src = GistManifestStore.NormalizeRelative(root + "/" + entry.packageDir);
            var srcFull = Path.Combine(
                projectRoot.Replace('\\', '/').TrimEnd('/'),
                src.Replace('/', Path.DirectorySeparatorChar));

            var destRel = GistManifestStore.BackupDirName + "/" + DisabledBackupDirName + "/" + entry.packageDir;
            var destFull = Path.Combine(
                projectRoot.Replace('\\', '/').TrimEnd('/'),
                (root + "/" + destRel).Replace('/', Path.DirectorySeparatorChar));

            var actuallyMoved = 0;
            try
            {
                if (Directory.Exists(srcFull) && Directory.GetFileSystemEntries(srcFull).Length > 0)
                {
                    var destParent = Path.GetDirectoryName(destFull);
                    if (!string.IsNullOrEmpty(destParent))
                        Directory.CreateDirectory(destParent);

                    var finalDest = destFull;
                    if (Directory.Exists(finalDest) && Directory.GetFileSystemEntries(finalDest).Length > 0)
                        finalDest += ".old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");   // 绝不覆盖已有备份
                    else if (Directory.Exists(finalDest))
                        Directory.Delete(finalDest, recursive: true);   // 空目录直接复用

                    Directory.Move(srcFull, finalDest);
                    actuallyMoved = 1;
                }
                else if (Directory.Exists(srcFull))
                {
                    // 空包目录：直接删掉（Unity 不识别它，留着也是死文件夹）
                    try { Directory.Delete(srcFull, recursive: false); } catch (Exception) { }
                }

                // 包目录的 .meta 是根的兄弟文件，不跟目录走——一并搬进 Backup~/Disabled
                var srcMeta = srcFull + ".meta";
                var destMeta = destFull + ".meta";
                if (File.Exists(srcMeta))
                {
                    if (File.Exists(destMeta))
                        destMeta += ".old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Move(srcMeta, destMeta);
                }

                // 散置在包目录外的 enabled 文件（手工配置过的）：记录为已禁用，保持不动
                // ——避免这种人为边角破坏主流程
            }
            catch (Exception ex)
            {
                report?.Invoke(GistLoc.T(
                    "禁用备份失败（目录未改动）：" + ex.Message,
                    "Package disable backup failed (directory unchanged): " + ex.Message));
                return false;
            }

            foreach (var file in entry.files)
            {
                file.enabled = false;
                file.meta = string.Empty;   // 不再存 meta；guid 在 Backup~ 树里保留
            }

            if (actuallyMoved > 0)
                report?.Invoke(GistLoc.T(
                    "已把整包搬到 Backup~/Disabled：" + entry.packageDir,
                    "Moved package into Backup~/Disabled: " + entry.packageDir));
            return true;
        }

        /// <summary>
        /// 从禁用备份（`Backup~/Disabled/&lt;packageDir&gt;`）整体恢复包目录。
        /// 目标已存在同名目录且非空则不覆盖（返回 false）；备份不存在返回 false。
        /// </summary>
        public static bool TryRestoreGistPackage(GistManifest manifest, GistEntry entry, Action<string>? report = null)
        {
            return TryRestoreGistPackageAt(GistManifestStore.ProjectRoot, manifest, entry, report);
        }

        internal static bool TryRestoreGistPackageAt(string projectRoot, GistManifest manifest, GistEntry entry, Action<string>? report = null)
        {
            if (manifest == null || entry == null)
                return false;

            var root = GistManifestStore.NormalizeRelative(manifest.NormalizedTargetRoot);
            if (string.IsNullOrEmpty(entry.packageDir))
                return false;

            var backupDir = Path.Combine(
                projectRoot.Replace('\\', '/').TrimEnd('/'),
                (root + "/" + GistManifestStore.BackupDirName + "/" + DisabledBackupDirName + "/" + entry.packageDir)
                    .Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(backupDir))
                return false;

            var destDir = Path.Combine(
                projectRoot.Replace('\\', '/').TrimEnd('/'),
                (root + "/" + entry.packageDir).Replace('/', Path.DirectorySeparatorChar));

            if (Directory.Exists(destDir) && Directory.GetFileSystemEntries(destDir).Length > 0)
            {
                report?.Invoke(GistLoc.T(
                    "目标目录已存在且非空，不恢复禁用备份：" + entry.packageDir,
                    "Destination is non-empty; not restoring the disabled package: " + entry.packageDir));
                return false;
            }

            var parent = Path.GetDirectoryName(destDir);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);

            try
            {
                if (Directory.Exists(destDir))
                    Directory.Delete(destDir, recursive: true);   // 目录为空：复用
                Directory.Move(backupDir, destDir);

                var backupMeta = backupDir + ".meta";
                var destMeta = destDir + ".meta";
                if (File.Exists(backupMeta))
                {
                    if (File.Exists(destMeta))
                        destMeta += ".old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Move(backupMeta, destMeta);
                }

                // best effort：清空搬走后腾空的 Backup~/Disabled 下的子目录
                var backupParent = Path.GetDirectoryName(backupDir);
                while (!string.IsNullOrEmpty(backupParent) && Directory.Exists(backupParent))
                {
                    if (Directory.GetFileSystemEntries(backupParent).Length > 0)
                        break;
                    try { Directory.Delete(backupParent, recursive: false); } catch (Exception) { break; }
                    backupParent = Path.GetDirectoryName(backupParent);
                }

                foreach (var file in entry.files)
                    file.enabled = true;

                report?.Invoke(GistLoc.T(
                    "已从禁用备份恢复整包：" + entry.packageDir,
                    "Restored package from the disabled backup: " + entry.packageDir));
                return true;
            }
            catch (Exception ex)
            {
                report?.Invoke(GistLoc.T(
                    "恢复禁用备份失败：" + ex.Message,
                    "Failed to restore the disabled package: " + ex.Message));
                return false;
            }
        }

        /// <summary>把目标路径相对目标根的部分切出来（正斜杠）；不在根下 = 空。</summary>
        internal static string RelativeTarget(string targetPath, string targetRoot)
        {
            var path = GistManifestStore.NormalizeRelative(targetPath);
            var root = GistManifestStore.NormalizeRelative(targetRoot).TrimEnd('/');
            var prefix = root + "/";
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return path.Substring(prefix.Length);
            return string.Empty;
        }

        /// <summary>`{projectRoot}/{targetRoot}/Backup~` 的绝对路径。</summary>
        internal static string BackupRootFull(string projectRoot, string targetRoot)
        {
            var root = GistManifestStore.NormalizeRelative(targetRoot).TrimEnd('/');
            return (projectRoot.Replace('\\', '/').TrimEnd('/') + "/" + root + "/" + GistManifestStore.BackupDirName)
                .Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// 从禁用备份（Backup~/Disabled/&lt;targetPath 去掉目标根的部分&gt;）恢复文件及其 .meta。
        /// 目标已存在则不覆盖（返回 false）。成功时文件与 .meta 的字节内容与禁用前完全一致。
        /// </summary>
        public static bool TryRestoreFromBackup(GistManifest manifest, GistEntry entry, GistFileEntry file,
            Action<string>? report = null)
        {
            return TryRestoreFromBackupAt(GistManifestStore.ProjectRoot, manifest, entry, file, report);
        }

        internal static bool TryRestoreFromBackupAt(string projectRoot, GistManifest manifest, GistEntry entry, GistFileEntry file,
            Action<string>? report = null)
        {
            if (report == null)
                report = delegate { };
            if (manifest == null || file == null || string.IsNullOrEmpty(file.targetPath))
                return false;

            var root = GistManifestStore.NormalizeRelative(manifest.NormalizedTargetRoot);
            var relative = RelativeTarget(file.targetPath, root);
            if (relative.Length == 0)
                return false;

            var backupFile = Path.Combine(
                BackupRootFull(projectRoot, root),
                DisabledBackupDirName,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(backupFile))
                return false;

            string fullPath;
            try { fullPath = GistManifestStore.ToFullPathAt(projectRoot, file.targetPath); }
            catch (Exception) { return false; }

            if (File.Exists(fullPath))
            {
                report(GistLoc.T(
                    "目标文件已存在，不恢复禁用备份：" + file.targetPath,
                    "Target file already exists; not restoring from the disabled backup: " + file.targetPath));
                return false;
            }

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.Move(backupFile, fullPath);

            var backupMetaPath = backupFile + ".meta";
            var metaPath = fullPath + ".meta";
            if (File.Exists(backupMetaPath) && !File.Exists(metaPath))
                File.Move(backupMetaPath, metaPath);

            // 清理搬走后腾空的备份子目录（best effort）
            try
            {
                var dir = Path.GetDirectoryName(backupFile);
                var disabledDir = Path.Combine(BackupRootFull(projectRoot, root), DisabledBackupDirName);
                while (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)
                       && !string.Equals(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar),
                           Path.GetFullPath(disabledDir).TrimEnd(Path.DirectorySeparatorChar),
                           StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.GetFileSystemEntries(dir).Length > 0)
                        break;
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch (Exception)
            {
                // best effort
            }

            report(GistLoc.T(
                "已从禁用备份恢复 " + file.filename,
                "Restored " + file.filename + " from the disabled backup"));
            return true;
        }

        /// <summary>
        /// 覆盖下载前的保险：把被覆盖的本地文件复制到 `{targetRoot}/Backup~/Overwritten/&lt;yyyyMMdd-HHmmss&gt;/&lt;相对路径&gt;`。
        /// 只复制局部有改动的文件——已被 sha256 保护判定为未改动的文件不会进 Backup~。
        /// </summary>
        public static void BackupOverwritten(GistManifest manifest, GistFileEntry file)
        {
            BackupOverwrittenAt(GistManifestStore.ProjectRoot, manifest, file);
        }

        internal static void BackupOverwrittenAt(string projectRoot, GistManifest manifest, GistFileEntry file)
        {
            if (manifest == null || file == null || string.IsNullOrEmpty(file.targetPath))
                return;

            var root = GistManifestStore.NormalizeRelative(manifest.NormalizedTargetRoot);
            var relative = RelativeTarget(file.targetPath, root);
            if (relative.Length == 0)
                return;

            string fullPath;
            try { fullPath = GistManifestStore.ToFullPathAt(projectRoot, file.targetPath); }
            catch (Exception) { return; }
            if (!File.Exists(fullPath))
                return;

            var destination = Path.Combine(
                Path.Combine(BackupRootFull(projectRoot, root), OverwrittenBackupDirName,
                    DateTime.Now.ToString("yyyyMMdd-HHmmss")),
                relative.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.Copy(fullPath, destination, true);
        }

        /// <summary>
        /// 目标根位于 Packages/ 时，在 targetRoot 根上生成唯一的 package.json（Unity 只把
        /// Packages/ 直接子目录当作嵌入式包），并清理旧版“每个 Gist 一个 package.json”的残留。
        /// </summary>
        public static void EnsureRootPackage(GistManifest manifest)
        {
            if (manifest == null || !manifest.UsesEmbeddedPackages)
                return;

            var root = manifest.NormalizedTargetRoot;
            var rootDir = GistManifestStore.ToFullPath(root);

            // 清理旧残留：targetRoot 下一层目录里的 package.json（含 .meta）
            if (Directory.Exists(rootDir))
            {
                foreach (var sub in Directory.GetDirectories(rootDir))
                {
                    var stray = Path.Combine(sub, "package.json");
                    var strayMeta = stray + ".meta";
                    if (File.Exists(stray))
                        File.Delete(stray);
                    if (File.Exists(strayMeta))
                        File.Delete(strayMeta);
                }
            }

            var path = rootDir + Path.DirectorySeparatorChar + "package.json";
            if (File.Exists(path))
                return;

            var package = new GistPackageJson
            {
                name = RootgistName,
                version = "1.0.0",
                displayName = "Gist Scripts",
                description = "Scripts synced from GitHub Gists. See gists.json (this package root) for the manifest."
            };

            Directory.CreateDirectory(rootDir);
            File.WriteAllText(path, JsonUtility.ToJson(package, true) + "\n", new UTF8Encoding(false));
        }

        private static string ReadAllTextSafe(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>已启用且有目标路径，可能还缺 raw_url。</summary>
        private static bool IsCandidate(GistFileEntry file)
        {
            return file != null && file.enabled && !string.IsNullOrEmpty(file.filename)
                   && !string.IsNullOrEmpty(file.targetPath);
        }
    }
}