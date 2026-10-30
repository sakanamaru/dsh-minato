using System;
using System.IO;
using System.Text;

namespace Dsht.Platform.Shared
{
    /// <summary>B-10：launcher.config 的**原子写** + 失败可观察（Windows/Linux 共用实现）。
    /// 手法：同目录临时文件 → File.Replace（目标已存在）/ File.Move（首次写）——断电/崩溃时旧文件不会被截断；
    /// 失败时清理临时文件并把原因写进 launcher.log（不再 `catch { }` 静默吞）。
    /// ⚠ 本文件也会被 csc（.NET Framework 4.x / C#5）整体编译（compare_markers / verify_switchover 门槛，见 B-17）
    ///   ⇒ 只能用 .NET Framework 也有的 API（File.Replace ✓；File.Move(src,dst,overwrite) 是 .NET Core 才有 ✗）。</summary>
    public static class AtomicConfigFile
    {
        /// <summary>原子写配置；返回是否成功。成功时目标文件要么是旧内容、要么是完整新内容（不会是被截断的半截）。</summary>
        public static bool WriteAllText(string configPath, string text, Encoding encoding, string stateDir)
        {
            string tmp = null;
            try
            {
                // 与目标同目录 ⇒ 同卷 ⇒ File.Move/Replace 是原子 rename ✓（跨卷才会退化成拷贝 ✗）
                tmp = configPath + ".tmp-" + Guid.NewGuid().ToString("N");
                File.WriteAllText(tmp, text, encoding);
                if (File.Exists(configPath)) File.Replace(tmp, configPath, null);
                else File.Move(tmp, configPath);
                tmp = null;
                return true;
            }
            catch (Exception ex)
            {
                if (tmp != null) { try { File.Delete(tmp); } catch { } }
                TryLog(stateDir, "ERROR", "config-write-failed path=" + (configPath ?? "") + " => " + Describe(ex) + "（临时文件已清理；原配置未被改动）");
                return false;
            }
        }

        /// <summary>读失败的原因落 launcher.log（ReadConfig 的 string 返回值/接口签名保持不变）。</summary>
        public static void LogReadFailure(string stateDir, string configPath, Exception ex)
        {
            TryLog(stateDir, "ERROR", "config-read-failed path=" + (configPath ?? "") + " => " + Describe(ex));
        }

        /// <summary>尽力而为写一行到 状态目录/logs/launcher.log —— 日志自身失败不影响主操作（同 ILogSource.Append 的纪律）。</summary>
        public static void TryLog(string stateDir, string level, string message)
        {
            try
            {
                string baseDir = string.IsNullOrEmpty(stateDir) ? AppDomain.CurrentDomain.BaseDirectory : stateDir;
                string dir = Path.Combine(baseDir, "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "launcher.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " " + level + " " + message + Environment.NewLine);
            }
            catch { }
        }

        private static string Describe(Exception ex)
        {
            if (ex == null) return "unknown";
            string inner = ex.InnerException == null ? "" : (" / inner=" + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message);
            return ex.GetType().Name + ": " + ex.Message + inner;
        }
    }
}
