using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MUD.Modules.GistManager
{
    /// <summary>
    /// 清单的合并与路径规则：
    ///  - AddOrUpdate / MergeMetadata：按 id 去重，已存在则更新元数据并保留用户设置（enabled、targetPath）
    ///  - MakePackageDir / BuildTargetPath：生成嵌入式包目录与目标路径
    ///  - Retarget：切换 targetRoot（Packages/GistScripts ↔ Assets/GistScripts）
    /// </summary>
    public static class GistCatalog
    {
        public static GistEntry AddOrUpdate(GistManifest manifest, GistMetadata metadata)
        {
            if (manifest == null)
                throw new ArgumentNullException("manifest");
            if (metadata == null)
                throw new ArgumentNullException("metadata");

            var entry = manifest.gists.Find(g => g.id == metadata.Id);
            if (entry == null)
            {
                entry = new GistEntry { id = metadata.Id };
                manifest.gists.Add(entry);
            }

            if (string.IsNullOrEmpty(entry.packageDir))
                entry.packageDir = MakePackageDir(manifest, metadata.Id, metadata.Author, metadata.Description, FirstFilename(metadata));

            MergeMetadata(manifest, entry, metadata);
            return entry;
        }

        /// <summary>
        /// 同步时的元数据刷新：只回填缺失的 raw_url，不增删文件。
        /// 清单的文件选择只由 Add（或手动编辑）改变，同步只负责下载。
        /// </summary>
        public static void BackfillRawUrls(GistEntry entry, GistMetadata metadata)
        {
            foreach (var file in entry.files)
            {
                if (!string.IsNullOrEmpty(file.rawUrl))
                    continue;

                var upstream = metadata.Files.Find(candidate => candidate.Filename == file.filename);
                if (upstream != null)
                    file.rawUrl = upstream.RawUrl;
            }
        }

        /// <summary>用上游元数据刷新条目；只更新 raw_url/描述/作者，不改动 enabled 与 targetPath。</summary>
        public static void MergeMetadata(GistManifest manifest, GistEntry entry, GistMetadata metadata)
        {
            entry.id = string.IsNullOrEmpty(metadata.Id) ? entry.id : metadata.Id;
            entry.author = string.IsNullOrEmpty(metadata.Author) ? GistUrlParser.AnonymousAuthor : metadata.Author;
            entry.description = metadata.Description ?? string.Empty;

            if (string.IsNullOrEmpty(entry.packageDir))
                entry.packageDir = MakePackageDir(manifest, entry.id, entry.author, entry.description, FirstFilename(metadata));

            foreach (var file in metadata.Files)
            {
                var existing = entry.files.Find(f => f.filename == file.Filename);
                if (existing != null)
                {
                    existing.rawUrl = file.RawUrl;
                    continue;
                }

                entry.files.Add(new GistFileEntry
                {
                    filename = file.Filename,
                    targetPath = BuildTargetPath(manifest, entry, file.Filename),
                    enabled = true,
                    rawUrl = file.RawUrl
                });
            }
        }

        /// <summary>
        /// 目录名 = author.slug，slug 优先取首个文件名（去扩展名，如 UnitypackageExtractor），
        /// 没有文件名时才回退到描述；全部小写并净化。
        /// 描述往往是一整句话（会被截断成难看的长目录），文件名才短而有辨识度。
        /// 与其他条目冲突时追加 id 前缀。
        /// </summary>
        public static string MakePackageDir(GistManifest manifest, string id, string author, string description, string? firstFilename)
        {
            var authorSegment = SanitizeSegment(author);
            if (authorSegment.Length == 0)
                authorSegment = GistUrlParser.AnonymousAuthor;

            // 优先用文件名的友好形式（UnitypackageExtractor.cs → "Unitypackage Extractor" → unitypackage-extractor），
            // 避免用整句描述当目录名（会被截断得很难看）
            var slugSource = firstFilename != null && !string.IsNullOrWhiteSpace(firstFilename)
                ? GistNiceName.ForFilename(firstFilename)
                : description;

            var slug = SanitizeSegment(slugSource);
            if (slug.Length == 0)
                slug = "gist";

            var dir = authorSegment + "." + slug;
            if (!StartsWithLetterOrDigit(dir))
                dir = "g" + dir;

            var taken = manifest.gists.Exists(g =>
                !string.IsNullOrEmpty(g.packageDir)
                && !string.Equals(g.id, id, StringComparison.Ordinal)
                && string.Equals(g.packageDir, dir, StringComparison.OrdinalIgnoreCase));

            if (taken)
                dir = dir + "." + ShortId(id);

            return dir;
        }

        public const string EditorPlacement = "Editor";
        public const string RuntimePlacement = "Runtime";

        /// <summary>placement 是否为显式 Editor/Runtime（而非 Auto）。</summary>
        public static bool IsExplicitPlacement(string placement)
        {
            return string.Equals(placement, EditorPlacement, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(placement, RuntimePlacement, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>placement 规范化：Editor/Runtime 之外的值一律当作 Auto（空字符串）。</summary>
        public static string NormalizePlacement(string placement)
        {
            if (string.Equals(placement, EditorPlacement, StringComparison.OrdinalIgnoreCase))
                return EditorPlacement;
            if (string.Equals(placement, RuntimePlacement, StringComparison.OrdinalIgnoreCase))
                return RuntimePlacement;
            return string.Empty;
        }

        /// <summary>placement 为空（Auto）时目录里不带 Editor/Runtime 层，同步探测后再归位。</summary>
        public static string BuildTargetPath(GistManifest manifest, GistEntry entry, string filename, string? placement = null)
        {
            var folder = IsExplicitPlacement(placement ?? string.Empty) ? "/" + NormalizePlacement(placement!) : string.Empty;
            return manifest.NormalizedTargetRoot + "/" + entry.packageDir + folder + "/" + filename;
        }

        /// <summary>补全手工编辑清单时缺失/不规范的字段，返回是否有改动。</summary>
        public static bool Normalize(GistManifest manifest)
        {
            var changed = false;

            var normalizedRoot = manifest.NormalizedTargetRoot;
            if (!string.Equals(manifest.targetRoot, normalizedRoot, StringComparison.Ordinal))
            {
                manifest.targetRoot = normalizedRoot;
                changed = true;
            }

            foreach (var entry in manifest.gists)
            {
                if (string.IsNullOrEmpty(entry.author))
                {
                    entry.author = GistUrlParser.AnonymousAuthor;
                    changed = true;
                }

                if (string.IsNullOrEmpty(entry.packageDir))
                {
                    var firstFile = entry.files.Find(f => !string.IsNullOrEmpty(f.filename));
                    entry.packageDir = MakePackageDir(
                        manifest,
                        entry.id,
                        entry.author,
                        entry.description,
                        firstFile != null ? firstFile.filename : null);
                    changed = true;
                }

                foreach (var file in entry.files)
                {
                    if (string.IsNullOrEmpty(file.filename) && !string.IsNullOrEmpty(file.targetPath))
                        file.filename = Path.GetFileName(GistManifestStore.NormalizeRelative(file.targetPath));

                    if (string.IsNullOrEmpty(file.filename))
                        continue;

                    var normalizedPath = GistManifestStore.NormalizeRelative(file.targetPath);
                    if (normalizedPath.Length == 0)
                    {
                        file.targetPath = BuildTargetPath(manifest, entry, file.filename);
                        changed = true;
                    }
                    else if (!string.Equals(file.targetPath, normalizedPath, StringComparison.Ordinal))
                    {
                        file.targetPath = normalizedPath;
                        changed = true;
                    }

                    // 显式 Editor/Runtime 归属但路径里没这一层 → 补上（手工编辑清单时常见）
                    var placement = NormalizePlacement(file.placement);
                    if (!string.Equals(placement, file.placement, StringComparison.Ordinal))
                    {
                        file.placement = placement;
                        changed = true;
                    }

                    if (IsExplicitPlacement(placement)
                        && file.targetPath.IndexOf("/" + placement + "/", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        file.targetPath = BuildTargetPath(manifest, entry, file.filename, placement);
                        changed = true;
                    }

                    // 旧清单（无 sha256）但磁盘上有文件时：把当前内容记为可比基线，立刻获得本地改动保护。
                    // 把“当前磁盘状态”当基准是刻意的——旧清单无法知道上次下载了什么。
                    if (string.IsNullOrEmpty(file.sha256))
                    {
                        try
                        {
                            var full = GistManifestStore.ToFullPath(file.targetPath);
                            if (File.Exists(full))
                            {
                                file.sha256 = GistHash.Sha256Hex(File.ReadAllBytes(full));
                                changed = true;
                            }
                        }
                        catch (Exception)
                        {
                            // 读不到就保持空基线（可覆盖），不阻塞加载
                        }
                    }
                }
            }

            return changed;
        }

        /// <summary>切换 targetRoot，并把仍在旧根目录下的目标路径整体迁移过去。</summary>
        public static void Retarget(GistManifest manifest, string newRoot)
        {
            var oldRoot = manifest.NormalizedTargetRoot;
            manifest.targetRoot = GistManifestStore.NormalizeRelative(newRoot);
            var newNormalized = manifest.NormalizedTargetRoot;

            if (string.Equals(oldRoot, newNormalized, StringComparison.OrdinalIgnoreCase))
                return;

            var prefix = oldRoot + "/";
            foreach (var entry in manifest.gists)
            {
                foreach (var file in entry.files)
                {
                    var path = GistManifestStore.NormalizeRelative(file.targetPath);
                    if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        file.targetPath = newNormalized + path.Substring(oldRoot.Length);
                }
            }
        }

        private static string? FirstFilename(GistMetadata metadata)
        {
            return metadata.Files.Count > 0 ? metadata.Files[0].Filename : null;
        }

        private static string ShortId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "000000";
            return id.Length <= 6 ? id : id.Substring(0, 6);
        }

        private static bool StartsWithLetterOrDigit(string value)
        {
            if (value.Length == 0)
                return false;
            var c = value[0];
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
        }

        /// <summary>转成适合包名/目录名的片段：小写，保留 [a-z0-9._-]，其余折叠为单个 '-'。</summary>
        private static string SanitizeSegment(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder();
            var pendingSeparator = false;

            foreach (var c in value.ToLowerInvariant())
            {
                var keep = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (keep)
                {
                    if (pendingSeparator && builder.Length > 0)
                        builder.Append('-');
                    builder.Append(c);
                    pendingSeparator = false;
                }
                else if (builder.Length > 0)
                {
                    pendingSeparator = true;
                }
            }

            var result = builder.ToString().Trim('.', '-', '_');
            return result.Length > 48 ? result.Substring(0, 48).Trim('.', '-', '_') : result;
        }
    }
}