using NUnit.Framework;

namespace MUD.Modules.GistManager.Tests
{
    public class GistUrlParserTests
    {
        [Test]
        public void ParsesFullUrl_WithAuthor()
        {
            bool ok = GistUrlParser.TryParse("https://gist.github.com/yasirkula/dfc43134fbfefb820d0adbc5d7c25fb3",
                out var author, out var id, out var error);
            Assert.IsTrue(ok, error);
            Assert.AreEqual("yasirkula", author);
            Assert.AreEqual("dfc43134fbfefb820d0adbc5d7c25fb3", id);
        }

        [Test]
        public void ParsesAnonymousUrl()
        {
            bool ok = GistUrlParser.TryParse("https://gist.github.com/dfc43134fbfefb820d0adbc5d7c25fb3",
                out var author, out var id, out var error);
            Assert.IsTrue(ok, error);
            Assert.AreEqual("anonymous", author);
            Assert.AreEqual("dfc43134fbfefb820d0adbc5d7c25fb3", id);
        }

        [Test]
        public void ParsesProtocolLessAndFragments()
        {
            bool ok = GistUrlParser.TryParse("gist.github.com/yasirkula/dfc43134fbfefb820d0adbc5d7c25fb3#file-test-cs",
                out var author, out var id, out var error);
            Assert.IsTrue(ok, error);
            Assert.AreEqual("yasirkula", author);
            Assert.AreEqual("dfc43134fbfefb820d0adbc5d7c25fb3", id);
        }

        [Test]
        public void ParsesBareAuthorSlashId()
        {
            bool ok = GistUrlParser.TryParse("yasirkula/dfc43134fbfefb820d0adbc5d7c25fb3",
                out var author, out var id, out var error);
            Assert.IsTrue(ok, error);
            Assert.AreEqual("yasirkula", author);
            Assert.AreEqual("dfc43134fbfefb820d0adbc5d7c25fb3", id);
        }

        [Test]
        public void ParsesBareId_AsAnonymous()
        {
            bool ok = GistUrlParser.TryParse("dfc43134fbfefb820d0adbc5d7c25fb3",
                out var author, out var id, out var error);
            Assert.IsTrue(ok, error);
            Assert.AreEqual("anonymous", author);
            Assert.AreEqual("dfc43134fbfefb820d0adbc5d7c25fb3", id);
        }

        [Test]
        public void RejectsGarbage()
        {
            bool ok = GistUrlParser.TryParse("not a url at all", out _, out _, out var error);
            Assert.IsFalse(ok);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void RejectsEmpty()
        {
            bool ok = GistUrlParser.TryParse("", out _, out _, out var error);
            Assert.IsFalse(ok);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void AnonymousHelper()
        {
            Assert.IsTrue(GistUrlParser.IsAnonymous("anonymous"));
            Assert.IsTrue(GistUrlParser.IsAnonymous(""));
            Assert.IsTrue(GistUrlParser.IsAnonymous(null!));
            Assert.IsFalse(GistUrlParser.IsAnonymous("yasirkula"));
        }

        [Test]
        public void GistWebUrlHandlesAnonymous()
        {
            Assert.AreEqual("https://gist.github.com/dfc43134", GistUrlParser.GistWebUrl("anonymous", "dfc43134"));
            Assert.AreEqual("https://gist.github.com/yasirkula/dfc43134", GistUrlParser.GistWebUrl("yasirkula", "dfc43134"));
        }
    }
}
