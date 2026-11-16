using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MUD.Modules.GistManager.Tests
{
    public class GistCatalogTests
    {
        private static GistMetadata MakeMetadata(string id, string author, string description, params string[] filenames)
        {
            var metadata = new GistMetadata
            {
                Id = id,
                Author = author,
                Description = description
            };
            foreach (var f in filenames)
                metadata.Files.Add(new GistFileMetadata { Filename = f, RawUrl = "https://raw.example.com/" + id + "/" + f });
            return metadata;
        }

        [Test]
        public void AddOrUpdateCreatesEntryAndPackageDir()
        {
            var manifest = new GistManifest();
            var entry = GistCatalog.AddOrUpdate(manifest,
                MakeMetadata("abc123", "yasirkula", "Unitypackage Extractor", "UnitypackageExtractor.cs"));

            Assert.AreEqual(1, manifest.gists.Count);
            Assert.AreEqual("abc123", entry.id);
            Assert.AreEqual("yasirkula", entry.author);
            Assert.AreEqual(1, entry.files.Count);
            Assert.AreEqual("yasirkula.unitypackage-extractor", entry.packageDir);
            Assert.AreEqual("Packages/gist-scripts/yasirkula.unitypackage-extractor/UnitypackageExtractor.cs",
                entry.files[0].targetPath);
            Assert.IsTrue(entry.files[0].enabled);
        }

        [Test]
        public void AddOrUpdateIsIdempotent()
        {
            var manifest = new GistManifest();
            var first = GistCatalog.AddOrUpdate(manifest,
                MakeMetadata("abc123", "yasirkula", "Unitypackage Extractor", "UnitypackageExtractor.cs"));
            var second = GistCatalog.AddOrUpdate(manifest,
                MakeMetadata("abc123", "yasirkula", "Unitypackage Extractor (updated)", "UnitypackageExtractor.cs"));

            Assert.AreSame(first, second);
            Assert.AreEqual(1, manifest.gists.Count);
        }

        [Test]
        public void AddOrUpdatePreservesUserDisabledFlagAndPath()
        {
            var manifest = new GistManifest();
            var first = GistCatalog.AddOrUpdate(manifest,
                MakeMetadata("abc123", "yasirkula", "Unitypackage Extractor", "UnitypackageExtractor.cs"));
            first.files[0].enabled = false;
            first.files[0].targetPath = "Packages/gist-scripts/custom/CustomPath.cs";

            var second = GistCatalog.AddOrUpdate(manifest,
                MakeMetadata("abc123", "yasirkula", "Unitypackage Extractor", "UnitypackageExtractor.cs"));

            Assert.AreSame(first, second);
            Assert.IsFalse(second.files[0].enabled, "用户禁用状态不应被覆盖");
            Assert.AreEqual("Packages/gist-scripts/custom/CustomPath.cs", second.files[0].targetPath, "用户自定义路径不应被覆盖");
        }

        [Test]
        public void RetargetMigratesTargetPaths()
        {
            var manifest = new GistManifest();
            GistCatalog.AddOrUpdate(manifest,
                MakeMetadata("abc123", "yasirkula", "x", "UnitypackageExtractor.cs"));
            GistCatalog.Retarget(manifest, "Assets/GistScripts");

            Assert.AreEqual("Assets/GistScripts", manifest.targetRoot);
            Assert.AreEqual("Assets/GistScripts/yasirkula.unitypackage-extractor/UnitypackageExtractor.cs",
                manifest.gists[0].files[0].targetPath);
        }

        [Test]
        public void NormalizeFillsMissingTargetPath()
        {
            var manifest = new GistManifest
            {
                gists = new List<GistEntry>
                {
                    new GistEntry
                    {
                        id = "abc123",
                        author = "yasirkula",
                        packageDir = "yasirkula.unitypackage-extractor",
                        files = new List<GistFileEntry>
                        {
                            new GistFileEntry { filename = "A.cs" }
                        }
                    }
                }
            };

            var changed = GistCatalog.Normalize(manifest);
            Assert.IsTrue(changed);
            Assert.AreEqual("Packages/gist-scripts/yasirkula.unitypackage-extractor/A.cs",
                manifest.gists[0].files[0].targetPath);
        }

        [Test]
        public void PackageDirConflictGetsIdSuffix()
        {
            var manifest = new GistManifest();
            var meta1 = MakeMetadata("a1b2c3d4e5f6", "yasirkula", "Extractor", "UnitypackageExtractor.cs");
            var meta2 = MakeMetadata("f6e5d4c3b2a1", "yasirkula", "Extractor", "UnitypackageExtractor.cs");

            var one = GistCatalog.AddOrUpdate(manifest, meta1);
            var two = GistCatalog.AddOrUpdate(manifest, meta2);

            Assert.AreEqual("yasirkula.unitypackage-extractor", one.packageDir);
            StringAssert.StartsWith("yasirkula.unitypackage-extractor", two.packageDir);
            Assert.AreNotEqual(one.packageDir, two.packageDir);
        }
    }
}
