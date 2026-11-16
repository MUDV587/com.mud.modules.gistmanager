using System.IO;
using System.Text;

namespace MUD.Modules.GistManager
{
    /// <summary>
    /// 把 gist 里的原始文件名转成展示用的友好名称：
    ///   UnitypackageExtractor.cs → "Unitypackage Extractor"
    ///   some_utils-v2.cs         → "Some Utils V 2"（lower→digit 按 Unity 行为断词）
    /// 算法与 Unity 的 ObjectNames.NicifyVariableName 对齐（见 Prettify 注释），
    /// 仅保留我们的文件名处理独有部分：扩展名剥离 + 连续分隔符折叠 + CJK 直通。
    /// 只用于 UI 展示，清单与磁盘路径仍用原始文件名。
    /// </summary>
    public static class GistNiceName
    {
        public static string ForFilename(string filename)
        {
            if (string.IsNullOrEmpty(filename))
                return string.Empty;

            var name = filename;
            try
            {
                var withoutExtension = Path.GetFileNameWithoutExtension(filename);
                if (!string.IsNullOrEmpty(withoutExtension))
                    name = withoutExtension;
            }
            catch (System.Exception)
            {
                // 文件名含非法字符时按原样处理
            }

            return Prettify(name);
        }

        private static string Prettify(string name)
        {
            var builder = new StringBuilder(name.Length + 8);
            var startOfWord = true;

            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];

                if (c == ' ' || c == '-' || c == '_' || c == '.' || c == '/' || c == '\\')
                {
                    if (builder.Length > 0 && builder[builder.Length - 1] != ' ')
                        builder.Append(' ');
                    startOfWord = true;
                    continue;
                }

                // 断词规则对齐 Unity ObjectNames.NicifyVariableName：
                // a) prev 为 lower 且 c 为 upper（驼峰边）；prev 为 digit 且 c 为 upper 且 next 为 lower（如 UTF8Utility→UTF8 Utility）
                // b) prev/c 同为 upper 且 next 为 lower（缩写块末尾，如 XMLParser→XML Parser、uGUITools→UGUI Tools）
                // c) prev 为 lower 且 c 为 digit（Unity 行为：Utils2x→Utils 2x；upper→digit 不断词，如 V2/AB2CD）
                if (i > 0 && builder.Length > 0 && builder[builder.Length - 1] != ' ')
                {
                    var prev = name[i - 1];
                    var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                    if (char.IsLower(prev) && char.IsUpper(c))
                        builder.Append(' ');                                  // (a1)
                    else if (char.IsDigit(prev) && char.IsUpper(c) && nextIsLower)
                        builder.Append(' ');                                  // (a2)
                    else if (char.IsUpper(prev) && char.IsUpper(c) && nextIsLower)
                        builder.Append(' ');                                  // (b)
                    else if (char.IsLower(prev) && char.IsDigit(c))
                        builder.Append(' ');                                  // (c)
                }

                if (startOfWord)
                {
                    builder.Append(char.ToUpperInvariant(c));
                    startOfWord = false;
                }
                else
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Trim();
        }

    }
}