using System;
using System.Text;

namespace Dsht.Domain.Services
{
    /// <summary>package.json 的**字段读取**（纯函数 ✓ 零 IO ✓ 零第三方依赖 ✓ —— 因此留在 Domain 层 ✓）。
    ///
    /// ★ 为什么单独抽成一个共享件（2026-10-13）：更新中心要显示"这个插件来自哪个仓库" ✓
    ///   而 `repository` 在真实 package.json 里有**两种都合法**的写法：
    ///     · 字符串：`"repository": "owner/repo"`（或整条 URL ✓）
    ///     · 对象  ：`"repository": { "type": "git", "url": "https://…", "directory": "plugin/x" }` ✓
    ///   ✗ 旧代码直接取"键之后的第一个引号串" ✗ → 对**对象**形态拿到的是对象里的第一把**键**（`type` ✗）
    ///     → 更新页把字面值 `type` 当仓库地址显示出来 ✗（同一个 package.json，CLI 一个答案、眼睛一个答案 ✓）
    ///   ✓ 现在：三种形态（对象 / 字符串 / 缺失）**只有这一处判定** ✓ —— CLI 与 GUI 单测都调它 ✓
    ///     ✗ 谁都不许再写第二份 ✗（两份就会有两个答案 —— 这个 bug 正是这么来的 ✓）。
    ///
    /// 纪律：读不出来就返回**空串** ✓（调用方写 `unknown` ✓）；**绝不猜** ✗ 不编默认值 ✗ 不改写内容 ✗。</summary>
    public static class PackageJsonFields
    {
        /// <summary>`repository` 的显示用地址：
        /// · 对象 → 取**对象内**的 `url` ✓（`directory` / `type` 不是地址 ✗）
        /// · 字符串 → **原样**返回 ✓（不补 `.git`、不加前缀、不改写 ✓）
        /// · 缺失 / 形态不对 / 对象里没有 url → 空串 ✓（不编 ✗）</summary>
        public static string RepoUrl(string packageJson)
        {
            string raw = RawValueAt(packageJson, "repository");
            if (raw == null) return "";
            if (raw.Length > 0 && raw[0] == '{') return (StringValueOf(raw, "url") ?? "").Trim();
            return raw.Trim();
        }

        /// <summary>`name` 的字符串值（读不到 → 空串 ✓ 不编 ✗）。</summary>
        public static string Name(string packageJson)
        {
            return StringValueOf(packageJson, "name") ?? "";
        }

        /// <summary>`version` 的字符串值（读不到 → 空串 ✓ 不编 ✗）。</summary>
        public static string Version(string packageJson)
        {
            return StringValueOf(packageJson, "version") ?? "";
        }

        /// <summary>任意键后面的**原始值片段**（JSON 形态的公开入口 ✓）：
        /// 字符串 → 字符串内容 ✓；对象 → `{` 到配对 `}` 的原文 ✓；其它 / 缺失 → null ✓（不猜 ✗）。
        /// ★ 2026-10-26 插件资格判定要读 `dsh` 对象本身（再由 `ObjectHasKey` 看它有没有 bundle/client 顶层键），
        ///   复用的仍是**同一份**取值/配对扫描 ✓（✗ 不在调用方再写一份解析 ✗）。</summary>
        public static string RawField(string packageJson, string key)
        {
            return RawValueAt(packageJson, key);
        }

        /// <summary>对象**顶层**是否含某个键（键名精确匹配 ✓；嵌套对象里的同名键**不算** ✓）。
        /// 用途：`dependencies` 的键集合、`dsh` 对象里的 `bundle` / `client` —— 都必须只看**这一层**，
        /// 否则 `{"dsh":{"note":{"bundle":1}}}` 会被误判成声明了 dsh.bundle ✗。
        /// 输入不是对象 / 读不到 → false ✓（不猜 ✗ 不抛 ✗）。</summary>
        public static bool ObjectHasKey(string jsonObject, string key)
        {
            if (string.IsNullOrEmpty(jsonObject) || string.IsNullOrEmpty(key)) return false;
            int start = 0;
            while (start < jsonObject.Length && IsJsonSpace(jsonObject[start])) start++;
            if (start >= jsonObject.Length || jsonObject[start] != '{') return false;
            bool inStr = false;
            int depth = 0;
            for (int i = start; i < jsonObject.Length; i++)
            {
                char ch = jsonObject[i];
                if (inStr)
                {
                    if (ch == '\\') { i++; continue; }
                    if (ch == '"') inStr = false;
                    continue;
                }
                if (ch == '"')
                {
                    if (depth == 1)
                    {
                        int end;
                        string s = ReadStringAt(jsonObject, i, out end);
                        if (s != null)
                        {
                            int c = end + 1;
                            while (c < jsonObject.Length && IsJsonSpace(jsonObject[c])) c++;
                            // 顶层字符串后面跟 `:` → 它是**键** ✓；否则是值 → 只跳过（不能把值当键 ✗）
                            if (c < jsonObject.Length && jsonObject[c] == ':' && string.Equals(s, key, StringComparison.Ordinal))
                                return true;
                            i = end;   // 跳过整个字符串，避免值里的 `{` / `}` / 引号干扰结构扫描 ✓
                            continue;
                        }
                    }
                    inStr = true;
                    continue;
                }
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0) break;
                }
            }
            return false;
        }

        /// <summary>某个键后面的**原始值片段**：字符串 → 字符串内容 ✓；对象 → `{` 到**配对的** `}` 的原文 ✓；
        /// 其它（数字 / 布尔 / null / 数组）/ 找不到 → null ✓（不猜 ✗）。
        /// 键名必须**整词**匹配（前一个非空白字符是 `{` 或 `,` ✓）→ `repositoryUrl` 不会被当成 `repository` ✓。</summary>
        private static string RawValueAt(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
            string needle = "\"" + key + "\"";
            int from = 0;
            while (true)
            {
                int at = json.IndexOf(needle, from, StringComparison.Ordinal);
                if (at < 0) return null;
                from = at + needle.Length;
                int j = at - 1;
                while (j >= 0 && IsJsonSpace(json[j])) j--;
                if (j >= 0 && json[j] != '{' && json[j] != ',') continue;   // 不是独立的键 → 继续往后找 ✓
                int c = from;
                while (c < json.Length && IsJsonSpace(json[c])) c++;
                if (c >= json.Length || json[c] != ':') continue;
                c++;
                while (c < json.Length && IsJsonSpace(json[c])) c++;
                if (c >= json.Length) return null;
                if (json[c] == '"') return StringValueAt(json, c);
                if (json[c] == '{')
                {
                    // 配对扫描：只在**字符串外**数括号 ✓（值里的 `{` / `}` 不算结构 ✓）
                    int depth = 0; bool inStr = false;
                    for (int k = c; k < json.Length; k++)
                    {
                        char ch = json[k];
                        if (inStr)
                        {
                            if (ch == '\\') { k++; continue; }
                            if (ch == '"') inStr = false;
                            continue;
                        }
                        if (ch == '"') { inStr = true; continue; }
                        if (ch == '{') depth++;
                        else if (ch == '}')
                        {
                            depth--;
                            if (depth == 0) return json.Substring(c, k - c + 1);
                        }
                    }
                    return null;   // 括号不配对 = 读不出来 ✓ 不编 ✗
                }
                return null;       // 不是字符串也不是对象 → 不是我们要的形态 ✓ 不猜 ✗
            }
        }

        /// <summary>`key` 的字符串值（找不到 / 值不是字符串 → null ✓）。</summary>
        private static string StringValueOf(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
            int at = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (at < 0) return null;
            int c = at + key.Length + 2;
            while (c < json.Length && IsJsonSpace(json[c])) c++;
            if (c >= json.Length || json[c] != ':') return null;
            c++;
            while (c < json.Length && IsJsonSpace(json[c])) c++;
            if (c >= json.Length || json[c] != '"') return null;
            return StringValueAt(json, c);
        }

        /// <summary>读 `json[q1]`（必须是开引号 ✓）起始的字符串内容 ✓ **含转义**（`\"` `\\` `\uXXXX` ✓）。
        /// 找不到收尾引号 → null ✓（不编 ✗）。</summary>
        private static string StringValueAt(string json, int q1)
        {
            int end;
            return ReadStringAt(json, q1, out end);
        }

        /// <summary>读 `json[q1]`（必须是开引号 ✓）起始的 JSON 字符串，并给出**收尾引号**的下标 ✓。
        /// 内容按 JSON 转义还原（`\"` `\\` `\uXXXX` ✓）；找不到收尾引号 → null（`end` 原样）✓ 不编 ✗。</summary>
        private static string ReadStringAt(string json, int q1, out int end)
        {
            end = q1;
            if (json == null || q1 < 0 || q1 >= json.Length || json[q1] != '"') return null;
            StringBuilder sb = new StringBuilder();
            for (int i = q1 + 1; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '\\')
                {
                    if (i + 1 >= json.Length) return null;
                    char e = json[++i];
                    if (e == 'n') sb.Append('\n');
                    else if (e == 't') sb.Append('\t');
                    else if (e == 'r') sb.Append('\r');
                    else if (e == 'u')
                    {
                        if (i + 4 >= json.Length) return null;
                        int code;
                        if (!int.TryParse(json.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out code)) return null;
                        sb.Append((char)code);
                        i += 4;
                    }
                    else sb.Append(e);   // `\"` / `\\` / `\/` 等 → 字面字符 ✓
                    continue;
                }
                if (ch == '"') { end = i; return sb.ToString(); }
                sb.Append(ch);
            }
            return null;
        }

        private static bool IsJsonSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\r' || c == '\n';
        }
    }
}
