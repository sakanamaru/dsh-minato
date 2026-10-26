using System;

namespace Dsht.Domain.Services
{
    /// <summary>node_modules 里的一个条目**是不是 dsh 插件**（纯判定 ✓ 零 IO ✓ 零第三方依赖 ✓）。
    ///
    /// ★ 为什么需要它（2026-10-26 真机缺陷）：更新中心的插件枚举原来只要求 `package.json`
    ///   存在 / 可读 / 非空 ✗ —— 于是**纯依赖**（zod 4.4.3）也被当插件输出 ✓，而 `@scope/包`
    ///   根本不在枚举范围里 ✗、读不到 manifest 的条目被**静默跳过** ✗（漏报比误报更危险 ✓）。
    ///
    /// 资格 = **三选一** ✓（任一成立即算插件 ✓）：
    ///   ① manifest 的 `dsh` 对象里声明了 `bundle` 或 `client`（顶层键 ✓ 嵌套同名键不算 ✗）
    ///   ② 包名出现在 profile `package.json` 的 `dependencies` 里（兜底 ✓ —— 有些真插件不写 dsh 声明 ✓）
    ///   ③ 包名在 profile 的 `cordis.patch.yml` / 配置里被**明确引用**（整词边界 ✓ 不拿子串冒充 ✓）
    ///
    /// 三类结果（调用方必须分开计数 ✓ 谁都不许静默丢 ✗）：
    ///   `Plugin` = 真插件 ✓ 照常出 ITEM 行 ✓
    ///   `Filtered` = 读过 manifest、判定为非插件 → 汇总成 `plugins-filtered n=N` ✓
    ///   `Unreadable` = manifest 不存在 / 读失败 / 空 → 汇总成 `plugins-unreadable n=N` ✓（✗ 不混进 filtered ✗）
    ///
    /// 纪律：读不到就 Unreadable ✓；判定不了不猜成插件 ✗；也不抛 ✗。</summary>
    public enum PluginEntryKind
    {
        Plugin = 0,
        Filtered = 1,
        Unreadable = 2
    }

    public static class PluginEligibility
    {
        /// <summary>① manifest 是否**顶层**声明了 `dsh.bundle` 或 `dsh.client` ✓。
        /// `dsh` 必须是对象 ✓；`bundle` / `client` 必须是 `dsh` 对象的**顶层键** ✓
        /// （`{"dsh":{"note":{"bundle":1}}}` 不算 ✗）。</summary>
        public static bool DeclaresDshExtension(string packageJson)
        {
            string dsh = PackageJsonFields.RawField(packageJson, "dsh");
            if (string.IsNullOrEmpty(dsh) || dsh[0] != '{') return false;
            return PackageJsonFields.ObjectHasKey(dsh, "bundle")
                || PackageJsonFields.ObjectHasKey(dsh, "client");
        }

        /// <summary>② 包名是否是 profile `package.json` 的 `dependencies` 顶层键 ✓。
        /// 只看 `dependencies` ✓（`devDependencies` / 嵌套对象不算 ✗ —— 那是开发依赖/传递依赖的语义）。</summary>
        public static bool ListedInDependencies(string profilePackageJson, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string deps = PackageJsonFields.RawField(profilePackageJson, "dependencies");
            if (string.IsNullOrEmpty(deps) || deps[0] != '{') return false;
            return PackageJsonFields.ObjectHasKey(deps, name);
        }

        /// <summary>③ 包名是否在 profile 配置文本里被**整词**引用 ✓。
        /// 边界字符按 npm 包名允许的字符集算（字母/数字/`_`/`-`/`.`/`/`/`@`/`~`）→
        ///   `@deepseek-ai/dsh-subagent-codex` 里的 `codex` **不算**引用了 `codex` ✓（真机陷阱 ✓）。
        /// 逐次 `IndexOf` 找下一个出现位置 ✓ 不编译正则 ✓（包名里的 `.` / `+` 之类不会被当成元字符 ✗）。</summary>
        public static bool ReferencedInConfig(string configText, string name)
        {
            if (string.IsNullOrEmpty(configText) || string.IsNullOrEmpty(name)) return false;
            int from = 0;
            while (true)
            {
                int at = configText.IndexOf(name, from, StringComparison.Ordinal);
                if (at < 0) return false;
                from = at + name.Length;
                bool leftOk = at == 0 || !IsPackageNameChar(configText[at - 1]);
                int end = at + name.Length;
                bool rightOk = end >= configText.Length || !IsPackageNameChar(configText[end]);
                if (leftOk && rightOk) return true;
            }
        }

        /// <summary>三选一资格判定（唯一入口 ✓ CLI 与测试都走它 ✓）。
        /// `manifestReadable=false` / manifest 为空 / 连 JSON 对象骨架都没有 → `Unreadable` ✓
        /// （**不**算 filtered ✗ —— 读失败要单独让用户看见 ✓）。</summary>
        public static PluginEntryKind Classify(string name, bool manifestReadable, string manifest, string profilePackageJson, string configText)
        {
            if (!manifestReadable || string.IsNullOrEmpty(manifest)) return PluginEntryKind.Unreadable;
            // 非空但连对象骨架都没有 = 内容坏了 → 同样按「读不到」处理 ✓（✗ 不静默当 filtered ✗）
            if (manifest.IndexOf('{') < 0 || manifest.IndexOf('}') < 0) return PluginEntryKind.Unreadable;
            if (string.IsNullOrEmpty(name)) return PluginEntryKind.Filtered;
            if (DeclaresDshExtension(manifest)) return PluginEntryKind.Plugin;
            if (ListedInDependencies(profilePackageJson, name)) return PluginEntryKind.Plugin;
            if (ReferencedInConfig(configText, name)) return PluginEntryKind.Plugin;
            return PluginEntryKind.Filtered;
        }

        /// <summary>npm 包名里可能出现的字符（用于整词边界 ✓）。`/` 与 `@` 必须算在内 ✓
        /// —— 否则 `@scope/pkg` 会在 `/` 处被截断，`pkg` 就能冒充整词 ✗。</summary>
        private static bool IsPackageNameChar(char c)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) return true;
            return c == '_' || c == '-' || c == '.' || c == '/' || c == '@' || c == '~';
        }
    }
}
