using NUnit.Framework;

namespace MUD.Modules.GistManager.Tests
{
    public class GistNiceNameTests
    {
        [Test]
        public void CamelCaseWithExtension()
        {
            Assert.AreEqual("Unitypackage Extractor", GistNiceName.ForFilename("UnitypackageExtractor.cs"));
        }

        [Test]
        public void SeparatorAndDigits()
        {
            // lower→digit 按 Unity 行为断词：v→2 变空格
            Assert.AreEqual("Some Utils V 2", GistNiceName.ForFilename("some_utils-v2.cs"));
        }

        [Test]
        public void DotsAndSlashes()
        {
            Assert.AreEqual("My Script", GistNiceName.ForFilename("my.script.cs"));
        }

        [Test]
        public void EmptyOrNull()
        {
            Assert.AreEqual(string.Empty, GistNiceName.ForFilename(string.Empty));
            Assert.AreEqual(string.Empty, GistNiceName.ForFilename(null!));
        }

        [Test]
        public void Utf8Passthrough()
        {
            Assert.AreEqual("中文文件", GistNiceName.ForFilename("中文文件.cs"));
        }
    }
}
