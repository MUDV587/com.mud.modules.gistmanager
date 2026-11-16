using System.Collections.Generic;
using NUnit.Framework;

namespace MUD.Modules.GistManager.Tests
{
    public class GistSearchTests
    {
        private static GistEntry MakeEntry() => new GistEntry
        {
            id = "dfc43134",
            author = "Yasirkula",
            description = "Find Textures with only a single color",
            packageDir = "yasirkula.single-color-texture-detector",
            files = new List<GistFileEntry>
            {
                new GistFileEntry
                {
                    filename = "SingleColorTextureDetector.cs",
                    targetPath = "Packages/gist-scripts/yasirkula.single-color-texture-detector/Editor/SingleColorTextureDetector.cs",
                    enabled = true
                }
            }
        };

        [Test]
        public void MatchesAuthorCaseInsensitive()
        {
            Assert.IsTrue(GistSearch.Matches(MakeEntry(), "YASIRKULA"));
        }

        [Test]
        public void MatchesIdCaseInsensitive()
        {
            Assert.IsTrue(GistSearch.Matches(MakeEntry(), "dfc43134"));
        }

        [Test]
        public void MatchesDescription()
        {
            Assert.IsTrue(GistSearch.Matches(MakeEntry(), "SINGLE COLOR"));
        }

        [Test]
        public void MatchesFilename()
        {
            Assert.IsTrue(GistSearch.Matches(MakeEntry(), "singlecolortexturedetector"));
        }

        [Test]
        public void MatchesTargetPath()
        {
            Assert.IsTrue(GistSearch.Matches(MakeEntry(), "texture-detector/editor/"));
        }

        [Test]
        public void DoesNotMatchUnrelated()
        {
            Assert.IsFalse(GistSearch.Matches(MakeEntry(), "nonexistent-needle"));
        }

        [Test]
        public void NullQueryMatches()
        {
            Assert.IsTrue(GistSearch.Matches(MakeEntry(), null));
        }

        [Test]
        public void NullEntryDoesNotMatch()
        {
            Assert.IsFalse(GistSearch.Matches(null, "anything"));
        }

        [Test]
        public void NoResultsMessageWithoutQuery()
        {
            Assert.IsNotEmpty(GistSearch.NoResultsMessage(""));
        }

        [Test]
        public void NoResultsMessageWithQueryEcho()
        {
            var msg = GistSearch.NoResultsMessage("needle");
            StringAssert.Contains("needle", msg);
        }

        [Test]
        public void TruncateLongQuery()
        {
            var longInput = new string('a', 100);
            var truncated = GistSearch.Truncate(longInput);
            Assert.LessOrEqual(truncated.Length, GistSearch.MaxDisplayQueryLength + 3);
            StringAssert.EndsWith("...", truncated);
        }
    }
}
