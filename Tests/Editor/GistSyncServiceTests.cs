using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MUD.Modules.GistManager.Tests
{
    public class GistSyncServiceTests
    {
        [Test]
        public void Sha256Hex_ReadDiskFile_NotModified()
        {
            var rel = "Temp/OGistSyncTests_" + System.Guid.NewGuid().ToString("N") + "/a.txt";
            var full = GistManifestStore.ToFullPath(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            try
            {
                var content = System.Text.Encoding.UTF8.GetBytes("hello gist");
                File.WriteAllBytes(full, content);

                var file = new GistFileEntry
                {
                    filename = "a.txt",
                    targetPath = rel,
                    sha256 = GistHash.Sha256Hex(content)
                };

                Assert.IsFalse(GistSyncService.IsLocallyModified(file));
            }
            finally
            {
                if (Directory.Exists(Path.GetDirectoryName(full)))
                    Directory.Delete(Path.GetDirectoryName(full), true);
            }
        }

        [Test]
        public void Sha256Hex_DiskDiffersFromRecorded_Modified()
        {
            var rel = "Temp/OGistSyncTests_" + System.Guid.NewGuid().ToString("N") + "/a.txt";
            var full = GistManifestStore.ToFullPath(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            try
            {
                File.WriteAllText(full, "本地手改", new System.Text.UTF8Encoding(false));

                var file = new GistFileEntry
                {
                    filename = "a.txt",
                    targetPath = rel,
                    sha256 = GistHash.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("upstream 原始下载内容"))
                };

                Assert.IsTrue(GistSyncService.IsLocallyModified(file));
            }
            finally
            {
                if (Directory.Exists(Path.GetDirectoryName(full)))
                    Directory.Delete(Path.GetDirectoryName(full), true);
            }
        }

        [Test]
        public void IsLocallyModified_EmptyRecordedSha_TreatedAsPristine()
        {
            var file = new GistFileEntry { filename = "a.txt", targetPath = "Temp/nonexistent.txt", sha256 = "" };
            Assert.IsFalse(GistSyncService.IsLocallyModified(file));
        }

        [Test]
        public void IsLocallyModified_MissingFile_TreatedAsPristine()
        {
            var file = new GistFileEntry { filename = "a.txt", targetPath = "Temp/nonexistent-" + System.Guid.NewGuid().ToString("N") + ".txt", sha256 = "anything" };
            Assert.IsFalse(GistSyncService.IsLocallyModified(file));
        }
    }
}
