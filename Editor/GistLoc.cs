using UnityEditor;
using UnityEngine;

namespace MUD.Modules.GistManager
{
    /// <summary>
    /// 中英双语文案。语言存于 ProjectSettings/GistManager.asset（由 <see cref="GistManagerSettings"/> 管理，可提交进版本库），
    /// 默认跟随系统语言（系统为中文* 时使用中文，否则 English）。
    /// * 只要 <see cref="Application.systemLanguage"/> 以 "Chinese" 开头即视为中文（Chinese/ChineseSimplified/ChineseTraditional）。
    /// 用法：GistLoc.T("中文", "English")。
    /// </summary>
    public static class GistLoc
    {
        public enum GistLanguage
        {
            Auto = 0,
            Zh = 1,
            En = 2
        }

        public static GistLanguage GetLanguage()
        {
            var settings = GistManagerSettings.instance;
            switch (settings.language)
            {
                case GistManagerSettings.LanguageZh: return GistLanguage.Zh;
                case GistManagerSettings.LanguageEn: return GistLanguage.En;
                default: return GistLanguage.Auto;
            }
        }

        public static void SetLanguage(GistLanguage language)
        {
            var settings = GistManagerSettings.instance;
            var value = language == GistLanguage.Zh ? GistManagerSettings.LanguageZh
                : language == GistLanguage.En ? GistManagerSettings.LanguageEn
                : GistManagerSettings.LanguageAuto;
            if (settings.language != value)
            {
                settings.language = value;
                settings.Save();
            }
        }

        public static bool SystemIsChinese =>
            Application.systemLanguage.ToString().StartsWith("Chinese", System.StringComparison.OrdinalIgnoreCase);

        public static bool IsChinese
        {
            get
            {
                var lang = GetLanguage();
                if (lang == GistLanguage.Zh) return true;
                if (lang == GistLanguage.En) return false;
                return SystemIsChinese;
            }
        }

        public static string T(string zh, string en) => IsChinese ? zh : en;
    }
}
