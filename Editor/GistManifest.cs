using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MUD.Modules.GistManager
{
    /// <summary>SHA-256 指纹工具（hex，小写）。下载写出时记录、加载时回填、同步时比对。</summary>
    public static class GistHash
    {
        public static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(data);
                var sb = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// gists.json 的内存表示。它是唯一事实源：GUI 操作和手动编辑都只改这份清单，
    /// 文件下载是独立的“同步”动作。
    /// </summary>
    [Serializable]
    public class GistManifest
    {
        public const string DefaultTargetRoot = "Packages/gist-scripts";

        /// <summary>同步时文件写入的根目录（项目相对路径），如 Packages/GistScripts 或 Assets/GistScripts。</summary>
        public string targetRoot = DefaultTargetRoot;

        public List<GistEntry> gists = new List<GistEntry>();

        public string NormalizedTargetRoot
        {
            get
            {
                var root = (targetRoot ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
                if (root.StartsWith("./", StringComparison.Ordinal))
                    root = root.Substring(2);
                return root.Length == 0 ? DefaultTargetRoot : root;
            }
        }

        /// <summary>
        /// 只有 targetRoot 严格位于 Packages/ 下一层时才算（`Packages` 本身不是合法的包根：
        /// Unity 只把 Packages/ 的直接子目录当包，写 Packages/package.json 无意义，
        /// 且清理逻辑会误伤其它包）。
        /// </summary>
        public bool UsesEmbeddedPackages
        {
            get
            {
                var root = NormalizedTargetRoot;
                return root.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Serializable]
    public class GistEntry
    {
        /// <summary>Gist id（去重键）。</summary>
        public string id = string.Empty;

        /// <summary>作者，从 URL 解析后持久化，窗口构建层级视图时无需网络。</summary>
        public string author = string.Empty;

        public string description = string.Empty;

        /// <summary>内部分组目录名（author.slug），添加时生成并固定，之后不再随上游变化。</summary>
        public string packageDir = string.Empty;

        public List<GistFileEntry> files = new List<GistFileEntry>();
    }

    [Serializable]
    public class GistFileEntry
    {
        public string filename = string.Empty;

        /// <summary>项目相对路径（正斜杠）。</summary>
        public string targetPath = string.Empty;

        /// <summary>仅控制同步时是否下载/覆盖，不涉及移动或修改包内文件。</summary>
        public bool enabled = true;

        /// <summary>缓存的 raw_url，添加/刷新元数据时写入，同步时直接使用。</summary>
        public string rawUrl = string.Empty;

        /// <summary>
        /// 文件被禁用移除时抓下来的 .meta 内容（含 guid）。
        /// 下次同步启用时，若磁盘上没有 .meta，先把它写回去，保住资源引用。
        /// </summary>
        public string meta = string.Empty;

        /// <summary>
        /// 编译归属："Editor" / "Runtime"，空字符串 = Auto（同步时按内容探测：
        /// 用到 UnityEditor 就归 Editor，否则 Runtime）。
        /// 决定文件落在 {targetRoot}/{packageDir}/Editor|Runtime/ 下，同目录生成对应 asmdef。
        /// </summary>
        public string placement = string.Empty;

        /// <summary>
        /// 最近一次由本工具写出的内容的 SHA-256（hex，小写）。
        /// 同步时若磁盘文件存在且指纹不一致 = 用户本地改动过 → 跳过并提示，避免覆盖。
        /// 空字符串 = 无可比基线（旧清单或手写条目），按可覆盖处理。
        /// </summary>
        public string sha256 = string.Empty;
    }

    /// <summary>
    /// gists.json 的读写。清单位于内嵌包目录（Packages/gist-scripts/gists.json），
    /// 与已同步的脚本同目录；既不是只读的包缓存，也不会被 AssetDatabase 导入，
    /// CI 直接读同一个文件即可。
    /// </summary>
    public static class GistManifestStore
    {
        public const string ManifestFileName = "gists.json";

        /// <summary>默认清单目录（老项目未切换过 Target Root 时的位置）。</summary>
        public const string ManifestDirectoryRelative = "Packages/gist-scripts";

        /// <summary>旧版清单位置（工程根目录）。仅用于一次迁移；新位置始终优先。</summary>
        public static string LegacyManifestPath
        {
            get { return ProjectRoot + "/" + ManifestFileName; }
        }

        public static string ProjectRoot
        {
            get
            {
                var dataPath = Application.dataPath.Replace('\\', '/');
                return dataPath.EndsWith("/Assets", StringComparison.OrdinalIgnoreCase)
                    ? dataPath.Substring(0, dataPath.Length - "/Assets".Length)
                    : dataPath;
            }
        }

        /// <summary>
        /// 清单路径：`{ProjectRoot}/{GistManagerSettings.manifestRelativePath}`；
        /// 未记录（老项目）或记录损坏时回落 `Packages/gist-scripts/gists.json`。
        /// 切换 Target Root 时，清单随根移动，并由搬运流程把新位置写入设置资产。
        /// </summary>
        /// <summary>
        /// 备份目录名（`Backup~`，Unity 完全忽略——不导入、不生成 .meta、不编译；包内也不计入嵌入式包）。
        /// 所有工具自己生成的备份都放这里，随目标根目录迁移。
        /// </summary>
        public const string BackupDirName = "Backup~";

        public static string ManifestPath
        {
            get { return CombineManifestPath(ProjectRoot, GistManagerSettings.instance.manifestRelativePath); }
        }

        /// <summary>默认清单相对位置（`Packages/gist-scripts/gists.json`，老项目回落）。</summary>
        public const string DefaultManifestRelative = ManifestDirectoryRelative + "/" + ManifestFileName;

        /// <summary>
        /// 由工程根 + 设置里记录的相对路径算出清单绝对路径（纯函数，便于测试）。
        /// 记录为空 / 非法（绝对路径、..、盘符）一律回落默认位置——绝不允许落到项目外。
        /// </summary>
        internal static string CombineManifestPath(string projectRoot, string? recordedRelativePath)
        {
            var normalizedRoot = (projectRoot ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            var relative = NormalizeRelative(string.IsNullOrWhiteSpace(recordedRelativePath)
                ? DefaultManifestRelative
                : recordedRelativePath!);
            if (!IsSafeProjectRelative(relative))
                relative = DefaultManifestRelative;
            return normalizedRoot + "/" + relative;
        }

        public static DateTime ManifestWriteTimeUtc
        {
            get
            {
                try
                {
                    return File.Exists(ManifestPath) ? File.GetLastWriteTimeUtc(ManifestPath) : DateTime.MinValue;
                }
                catch (Exception)
                {
                    return DateTime.MinValue;
                }
            }
        }

        /// <summary>目标路径必须是安全的项目相对路径：非空、非绝对、无盘符、无 ..。</summary>
        public static bool IsSafeProjectRelative(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;

            var path = relativePath.Replace('\\', '/').Trim();
            if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains(":"))
                return false;

            var segments = path.Split('/');
            foreach (var segment in segments)
            {
                if (segment == "..")
                    return false;
            }

            return true;
        }

        public static string NormalizeRelative(string projectRelativePath)
        {
            return (projectRelativePath ?? string.Empty).Replace('\\', '/').Trim().Trim('/');
        }

        public static string ToFullPath(string projectRelativePath)
        {
            return ToFullPathAt(ProjectRoot, projectRelativePath);
        }

        /// <summary>显式工程根的 ToFullPath 内部重载（供搬运/备份等内部消费者与测试参数化路径用）。</summary>
        internal static string ToFullPathAt(string projectRoot, string projectRelativePath)
        {
            if (!IsSafeProjectRelative(projectRelativePath))
                throw new InvalidOperationException("Unsafe project path: " + projectRelativePath);

            var relative = NormalizeRelative(projectRelativePath);
            var normalizedRoot = (projectRoot ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            return (normalizedRoot + "/" + relative).Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>旧位置（工程根目录）存在清单时，自动搬到 Packages/gist-scripts/gists.json。</summary>
        private static void MigrateLegacyManifestIfNeeded(string legacyManifestPath, string manifestPath)
        {
            var legacy = legacyManifestPath;
            if (File.Exists(manifestPath) || !File.Exists(legacy))
                return;

            try
            {
                var directory = Path.GetDirectoryName(manifestPath);
                if (directory != null && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.Move(legacy, manifestPath);
                UnityEngine.Debug.Log("[GistManager] gists.json 已迁移到 " + manifestPath);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[GistManager] 清单迁移失败（保留原位置）: " + ex.Message);
            }
        }

        public static GistManifest LoadOrCreate(out string? error)
        {
            return LoadOrCreateAtPath(ManifestPath, LegacyManifestPath, out error);
        }

        /// <summary>
        /// 显式路径的 LoadOrCreate（供测试与内部复用；公开入口走 ManifestPath）。
        /// 文件损坏（读失败 / JSON 非法）时，把原文件复制为 `gists.json.bad-yyyyMMdd-HHmmss-fff` 再报错——
        /// 后续任何保存操作都不会静默覆盖唯一的清单。
        /// </summary>
        internal static GistManifest LoadOrCreateAtPath(string manifestPath, string legacyManifestPath, out string? error)
        {
            error = null;
            MigrateLegacyManifestIfNeeded(legacyManifestPath, manifestPath);
            MigrateStrayBackupsIntoBackupDir(manifestPath);

            if (!File.Exists(manifestPath))
                return new GistManifest();

            string text;
            try
            {
                text = File.ReadAllText(manifestPath);
            }
            catch (Exception ex)
            {
                BackupBadManifest(manifestPath);
                error = "Failed to read gists.json: " + ex.Message + " (the corrupt file was backed up next to the original)";
                return new GistManifest();
            }

            GistManifest? manifest;
            try
            {
                manifest = JsonUtility.FromJson<GistManifest>(text);
            }
            catch (Exception ex)
            {
                BackupBadManifest(manifestPath);
                error = "gists.json is not valid JSON: " + ex.Message + " (the corrupt file was backed up next to the original)";
                return new GistManifest();
            }

            if (manifest == null)
            {
                BackupBadManifest(manifestPath);
                error = "gists.json could not be parsed (the corrupt file was backed up next to the original).";
                return new GistManifest();
            }

            NormalizeNulls(manifest);
            return manifest;
        }

        public static void Save(GistManifest manifest)
        {
            SaveAtPath(ManifestPath, manifest);
        }

        /// <summary>
        /// 显式路径的原子写入（供测试与内部复用；公开入口走 ManifestPath）：
        /// 先写临时文件再原子替换目标；旧版本轮换为 `Backup~/gists.json.bak`（单副本，Backup~ 不入 AssetDatabase）。
        /// 替换失败时退化为「备份 + 覆盖移动」，绝不直接截断目标文件。
        /// </summary>
        internal static void SaveAtPath(string manifestPath, GistManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException("manifest");

            manifest.targetRoot = manifest.NormalizedTargetRoot;

            var directory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            EnsureBackupDir(directory);
            var backupPath = Path.Combine(directory ?? string.Empty, BackupDirName, "gists.json.bak");

            var json = JsonUtility.ToJson(manifest, true);
            var tempPath = manifestPath + ".tmp-" + Guid.NewGuid().ToString("N");

            try
            {
                File.WriteAllText(tempPath, json + "\n", new UTF8Encoding(false));

                if (File.Exists(manifestPath))
                {
                    try
                    {
                        File.Replace(tempPath, manifestPath, backupPath);
                    }
                    catch (Exception)
                    {
                        // 非 NTFS / 目标被占用时 File.Replace 不可用：先复制备份再覆盖
                        File.Copy(manifestPath, backupPath, true);
                        File.Copy(tempPath, manifestPath, true);
                        File.Delete(tempPath);
                    }
                }
                else
                {
                    File.Move(tempPath, manifestPath);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception)
                {
                    // 临时文件清理失败无所谓
                }
            }
        }

        /// <summary>备份目录（`Backup~`），不存在则创建；失败返回 false（调用方按 best-effort 处理）。</summary>
        internal static bool EnsureBackupDir(string? directory)
        {
            if (string.IsNullOrEmpty(directory))
                return false;
            try
            {
                var backupDir = Path.Combine(directory, BackupDirName);
                if (!Directory.Exists(backupDir))
                    Directory.CreateDirectory(backupDir);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 清单目录下的备份类旧残留迁入 Backup~（不复制、不删除内容，只移动）：
        /// `gists.json.bak`、`gists.json.bad-*`、`gists.json.tmp-*` 及它们被 Unity 导入出的 `.meta`。
        /// </summary>
        private static void MigrateStrayBackupsIntoBackupDir(string manifestPath)
        {
            try
            {
                var directory = Path.GetDirectoryName(manifestPath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    return;
                if (!EnsureBackupDir(directory))
                    return;

                var backupDir = Path.Combine(directory, BackupDirName);
                foreach (var name in new[] { "gists.json.bak", "gists.json.bak.meta" })
                {
                    var stray = Path.Combine(directory, name);
                    if (File.Exists(stray))
                        MoveStrayInto(backupDir, stray);
                }
                foreach (var file in Directory.GetFiles(directory, "gists.json.bad-*"))
                    MoveStrayInto(backupDir, file);
                foreach (var file in Directory.GetFiles(directory, "gists.json.bad-*.meta"))
                    MoveStrayInto(backupDir, file);
                foreach (var file in Directory.GetFiles(directory, "gists.json.tmp-*"))
                    MoveStrayInto(backupDir, file);
            }
            catch (Exception)
            {
                // 残留迁移失败不阻塞正常读取
            }
        }

        private static void MoveStrayInto(string backupDir, string stray)
        {
            try
            {
                var name = Path.GetFileName(stray);
                var dest = Path.Combine(backupDir, name);
                if (File.Exists(dest))
                    dest += ".stray-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                File.Move(stray, dest);
                UnityEngine.Debug.Log("[GistManager] 已迁移备份残留到 " + dest);
            }
            catch (Exception)
            {
                // 单个文件迁移失败不阻塞其他迁移
            }
        }

        /// <summary>损坏清单备份（解析失败时调用）；best-effort，失败不阻塞错误报告。备份写入 Backup~。</summary>
        private static void BackupBadManifest(string manifestPath)
        {
            try
            {
                var directory = Path.GetDirectoryName(manifestPath);
                if (string.IsNullOrEmpty(directory))
                    return;
                if (!EnsureBackupDir(directory))
                    return;
                var backupPath = Path.Combine(directory, BackupDirName,
                    "gists.json.bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
                File.Copy(manifestPath, backupPath, false);
                UnityEngine.Debug.LogWarning("[GistManager] gists.json 解析失败，原文件已备份到 " + backupPath);
            }
            catch (Exception)
            {
                // 备份失败不影响错误报告
            }
        }

        private static void NormalizeNulls(GistManifest manifest)
        {
            if (manifest.gists == null)
                manifest.gists = new List<GistEntry>();

            foreach (var entry in manifest.gists)
            {
                if (entry == null)
                    continue;

                entry.id = (entry.id ?? string.Empty).Trim();
                entry.author = string.IsNullOrEmpty(entry.author) ? "anonymous" : entry.author.Trim();
                entry.description = entry.description ?? string.Empty;
                entry.packageDir = entry.packageDir ?? string.Empty;

                if (entry.files == null)
                    entry.files = new List<GistFileEntry>();

                foreach (var file in entry.files)
                {
                    if (file == null)
                        continue;

                    file.filename = file.filename ?? string.Empty;
                    file.targetPath = GistManifestStore.NormalizeRelative(file.targetPath);
                    file.rawUrl = file.rawUrl ?? string.Empty;
                }
            }

            manifest.gists.RemoveAll(entry => entry == null || entry.id == null || entry.id.Length == 0);
        }
    }
}