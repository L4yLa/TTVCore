using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TTVCore.Utilities
{
    /// <summary>UI の言語。</summary>
    internal enum UiLanguage
    {
        /// <summary>OS の言語が日本語なら日本語、それ以外は英語。</summary>
        System,
        English,
        Japanese,
    }

    /// <summary>
    /// 表示する文言の言語を切り替える。文言は英語と日本語を並べて書く。
    /// BSML では {{English|日本語}} と書き、読み込むときに選んだ方へ置き換える。
    /// </summary>
    internal static class Localization
    {
        private static readonly Regex Token = new Regex(@"\{\{(.*?)\|(.*?)\}\}", RegexOptions.Singleline);
        private static bool s_applied;

        /// <summary>日本語で表示するか。</summary>
        public static bool IsJapanese { get; private set; }

        /// <summary>言語が変わるたびに増える。作った画面がどの言語のものかを見分けるのに使う。</summary>
        public static int Version { get; private set; }

        /// <summary>言語が変わった。</summary>
        public static event Action Changed;

        public static void Apply(UiLanguage language)
        {
            var japanese = Resolve(language);
            if (s_applied && japanese == IsJapanese) {
                return;
            }
            s_applied = true;
            IsJapanese = japanese;
            Version++;
            try {
                Changed?.Invoke();
            }
            catch (Exception e) {
                Plugin.Log.Error($"Localization: {e}");
            }
        }

        public static bool Resolve(UiLanguage language) =>
            language == UiLanguage.Japanese || (language == UiLanguage.System && Application.systemLanguage == SystemLanguage.Japanese);

        public static string T(string english, string japanese) => IsJapanese ? japanese : english;

        /// <summary>BSML の {{English|日本語}} を選んだ言語の文言に置き換える。</summary>
        public static string Bsml(string content) =>
            string.IsNullOrEmpty(content) ? content : Token.Replace(content, m => IsJapanese ? m.Groups[2].Value : m.Groups[1].Value);
    }
}
