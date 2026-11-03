using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace TTVCore.Utilities
{
    internal static class JsonUtil
    {
        // EventSub の時刻はナノ秒まであるため、.NET が扱える7桁に切り詰める
        private static readonly Regex s_fraction = new Regex(@"\.(\d{7})\d+", RegexOptions.Compiled);

        /// <summary>日付の文字列を変換せずに解析する（ナノ秒の時刻を文字列のまま残す）。</summary>
        public static JObject Parse(string text)
        {
            using (var reader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None }) {
                return JObject.Load(reader);
            }
        }

        /// <summary>
        /// 子のオブジェクトを順にたどる。途中がない、JSON の null、オブジェクト以外なら null を返す。
        /// JSON の null は JValue なので、<c>token?["x"]</c> と書くと例外になる。
        /// </summary>
        public static JToken At(JToken token, params string[] keys)
        {
            foreach (var key in keys) {
                if (!(token is JObject obj)) {
                    return null;
                }
                token = obj[key];
            }
            return token;
        }

        public static string Str(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) {
                return null;
            }
            return token.Type == JTokenType.String ? (string)token : token.ToString(Formatting.None);
        }

        /// <summary><see cref="Str"/> と同じだが、空文字は null にする。</summary>
        public static string StrOrNull(JToken token)
        {
            var s = Str(token);
            return string.IsNullOrEmpty(s) ? null : s;
        }

        /// <summary>整数。文字列で届く数値にも対応する。</summary>
        public static int? IntN(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) {
                return null;
            }
            try {
                return (int?)token;
            }
            catch (Exception) {
                return int.TryParse(Str(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int?)null;
            }
        }

        public static int Int(JToken token) => IntN(token) ?? 0;

        /// <summary>真偽値。文字列で届く値にも対応する。</summary>
        public static bool Bool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) {
                return false;
            }
            try {
                return (bool)token;
            }
            catch (Exception) {
                return bool.TryParse(Str(token), out var v) && v;
            }
        }

        /// <summary>RFC 3339 の時刻（ナノ秒を含む）。解析できなければ null。</summary>
        public static DateTimeOffset? Time(JToken token)
        {
            var s = Str(token);
            if (string.IsNullOrEmpty(s)) {
                return null;
            }
            s = s_fraction.Replace(s, ".$1");
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                                           DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var v)
                ? v
                : (DateTimeOffset?)null;
        }
    }
}
