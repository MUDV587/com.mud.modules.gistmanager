using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace MUD.Modules.GistManager
{
    /// <summary>
    /// Target Root 切换（设置页下拉 →「切换并移动」）时的整体搬运：
    /// 清单（gists.json）、嵌入式包标记（package.json）以及每个 gist 的目录树
    ///（.cs / asmdef / 所有 .meta）一并移动到新根；旧根清空后整体删除。
    /// 预检不通过 → 整体中止、零改动；搬运中途 IO 失败 → 回滚已搬动项，清单不改。
    /// 绝不覆盖目标已有文件；未登记在清单里的东西不会被删除。
    /// </summary>
    public static class GistRelocateService
    {
        public enum RelocateStatus
        {
            /// <summary>搬运成功（或相同根 = 零操作）。</summary>
            Success,
            /// <summary>新根不合法（绝对路径 / 嵌套 / 层级不足）。</summary>
            InvalidRoot,
            /// <summary>目标位置已有同名文件 / 非空 gist 目录——为避免覆盖，整体中止。</summary>
            Blocked,
            /// <summary>搬运中途 IO 失败，已回滚。</summary>
            IoError
        }

        public sealed class RelocateResult
        {
            public RelocateStatus Status = RelocateStatus.Success;
            public readonly List<string> Blockers = new List<string>();
            public int MovedFileCount;
            public int MovedDirectoryCount;
            public bool CreatedEmbeddedPackageJson;
            public bool OldRootDeleted;
            public readonly List<string> Warnings = new List<string>();
            public string? Error;
        }

        /// <summary>测试用失败注入：执行到匹配源路径（全路径，正斜杠）的文件移动时抛 IOException。</summary>
        internal static string? FailOnMovingSource;

        /// <summary>搬运并改写清单的内存模型；调用方随后负责 GistManifestStore.Save / 更新设置资产。</summary>
        public static RelocateResult Relocate(GistManifest manifest, string newRoot)
        {
            return RelocateAt(GistManifestStore.ProjectRoot, manifest, newRoot);
        }

        internal static RelocateResult RelocateAt(string projectRoot, GistManifest manifest, string newRoot)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            var result = new RelocateResult();
            var root = (projectRoot ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            var oldRoot = GistManifestStore.NormalizeRelative(manifest.NormalizedTargetRoot);
            var newRelative = GistManifestStore.NormalizeRelative(newRoot);

            // ---------- 新根合法性 ----------
            var rootBlockers = ValidateNewRoot(oldRoot, newRelative);
            if (rootBlockers.Count > 0)
            {
                result.Status = RelocateStatus.InvalidRoot;
                result.Blockers.AddRange(rootBlockers);
                return result;
            }
            if (string.Equals(oldRoot, newRelative, StringComparison.OrdinalIgnoreCase))
                return result;   // Success，零操作

            var oldFull = Fs(root, oldRoot);
            var newFull = Fs(root, newRelative);

            // ---------- 预检：目标侧任何同名文件 / 非空 gist 目录 → 整体中止 ----------
            var gistDirs = GistDirsNeedingMove(root, oldRoot, manifest);
            var blockers = new List<string>();
            foreach (var gistDir in gistDirs)
            {
                var destDir = Path.Combine(newFull, gistDir.Replace('/', Path.DirectorySeparatorChar));
                if (DirectoryNonEmpty(destDir))
                    blockers.Add("目标目录已存在且不为空：" + newRelative + "/" + gistDir + "（已中止，未做任何改动）");
            }
            foreach (var name in new[] { "gists.json", "gists.json.meta", "package.json", "package.json.meta" })
            {
                var destFile = Path.Combine(newFull, name);
                if (File.Exists(destFile))
                    blockers.Add("目标文件已存在：" + newRelative + "/" + name + "（已中止，未做任何改动）");
            }
            var destBackupDir = Path.Combine(newFull, GistManifestStore.BackupDirName);
            if (DirectoryNonEmpty(destBackupDir))
                blockers.Add("目标备份目录已存在且不为空：" + newRelative + "/" + GistManifestStore.BackupDirName + "（已中止，未做任何改动）");
            if (blockers.Count > 0)
            {
                result.Status = RelocateStatus.Blocked;
                result.Blockers.AddRange(blockers);
                return result;
            }

            // ---------- 搬运（任何一步失败 → 逆序回滚） ----------
            var rollback = new List<Action>();
            try
            {
                CreateDirectoryTracked(newFull, rollback);

                // (a) 每个需要移动的 gist 目录（含 .meta / asmdef）；以及 Backup~（工具备份，随根迁移）
                foreach (var gistDir in gistDirs)
                {
                    var src = Path.Combine(oldFull, gistDir.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(src))
                        continue;
                    var dst = Path.Combine(newFull, gistDir.Replace('/', Path.DirectorySeparatorChar));
                    MoveTree(src, dst, rollback, result);
                }

                var srcBackup = Path.Combine(oldFull, GistManifestStore.BackupDirName);
                var dstBackup = Path.Combine(newFull, GistManifestStore.BackupDirName);
                if (Directory.Exists(srcBackup))
                    MoveTree(srcBackup, dstBackup, rollback, result);

                // (b) 清单与嵌入式包标记（源不存在则跳过；同根 = 目标已就位，跳过）
                foreach (var name in new[] { "gists.json", "gists.json.meta", "package.json", "package.json.meta" })
                {
                    var src = Path.Combine(oldFull, name);
                    var dst = Path.Combine(newFull, name);
                    if (!File.Exists(src) || PathsEqual(src, dst))
                        continue;
                    MoveFileTracked(src, dst, rollback, result);
                }

                // (c) 清单内存重定向：
                //   已随 gist 目录搬动的 → 前缀替换；磁盘上本就不存在的（如被禁用、从未落盘）→ 也按新根记录（sync 时按新根恢复）；
                //   磁盘上存在但没搬动的（如 gist 根下的散置文件）→ 保留旧路径 + 警告（旧根因此不会被删空）。
                foreach (var entry in manifest.gists)
                {
                    if (entry == null)
                        continue;
                    var prefix = oldRoot + "/";
                    foreach (var file in entry.files)
                    {
                        if (file == null)
                            continue;
                        var path = GistManifestStore.NormalizeRelative(file.targetPath);
                        if (path.Length == 0)
                            continue;
                        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Warnings.Add("文件不在旧根下，未随搬运移动，路径保持不变：" + path);
                            continue;
                        }

                        var moved = TryGetMovedTopSegment(path, oldRoot, out var top) && gistDirs.Contains(top);
                        if (moved || !File.Exists(Fs(root, path)))
                            file.targetPath = newRelative + path.Substring(oldRoot.Length);
                        else
                            result.Warnings.Add("文件未随搬运移动（不属于要移动的 gist 目录），路径保持不变：" + path);
                    }
                }
                manifest.targetRoot = newRelative;
            }
            catch (Exception ex)
            {
                for (var i = rollback.Count - 1; i >= 0; i--)
                {
                    try { rollback[i](); }
                    catch (Exception) { /* 回滚失败不覆盖原始错误 */ }
                }
                result.Status = RelocateStatus.IoError;
                result.Error = ex.Message;
                return result;
            }

            // ---------- 收尾（best effort，不参与回滚） ----------
            try
            {
                if (ManifestUsesEmbeddedPackages(newRelative) && !File.Exists(Path.Combine(newFull, "package.json")))
                {
                    var package = new GistPackageJson
                    {
                        name = GistSyncService.RootgistName,
                        version = "1.0.0",
                        displayName = "Gist Scripts",
                        description = "Scripts synced from GitHub Gists. See gists.json (this package root) for the manifest."
                    };
                    Directory.CreateDirectory(newFull);
                    File.WriteAllText(
                        Path.Combine(newFull, "package.json"),
                        JsonUtility.ToJson(package, true) + "\n",
                        new UTF8Encoding(false));
                    result.CreatedEmbeddedPackageJson = true;
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add("新根缺少 package.json 且自动创建失败（不妨碍搬运）：" + ex.Message);
            }

            try
            {
                result.OldRootDeleted = DeleteEmptiedRoot(oldFull);
            }
            catch (Exception ex)
            {
                result.Warnings.Add("旧根目录未能完全清空（残留内容不会被删除）：" + ex.Message);
            }

            return result;
        }

        private static List<string> ValidateNewRoot(string oldRoot, string newRoot)
        {
            var blockers = new List<string>();
            if (string.IsNullOrEmpty(newRoot) || !GistManifestStore.IsSafeProjectRelative(newRoot))
            {
                blockers.Add("新根目录非法（空 / 绝对路径 / 盘符 / ..）：" + newRoot);
                return blockers;
            }

            var segments = newRoot.Split('/');
            if (segments.Length < 2)
                blockers.Add("新根至少需要一层目录（如 Assets/GistScripts）：" + newRoot);
            if (segments[0].Equals("Library", StringComparison.OrdinalIgnoreCase)
                || segments[0].Equals("ProjectSettings", StringComparison.OrdinalIgnoreCase))
                blockers.Add("新根不能指向工程生成目录：" + newRoot);

            if (oldRoot.Length > 0)
            {
                if (newRoot.StartsWith(oldRoot + "/", StringComparison.OrdinalIgnoreCase))
                    blockers.Add("新根不能是旧根的子目录：" + newRoot);
                else if (oldRoot.StartsWith(newRoot + "/", StringComparison.OrdinalIgnoreCase))
                    blockers.Add("新根不能是旧根的父目录：" + newRoot);
            }

            return blockers;
        }

        /// <summary>
        /// 收集本次需要移动的顶级 gist 目录名（至少有一个文件真实存在于旧根下）。
        /// 直接位于旧根下的散置文件（不在任何 gist 目录内）不随之移动。
        /// </summary>
        private static HashSet<string> GistDirsNeedingMove(string projectRoot, string oldRoot, GistManifest manifest)
        {
            var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var prefix = oldRoot + "/";
            foreach (var entry in manifest.gists)
            {
                if (entry == null)
                    continue;
                foreach (var file in entry.files)
                {
                    if (file == null)
                        continue;
                    var path = GistManifestStore.NormalizeRelative(file.targetPath);
                    if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var rest = path.Substring(prefix.Length);
                    var slash = rest.IndexOf('/');
                    if (slash <= 0)
                        continue;   // 旧根下的散置文件：不归属任何 gist 目录
                    if (File.Exists(Fs(projectRoot, path)))
                        needed.Add(rest.Substring(0, slash));
                }
            }
            return needed;
        }

        private static bool TryGetMovedTopSegment(string path, string oldRoot, out string top)
        {
            top = string.Empty;
            var prefix = oldRoot + "/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
            var rest = path.Substring(prefix.Length);
            var slash = rest.IndexOf('/');
            if (slash <= 0)
                return false;
            top = rest.Substring(0, slash);
            return true;
        }

        /// <summary>目录存在且至少有一个条目（含隐藏文件）= 非空；探测异常按非空处理（保守中止）。</summary>
        private static bool DirectoryNonEmpty(string dirFull)
        {
            if (!Directory.Exists(dirFull))
                return false;
            try
            {
                return Directory.GetFileSystemEntries(dirFull).Length > 0;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static string Fs(string projectRoot, string relative)
        {
            var combined = (projectRoot.TrimEnd('/') + "/" + relative.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar);
            return combined;
        }

        private static bool PathsEqual(string a, string b)
        {
            return string.Equals(
                Path.GetFullPath(a).Replace('\\', '/'),
                Path.GetFullPath(b).Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }

        private static void CreateDirectoryTracked(string path, List<Action> rollback)
        {
            if (Directory.Exists(path))
                return;
            Directory.CreateDirectory(path);
            var captured = path;
            rollback.Add(() => TryDeleteIfEmpty(captured));
        }

        private static void MoveTree(string srcFull, string dstFull, List<Action> rollback, RelocateResult result)
        {
            if (!Directory.Exists(dstFull))
            {
                Directory.CreateDirectory(dstFull);
                var captured = dstFull;
                rollback.Add(() => TryDeleteIfEmpty(captured));
            }

            foreach (var file in Directory.GetFiles(srcFull))
                MoveFileTracked(file, Path.Combine(dstFull, Path.GetFileName(file)), rollback, result);

            foreach (var dir in Directory.GetDirectories(srcFull))
                MoveTree(dir, Path.Combine(dstFull, Path.GetFileName(dir)), rollback, result);

            result.MovedDirectoryCount++;
        }

        private static void MoveFileTracked(string srcFull, string dstFull, List<Action> rollback, RelocateResult result)
        {
            var srcNorm = srcFull.Replace('\\', '/');
            if (FailOnMovingSource != null
                && string.Equals(srcNorm, FailOnMovingSource.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Simulated move failure at " + srcNorm);

            var capturedSource = srcFull;
            var capturedDest = dstFull;
            rollback.Add(() =>
            {
                if (!File.Exists(capturedDest))
                    return;
                var dir = Path.GetDirectoryName(capturedSource);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.Move(capturedDest, capturedSource);
            });

            File.Move(srcFull, dstFull);
            result.MovedFileCount++;
        }

        private static void TryDeleteIfEmpty(string dirFull)
        {
            try
            {
                if (Directory.Exists(dirFull) && Directory.GetFileSystemEntries(dirFull).Length == 0)
                    Directory.Delete(dirFull, recursive: false);
            }
            catch (Exception)
            {
                // best effort
            }
        }

        /// <summary>删除搬空后的旧根（best effort）；返回旧根是否已被删除。</summary>
        private static bool DeleteEmptiedRoot(string oldFull)
        {
            if (!Directory.Exists(oldFull))
                return true;

            var remaining = CleanupEmptiedTree(oldFull);

            if (Directory.Exists(oldFull))
            {
                if (remaining > 0)
                    return false;   // 仍有非 stray 内容（如用户文件），保留根

                // 此时必为空（只剩 stray .meta 的情况也被清掉了）
                try
                {
                    Directory.Delete(oldFull, recursive: false);
                }
                catch (Exception)
                {
                    return Directory.Exists(oldFull) == false;
                }
            }

            // 旧根目录的 .meta 一起删（Assets/GistScripts.meta 这类）
            try
            {
                var rootMeta = oldFull + ".meta";
                if (File.Exists(rootMeta))
                    File.Delete(rootMeta);
            }
            catch (Exception)
            {
                // 不影响搬运结果
            }

            return !Directory.Exists(oldFull);
        }

        /// <summary>
        /// 递归清理后序：子目录 → .meta 游离文件 → 若空则连目录本身删除；返回仍占用的条目数。
        /// stray .meta 判定：兄弟路径（去掉 .meta）不存在 → 删除（Unity 否则会重建空目录）。
        /// </summary>
        private static int CleanupEmptiedTree(string dirFull)
        {
            var remaining = 0;

            foreach (var sub in Directory.GetDirectories(dirFull))
                remaining += CleanupEmptiedTree(sub);

            // 先删本目录下的 stray .meta（兄弟文件/目录已不存在），避免 Unity 重建空目录
            foreach (var meta in Directory.GetFiles(dirFull, "*.meta"))
            {
                var sibling = meta.Substring(0, meta.Length - ".meta".Length);
                if (File.Exists(sibling) || Directory.Exists(sibling))
                    continue;   // 仍有对应文件/目录：保留 meta

                try
                {
                    File.Delete(meta);
                }
                catch (Exception)
                {
                    remaining += 1;
                }
            }

            try
            {
                if (Directory.GetFileSystemEntries(dirFull).Length == 0)
                    Directory.Delete(dirFull, recursive: false);
                else
                    remaining += Directory.GetFileSystemEntries(dirFull).Length;
            }
            catch (Exception)
            {
                try
                {
                    remaining += Directory.GetFileSystemEntries(dirFull).Length;
                }
                catch (Exception)
                {
                    remaining += 1;
                }
            }

            return remaining;
        }

        private static bool ManifestUsesEmbeddedPackages(string targetRoot)
        {
            return targetRoot.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
