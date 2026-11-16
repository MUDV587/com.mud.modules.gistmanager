using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MUD.Modules.GistManager.Tests
{
    /// <summary>
    /// 清单读写的可靠性测试。用显式路径重载（GistManifestStore.SaveAtPath / LoadOrCreateAtPath）
    /// 在临时目录里操作，绝不触碰工程里的 Packages/gist-scripts/gists.json。
    /// </summary>
    public class GistManifestSaveTests
    {
        private string _dir = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "gist-manifest-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_dir))
                    Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不算测试失败
            }
        }

        private string PathOf(string fileName)
        {
            return Path.Combine(_dir, fileName);
        }

        private static GistManifest MakeManifest(string description)
        {
            var manifest = new GistManifest
            {
                targetRoot = "Packages/gist-scripts"
            };
            manifest.gists.Add(new GistEntry
            {
                id = "abc123",
                author = "yasirkula",
                description = description,
                packageDir = "yasirkula.foo",
                files =
                {
                    new GistFileEntry
                    {
                        filename = "a.cs",
                        targetPath = "Packages/gist-scripts/yasirkula.foo/a.cs",
                        enabled = true,
                        rawUrl = "https://example.com/a",
                        sha256 = "deadbeef"
                    }
                }
            });
            return manifest;
        }

        [Test]
        public void SaveAtPath_FirstSave_CreatesFileWithoutBak()
        {
            var path = PathOf("gists.json");

            GistManifestStore.SaveAtPath(path, MakeManifest("v1"));

            Assert.IsTrue(File.Exists(path));
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "Backup~", "gists.json.bak")));
            StringAssert.Contains("abc123", File.ReadAllText(path));
            StringAssert.Contains("\"description\": \"v1\"", File.ReadAllText(path));
        }

        [Test]
        public void SaveAtPath_SecondSave_RotatesBakWithPreviousContent()
        {
            var path = PathOf("gists.json");

            GistManifestStore.SaveAtPath(path, MakeManifest("v1"));
            GistManifestStore.SaveAtPath(path, MakeManifest("v2"));

            var bak = Path.Combine(_dir, "Backup~", "gists.json.bak");
            Assert.IsTrue(File.Exists(bak), "backup must live in Backup~ (Unity-ignored)");
            Assert.IsFalse(File.Exists(path + ".bak"), "no backup next to the manifest anymore");
            StringAssert.Contains("\"description\": \"v1\"", File.ReadAllText(bak));
            StringAssert.Contains("\"description\": \"v2\"", File.ReadAllText(path));
        }

        [Test]
        public void LoadOrCreateAtPath_CorruptJson_BacksUpAndKeepsOriginal()
        {
            var path = PathOf("gists.json");
            const string garbage = "{ not valid json at all";
            File.WriteAllText(path, garbage);

            var manifest = GistManifestStore.LoadOrCreateAtPath(path, PathOf("no-legacy-here.json"), out var error);

            Assert.IsFalse(string.IsNullOrEmpty(error), "corrupt file must report an error");
            Assert.IsNotNull(manifest);
            Assert.AreEqual(0, manifest.gists.Count, "corrupt load returns an empty manifest");
            Assert.AreEqual(garbage, File.ReadAllText(path), "the corrupt original must not be overwritten");

            var backups = Directory.GetFiles(Path.Combine(_dir, "Backup~"), "gists.json.bad-*");
            Assert.AreEqual(1, backups.Length, "exactly one .bad-* backup should be produced in Backup~");
            Assert.AreEqual(garbage, File.ReadAllText(backups[0]));
        }

        [Test]
        public void LoadOrCreateAtPath_StrayBackupsNextToManifest_MigrateIntoBackupDir()
        {
            // 旧版把 .bak 放在清单同级（Unity 会导入成 .meta 资产）；加载时应自动迁入 Backup~
            var path = PathOf("gists.json");
            File.WriteAllText(path, "{\"targetRoot\":\"Packages/gist-scripts\"}");
            File.WriteAllText(path + ".bak", "{}");
            File.WriteAllText(path + ".bak.meta", "meta of bak");
            File.WriteAllText(PathOf("gists.json.bad-20000101-000000-001"), "{ bad }");
            File.WriteAllText(PathOf("gists.json.bad-20000101-000000-001.meta"), "meta of bad");
            File.WriteAllText(PathOf("gists.json.tmp-abc123"), "partial");

            var manifest = GistManifestStore.LoadOrCreateAtPath(path, PathOf("no-legacy-here.json"), out var error);

            Assert.IsNull(error);
            Assert.IsTrue(Directory.Exists(Path.Combine(_dir, "Backup~")));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "Backup~", "gists.json.bak")));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "Backup~", "gists.json.bak.meta")));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "Backup~", "gists.json.bad-20000101-000000-001")));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "Backup~", "gists.json.bad-20000101-000000-001.meta")));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "Backup~", "gists.json.tmp-abc123")));
            Assert.IsFalse(File.Exists(path + ".bak"));
            Assert.IsFalse(File.Exists(PathOf("gists.json.bad-20000101-000000-001")));
            Assert.IsFalse(File.Exists(PathOf("gists.json.tmp-abc123")));
        }

        [Test]
        public void LoadOrCreateAtPath_ValidJson_Roundtrips()
        {
            var path = PathOf("gists.json");
            GistManifestStore.SaveAtPath(path, MakeManifest("roundtrip"));

            var manifest = GistManifestStore.LoadOrCreateAtPath(path, PathOf("no-legacy-here.json"), out var error);

            Assert.IsNull(error);
            Assert.AreEqual(1, manifest.gists.Count);
            Assert.AreEqual("yasirkula.foo", manifest.gists[0].packageDir);
            Assert.AreEqual("deadbeef", manifest.gists[0].files[0].sha256);
        }
    }
}
