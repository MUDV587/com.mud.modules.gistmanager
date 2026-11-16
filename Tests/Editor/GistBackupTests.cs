using System;
using System.IO;
using NUnit.Framework;

namespace MUD.Modules.GistManager.Tests
{
    /// <summary>
    /// Gist 包级备份/禁用/恢复链路测试。全部在临时工程目录里跑（显式 projectRoot），
    /// 不触碰真实工程、不发网络请求。
    /// </summary>
    public class GistBackupTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "gist-backup-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不算测试失败
            }
        }

        private string Abs(string relative)
        {
            return Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        }

        private void WriteProjectFile(string relativePath, string content)
        {
            var full = Abs(relativePath);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(full, content ?? string.Empty);
        }

        private static GistManifest MakeManifest(string targetRoot)
        {
            var manifest = new GistManifest { targetRoot = targetRoot };
            var entry = new GistEntry
            {
                id = "aaaabbbb",
                author = "tester",
                description = "d",
                packageDir = "tester.sample"
            };
            entry.files.Add(new GistFileEntry
            {
                filename = "Sample.cs",
                targetPath = targetRoot + "/tester.sample/Editor/Sample.cs",
                enabled = true,
                rawUrl = "https://example.invalid/never-fetched-raw",
                meta = "",
                placement = "Editor",
                sha256 = "deadbeef"
            });
            manifest.gists.Add(entry);
            return manifest;
        }

        private void SeedPackage(string targetRoot)
        {
            WriteProjectFile(targetRoot + "/tester.sample/Editor/Sample.cs", "// with local edits");
            WriteProjectFile(targetRoot + "/tester.sample/Editor/Sample.cs.meta",
                "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n");
            WriteProjectFile(targetRoot + "/tester.sample/Editor/tester.sample.Editor.asmdef", "{\"name\":\"tester.sample.Editor\"}");
            WriteProjectFile(targetRoot + "/tester.sample/Editor/tester.sample.Editor.asmdef.meta",
                "fileFormatVersion: 2\nguid: 11223344556677889900112233445566\n");
            WriteProjectFile(targetRoot + "/tester.sample.meta",
                "fileFormatVersion: 2\nguid: 7788990011223344556677889900112\nfolderAsset: yes\n");
        }

        // ------------------------------------------------ 禁用 → Backup~/Disabled

        [Test]
        public void BackupGistPackageAt_MovesWholePackageTreeIntoBackupDir()
        {
            SeedPackage("Packages/gist-scripts");

            var manifest = MakeManifest("Packages/gist-scripts");
            var entry = manifest.gists[0];

            Assert.IsTrue(GistSyncService.BackupGistPackageAt(_root, manifest, entry));

            Assert.IsFalse(Directory.Exists(Abs("Packages/gist-scripts/tester.sample")), "package dir moved");
            var backupDir = Abs("Packages/gist-scripts/Backup~/Disabled/tester.sample");
            Assert.IsTrue(Directory.Exists(backupDir));
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/Backup~/Disabled/tester.sample/Editor/Sample.cs")));
            Assert.AreEqual("// with local edits",
                File.ReadAllText(Abs("Packages/gist-scripts/Backup~/Disabled/tester.sample/Editor/Sample.cs")));
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/Backup~/Disabled/tester.sample/Editor/tester.sample.Editor.asmdef")));
            Assert.IsFalse(File.Exists(Abs("Packages/gist-scripts/tester.sample.meta")),
                "package-dir .meta must move along with the package (Unity recreates it and strand the empty shell otherwise)");
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/Backup~/Disabled/tester.sample.meta")));
            Assert.IsTrue(entry.files[0].enabled == false);
            Assert.AreEqual(string.Empty, entry.files[0].meta);
        }

        [Test]
        public void BackupGistPackageAt_MissingPackageDir_StillRecordsDisabled()
        {
            var manifest = MakeManifest("Packages/gist-scripts");
            var entry = manifest.gists[0];

            Assert.IsTrue(GistSyncService.BackupGistPackageAt(_root, manifest, entry));

            Assert.IsFalse(entry.files[0].enabled);
            Assert.AreEqual(string.Empty, entry.files[0].meta);
            Assert.IsFalse(Directory.Exists(Abs("Packages/gist-scripts/tester.sample")));
        }

        // ------------------------------------------------ 恢复（离线）

        [Test]
        public void TryRestoreGistPackageAt_RestoresTreeAndMetasRoundtrip()
        {
            SeedPackage("Packages/gist-scripts");

            var manifest = MakeManifest("Packages/gist-scripts");
            var entry = manifest.gists[0];

            Assert.IsTrue(GistSyncService.BackupGistPackageAt(_root, manifest, entry));
            Assert.IsFalse(Directory.Exists(Abs("Packages/gist-scripts/tester.sample")));

            Assert.IsTrue(GistSyncService.TryRestoreGistPackageAt(_root, manifest, entry));

            Assert.AreEqual("// with local edits", File.ReadAllText(Abs("Packages/gist-scripts/tester.sample/Editor/Sample.cs")));
            Assert.AreEqual("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n",
                File.ReadAllText(Abs("Packages/gist-scripts/tester.sample/Editor/Sample.cs.meta")));
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/tester.sample/Editor/tester.sample.Editor.asmdef")));
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/tester.sample.meta")),
                "package-dir .meta must be restored alongside the directory");
            Assert.IsFalse(File.Exists(Abs("Packages/gist-scripts/Backup~/Disabled/tester.sample.meta")));
            Assert.IsTrue(entry.files[0].enabled);
            Assert.IsFalse(Directory.Exists(Abs("Packages/gist-scripts/Backup~/Disabled/tester.sample")),
                "emptied backup tree is removed");
        }

        [Test]
        public void TryRestoreGistPackageAt_NothingBackedUp_ReturnsFalse()
        {
            var manifest = MakeManifest("Packages/gist-scripts");
            var entry = manifest.gists[0];

            Assert.IsFalse(GistSyncService.TryRestoreGistPackageAt(_root, manifest, entry));
        }

        [Test]
        public void TryRestoreGistPackageAt_DestinationNonEmpty_DoesNotOverwrite()
        {
            SeedPackage("Packages/gist-scripts");
            WriteProjectFile("Assets/GistScripts/tester.sample/Editor/Sample.cs", "// stale file elsewhere");

            var manifest = MakeManifest("Packages/gist-scripts");
            manifest.targetRoot = "Packages/gist-scripts";
            var entry = manifest.gists[0];
            entry.files[0].targetPath = "Packages/gist-scripts/tester.sample/Editor/Sample.cs";

            // 先在 backup 里放一份完整树，把目标目录预置成“非空”
            Assert.IsTrue(GistSyncService.BackupGistPackageAt(_root, manifest, entry));
            WriteProjectFile("Packages/gist-scripts/tester.sample/Editor/Sample.cs", "// user-created after disable");

            Assert.IsFalse(GistSyncService.TryRestoreGistPackageAt(_root, manifest, entry));
            Assert.AreEqual("// user-created after disable",
                File.ReadAllText(Abs("Packages/gist-scripts/tester.sample/Editor/Sample.cs")));
            Assert.IsTrue(Directory.Exists(Abs("Packages/gist-scripts/Backup~/Disabled/tester.sample")),
                "backup stays when destination non-empty");
        }

        [Test]
        public void SyncOneFileAsyncAt_RestoresFromBackupBeforeUrlCheck()
        {
            // SyncOneFileAsyncAt 的禁用备份恢复先于 rawUrl 检查：
            // 包已禁用、启用后重 Sync 时应先于网络恢复本地文件（含本地修改）
            SeedPackage("Packages/gist-scripts");

            var manifest = MakeManifest("Packages/gist-scripts");
            manifest.gists[0].files[0].rawUrl = string.Empty;   // 缺少 raw_url 也应恢复

            Assert.IsTrue(GistSyncService.BackupGistPackageAt(_root, manifest, manifest.gists[0]));
            manifest.gists[0].files[0].enabled = true;   // 启用后 Sync

            var result = GistSyncService.SyncOneFileAsyncAt(_root, manifest, manifest.gists[0], manifest.gists[0].files[0]).Result;

            Assert.AreEqual(GistSyncService.SyncFileResult.Written, result);
            Assert.AreEqual("// with local edits",
                File.ReadAllText(Abs("Packages/gist-scripts/tester.sample/Editor/Sample.cs")));
        }

        // ------------------------------------------------ 覆盖下载前保险（保留文件级测试）

        [Test]
        public void BackupOverwrittenAt_CopiesExistingLocalFileToTimestampedBackup()
        {
            WriteProjectFile("Packages/gist-scripts/tester.sample/Editor/Sample.cs", "// local edits");

            var manifest = MakeManifest("Packages/gist-scripts");
            var file = manifest.gists[0].files[0];

            GistSyncService.BackupOverwrittenAt(_root, manifest, file);

            var overwrittenDir = Abs("Packages/gist-scripts/Backup~/Overwritten");
            Assert.IsTrue(Directory.Exists(overwrittenDir));
            var tsDirs = Directory.GetDirectories(overwrittenDir);
            Assert.AreEqual(1, tsDirs.Length);
            var copied = Path.Combine(tsDirs[0], "tester.sample/Editor/Sample.cs".Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(copied));
            Assert.AreEqual("// local edits", File.ReadAllText(copied));
            Assert.AreEqual("// local edits", File.ReadAllText(
                Abs("Packages/gist-scripts/tester.sample/Editor/Sample.cs")), "original stays untouched (copy, not move)");
        }
    }
}
