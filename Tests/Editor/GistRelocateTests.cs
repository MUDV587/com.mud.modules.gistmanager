using System;
using System.IO;
using NUnit.Framework;

namespace MUD.Modules.GistManager.Tests
{
    /// <summary>
    /// Target Root 搬运测试。全部在临时工程目录里跑（显式 projectRoot），
    /// 绝不触碰真实的 Packages/gist-scripts 与 ProjectSettings/GistManager.asset。
    /// </summary>
    public class GistRelocateTests
    {
        private string _root = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "gist-relocate-tests-" + Guid.NewGuid().ToString("N"));
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
            finally
            {
                GistRelocateService.FailOnMovingSource = null;
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

        private static GistManifest MakeManifest(string targetRoot, params string[] gistDirs)
        {
            var manifest = new GistManifest { targetRoot = targetRoot };
            foreach (var dir in gistDirs)
            {
                var entry = new GistEntry
                {
                    id = "id-" + dir,
                    author = "tester",
                    description = "d",
                    packageDir = dir
                };
                entry.files.Add(new GistFileEntry
                {
                    filename = dir + ".cs",
                    targetPath = targetRoot + "/" + dir + "/Editor/" + dir + ".cs",
                    enabled = true,
                    rawUrl = "https://example.com/" + dir,
                    sha256 = "deadbeef"
                });
                manifest.gists.Add(entry);
            }
            return manifest;
        }

        private void SeedPackagesRoot()
        {
            WriteProjectFile("Packages/gist-scripts/gists.json", "{\"targetRoot\":\"Packages/gist-scripts\"}");
            WriteProjectFile("Packages/gist-scripts/gists.json.meta", "fileFormatVersion: 2\nguid: aa\n");
            WriteProjectFile("Packages/gist-scripts/package.json", "{\"name\":\"com.gistscripts.gists\"}");
            WriteProjectFile("Packages/gist-scripts/package.json.meta", "fileFormatVersion: 2\nguid: bb\n");
            WriteProjectFile("Packages/gist-scripts/yasirkula.foo/yasirkula.foo.meta", "fileFormatVersion: 2\nguid: cc\n");
            WriteProjectFile("Packages/gist-scripts/yasirkula.foo/Editor.meta", "fileFormatVersion: 2\nguid: dd\n");
            WriteProjectFile("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs", "// foo");
            WriteProjectFile("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs.meta", "fileFormatVersion: 2\nguid: ee\n");
            WriteProjectFile("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.Editor.asmdef", "{\"name\":\"yasirkula.foo.Editor\"}");
            WriteProjectFile("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.Editor.asmdef.meta", "fileFormatVersion: 2\nguid: ff\n");
        }

        // ------------------------------------------------ 清单定位（CombinedManifestPath）

        [Test]
        public void CombineManifestPath_DefaultsToLegacyFixedPath()
        {
            Assert.AreEqual("C:/proj/Packages/gist-scripts/gists.json", GistManifestStore.CombineManifestPath("C:/proj", null));
            Assert.AreEqual("C:/proj/Packages/gist-scripts/gists.json", GistManifestStore.CombineManifestPath("C:/proj", ""));
            Assert.AreEqual("C:/proj/Packages/gist-scripts/gists.json", GistManifestStore.CombineManifestPath("C:/proj", "   "));
        }

        [Test]
        public void CombineManifestPath_UsesRecordedPath()
        {
            Assert.AreEqual("C:/proj/Assets/GistScripts/gists.json", GistManifestStore.CombineManifestPath("C:/proj", "Assets/GistScripts/gists.json"));
        }

        [Test]
        public void CombineManifestPath_IllegalRecordFallsBackToDefault()
        {
            Assert.AreEqual("C:/proj/Packages/gist-scripts/gists.json", GistManifestStore.CombineManifestPath("C:/proj", "../outside/gists.json"));
            Assert.AreEqual("C:/proj/Packages/gist-scripts/gists.json", GistManifestStore.CombineManifestPath("C:/proj", "C:/abs/gists.json"));
        }

        // ------------------------------------------------ 预检（零改动）

        [Test]
        public void Relocate_Blocked_WhenDestinationGistDirNonEmpty_ChangesNothing()
        {
            SeedPackagesRoot();
            WriteProjectFile("Assets/GistScripts/yasirkula.foo/Editor/stale.cs", "// stale");   // 目标 gist 目录非空

            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");
            var result = GistRelocateService.RelocateAt(_root, manifest, "Assets/GistScripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.Blocked, result.Status);
            Assert.IsTrue(result.Blockers.Count > 0);
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs")), "source must stay untouched");
            Assert.IsTrue(File.Exists(Abs("Assets/GistScripts/yasirkula.foo/Editor/stale.cs")), "destination must stay untouched");
            Assert.AreEqual("Packages/gist-scripts", manifest.targetRoot, "manifest must stay untouched");
        }

        [Test]
        public void Relocate_Blocked_WhenDestinationManifestExists_ChangesNothing()
        {
            SeedPackagesRoot();
            WriteProjectFile("Assets/GistScripts/gists.json", "{}");

            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");
            var result = GistRelocateService.RelocateAt(_root, manifest, "Assets/GistScripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.Blocked, result.Status);
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/gists.json")), "source manifest must stay");
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs")));
        }

        [Test]
        public void Relocate_InvalidRoots_Rejected()
        {
            SeedPackagesRoot();
            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");

            foreach (var bad in new[] { "C:/out", "../up", "Assets", "Library/x", "ProjectSettings/x", "Packages" })
            {
                var result = GistRelocateService.RelocateAt(_root, manifest, bad);
                Assert.AreEqual(GistRelocateService.RelocateStatus.InvalidRoot, result.Status, "bad root: " + bad);
            }

            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/gists.json")), "nothing must move on invalid roots");
        }

        // ------------------------------------------------ 搬运（双向）

        [Test]
        public void Relocate_PackagesToAssets_MovesEverything_WithMetasPreserved()
        {
            SeedPackagesRoot();
            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");

            var result = GistRelocateService.RelocateAt(_root, manifest, "Assets/GistScripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.Success, result.Status);
            Assert.Greater(result.MovedFileCount, 0);

            // 源目录树整体消失
            Assert.IsFalse(Directory.Exists(Abs("Packages/gist-scripts/yasirkula.foo")));
            Assert.IsFalse(File.Exists(Abs("Packages/gist-scripts.meta")) || Directory.Exists(Abs("Packages/gist-scripts")));

            // 目标侧内容一致（含 .meta 的 GUID 保留）
            Assert.AreEqual("// foo", File.ReadAllText(Abs("Assets/GistScripts/yasirkula.foo/Editor/yasirkula.foo.cs")));
            Assert.AreEqual("fileFormatVersion: 2\nguid: ee\n", File.ReadAllText(Abs("Assets/GistScripts/yasirkula.foo/Editor/yasirkula.foo.cs.meta")));
            Assert.AreEqual("fileFormatVersion: 2\nguid: ff\n", File.ReadAllText(Abs("Assets/GistScripts/yasirkula.foo/Editor/yasirkula.foo.Editor.asmdef.meta")));
            Assert.AreEqual("{\"name\":\"com.gistscripts.gists\"}", File.ReadAllText(Abs("Assets/GistScripts/package.json")));
            Assert.AreEqual("{\"targetRoot\":\"Packages/gist-scripts\"}", File.ReadAllText(Abs("Assets/GistScripts/gists.json")));

            // 清单内存模型已重定向
            Assert.AreEqual("Assets/GistScripts", manifest.targetRoot);
            Assert.AreEqual("Assets/GistScripts/yasirkula.foo/Editor/yasirkula.foo.cs", manifest.gists[0].files[0].targetPath);
        }

        [Test]
        public void Relocate_AssetsToPackages_CreatesEmbeddedMarker()
        {
            WriteProjectFile("Assets/GistScripts/gists.json", "{\"targetRoot\":\"Assets/GistScripts\"}");
            WriteProjectFile("Assets/GistScripts/yasirkula.foo/Editor/yasirkula.foo.cs", "// foo");
            WriteProjectFile("Assets/GistScripts/yasirkula.foo/Editor/yasirkula.foo.cs.meta", "fileFormatVersion: 2\nguid: ee\n");

            var manifest = MakeManifest("Assets/GistScripts", "yasirkula.foo");
            var result = GistRelocateService.RelocateAt(_root, manifest, "Packages/gist-scripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.Success, result.Status);
            Assert.IsTrue(result.CreatedEmbeddedPackageJson, "no package.json moved along; one must be created");
            StringAssert.Contains("com.gistscripts.gists", File.ReadAllText(Abs("Packages/gist-scripts/package.json")));
            Assert.AreEqual("// foo", File.ReadAllText(Abs("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs")));
            Assert.IsFalse(Directory.Exists(Abs("Assets/GistScripts")));
            Assert.AreEqual("Packages/gist-scripts", manifest.targetRoot);
            Assert.AreEqual("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs", manifest.gists[0].files[0].targetPath);
        }

        [Test]
        public void Relocate_IntoExistingEmptyDir_MergesAndDeletesSourceRoot()
        {
            SeedPackagesRoot();
            WriteProjectFile("Assets/GistScripts/unrelated.txt", "keep me");   // 新根下的无关文件必须保留
            Directory.CreateDirectory(Abs("Assets/GistScripts"));

            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");
            var result = GistRelocateService.RelocateAt(_root, manifest, "Assets/GistScripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.Success, result.Status);
            Assert.AreEqual("keep me", File.ReadAllText(Abs("Assets/GistScripts/unrelated.txt")));
            Assert.AreEqual("// foo", File.ReadAllText(Abs("Assets/GistScripts/yasirkula.foo/Editor/yasirkula.foo.cs")));
        }

        [Test]
        public void Relocate_CleansOldRootCompletelyWhenNothingLeft()
        {
            SeedPackagesRoot();
            WriteProjectFile("Packages/gist-scripts/stray-orphan.meta", "fileFormatVersion: 2\nguid: deadbeef\n");
            WriteProjectFile("Packages/gist-scripts/empty-dir/stray-empty.meta",
                "fileFormatVersion: 2\nguid: deadbee1\n");   // 子目录里的 stray meta 也应当清

            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");
            var result = GistRelocateService.RelocateAt(_root, manifest, "Assets/GistScripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.Success, result.Status);
            // 不应在旧根下残留任何 stray meta；旧根应被整体删除
            Assert.IsFalse(File.Exists(Abs("Packages/gist-scripts/stray-orphan.meta")));
            Assert.IsFalse(Directory.Exists(Abs("Packages/gist-scripts")), "old root fully deleted");
        }

        [Test]
        public void Relocate_WithUserFileAtOldRoot_PreservesItButCleansRest()
        {
            SeedPackagesRoot();
            WriteProjectFile("Packages/gist-scripts/keeper.txt", "user data (not in manifest)");
            WriteProjectFile("Packages/gist-scripts/empty-dir/marker.txt", "x");
            WriteProjectFile("Packages/gist-scripts/stray-orphan.meta", "fileFormatVersion: 2\nguid: deadbeef\n");

            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");
            var result = GistRelocateService.RelocateAt(_root, manifest, "Assets/GistScripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.Success, result.Status);
            // 用户文件不动
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/keeper.txt")));
            // 非 stray 内容也保留（包括用户创建的 empty-dir），但应至少清理掉 stray orphan meta
            Assert.IsFalse(File.Exists(Abs("Packages/gist-scripts/stray-orphan.meta")));
        }

        [Test]
        public void Relocate_EmptySourceRoot_OnlyRewritesManifest()
        {
            // 旧根为空（gist 被禁用且从未落盘）：挪动清单记录即可，不写盘
            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");
            manifest.gists[0].files[0].enabled = false;

            var result = GistRelocateService.RelocateAt(_root, manifest, "Assets/GistScripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.Success, result.Status);
            Assert.AreEqual("Assets/GistScripts", manifest.targetRoot);
            Assert.AreEqual("Assets/GistScripts/yasirkula.foo/Editor/yasirkula.foo.cs", manifest.gists[0].files[0].targetPath);
            Assert.IsFalse(Directory.Exists(Abs("Packages")));
        }

        [Test]
        public void Relocate_SameRoot_IsNoOp()
        {
            SeedPackagesRoot();
            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");

            var result = GistRelocateService.RelocateAt(_root, manifest, "Packages/gist-scripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.Success, result.Status);
            Assert.AreEqual(0, result.MovedFileCount);
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/gists.json")));
        }

        [Test]
        public void Relocate_MovesBackupDirWithRoot()
        {
            SeedPackagesRoot();
            WriteProjectFile("Packages/gist-scripts/Backup~/gists.json.bak", "{\"v\":1}");
            WriteProjectFile("Packages/gist-scripts/Backup~/Disabled/yasirkula.foo/Editor/yasirkula.foo.cs", "// backed up");
            WriteProjectFile("Packages/gist-scripts/Backup~/Disabled/yasirkula.foo/Editor/yasirkula.foo.cs.meta", "fileFormatVersion: 2\nguid: ee\n");

            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");
            var result = GistRelocateService.RelocateAt(_root, manifest, "Assets/GistScripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.Success, result.Status);
            Assert.IsTrue(File.Exists(Abs("Assets/GistScripts/Backup~/gists.json.bak")));
            Assert.AreEqual("// backed up", File.ReadAllText(Abs("Assets/GistScripts/Backup~/Disabled/yasirkula.foo/Editor/yasirkula.foo.cs")));
            Assert.AreEqual("fileFormatVersion: 2\nguid: ee\n", File.ReadAllText(Abs("Assets/GistScripts/Backup~/Disabled/yasirkula.foo/Editor/yasirkula.foo.cs.meta")));
            Assert.IsFalse(Directory.Exists(Abs("Packages/gist-scripts/Backup~")));
        }

        [Test]
        public void Relocate_Blocked_WhenDestinationBackupDirNonEmpty_ChangesNothing()
        {
            SeedPackagesRoot();
            WriteProjectFile("Assets/GistScripts/Backup~/gists.json.bak", "{\"other\":true}");

            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");
            var result = GistRelocateService.RelocateAt(_root, manifest, "Assets/GistScripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.Blocked, result.Status);
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs")), "source must stay untouched");
            Assert.IsTrue(File.Exists(Abs("Assets/GistScripts/Backup~/gists.json.bak")), "destination backup must stay untouched");
            Assert.AreEqual("{\"other\":true}", File.ReadAllText(Abs("Assets/GistScripts/Backup~/gists.json.bak")));
        }

        // ------------------------------------------------ 回滚

        [Test]
        public void Relocate_RollsBack_OnMoveFailure()
        {
            WriteProjectFile("Packages/gist-scripts/gists.json", "{}");
            WriteProjectFile("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs", "// foo");
            WriteProjectFile("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs.meta", "fileFormatVersion: 2\nguid: ee\n");
            WriteProjectFile("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.Editor.asmdef", "{\"name\":\"yasirkula.foo.Editor\"}");

            var manifest = MakeManifest("Packages/gist-scripts", "yasirkula.foo");
            GistRelocateService.FailOnMovingSource = Path.GetFullPath(Abs("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs.meta")).Replace('\\', '/');

            var result = GistRelocateService.RelocateAt(_root, manifest, "Assets/GistScripts");

            Assert.AreEqual(GistRelocateService.RelocateStatus.IoError, result.Status);
            Assert.IsNotNull(result.Error);

            // 全部文件回到源位置，目标目录树不存在，清单 targetRoot 未改
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs")));
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs.meta")));
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.Editor.asmdef")));
            Assert.IsTrue(File.Exists(Abs("Packages/gist-scripts/gists.json")));
            Assert.IsFalse(Directory.Exists(Abs("Assets/GistScripts/yasirkula.foo")));
            Assert.AreEqual("Packages/gist-scripts", manifest.targetRoot);
            Assert.AreEqual("Packages/gist-scripts/yasirkula.foo/Editor/yasirkula.foo.cs", manifest.gists[0].files[0].targetPath);
        }
    }
}
