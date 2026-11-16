using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MUD.Modules.GistManager.Tests
{
    public class GistManifestStoreTests
    {
        [Test]
        public void SaveAndLoadRoundtrip()
        {
            var manifest = new GistManifest
            {
                targetRoot = "Packages/gist-scripts"
            };
            manifest.gists.Add(new GistEntry
            {
                id = "abc123",
                author = "yasirkula",
                description = "d",
                packageDir = "yasirkula.foo",
                files =
                {
                    new GistFileEntry
                    {
                        filename = "a.cs",
                        targetPath = "Packages/gist-scripts/yasirkula.foo/a.cs",
                        enabled = true,
                        rawUrl = "https://example.com/a",
                        meta = "",
                        placement = "Editor",
                        sha256 = "deadbeef"
                    }
                }
            });

            // 写到临时项目根路径：这个测试不会真的在工程根目录产生文件（直接对内存模型断言不更好，验证往返）
            var json = JsonUtility.ToJson(manifest, prettyPrint: true);
            var loaded = JsonUtility.FromJson<GistManifest>(json);

            Assert.AreEqual("yasirkula.foo", loaded.gists[0].packageDir);
            Assert.AreEqual("deadbeef", loaded.gists[0].files[0].sha256);
            Assert.IsTrue(loaded.gists[0].files[0].enabled);
        }

        [Test]
        public void IsSafeProjectRelative_RejectsBadPaths()
        {
            Assert.IsFalse(GistManifestStore.IsSafeProjectRelative("../escape.cs"));
            Assert.IsFalse(GistManifestStore.IsSafeProjectRelative("/absolute/path/file.cs"));
            Assert.IsFalse(GistManifestStore.IsSafeProjectRelative("C:/abs/path/file.cs"));
            Assert.IsFalse(GistManifestStore.IsSafeProjectRelative("Assets/../outside.cs"));
            Assert.IsTrue(GistManifestStore.IsSafeProjectRelative("Assets/gists/a.cs"));
            Assert.IsTrue(GistManifestStore.IsSafeProjectRelative("Packages/gist-scripts/yasirkula.foo/a.cs"));
        }

        [Test]
        public void NormalizeRelativeTrimsAndForwardSlashes()
        {
            Assert.AreEqual("Assets/gists/a.cs", GistManifestStore.NormalizeRelative("Assets\\gists/a.cs"));
            Assert.AreEqual(string.Empty, GistManifestStore.NormalizeRelative(""));
        }

        [Test]
        public void Sha256HexIsLowercaseAndStable()
        {
            var a = GistHash.Sha256Hex(new byte[] { 1, 2, 3 });
            var b = GistHash.Sha256Hex(new byte[] { 1, 2, 3 });
            var c = GistHash.Sha256Hex(new byte[] { 1, 2, 4 });

            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
            Assert.AreEqual(64, a.Length);
            Assert.AreEqual(a, a.ToLowerInvariant());
        }
    }
}
