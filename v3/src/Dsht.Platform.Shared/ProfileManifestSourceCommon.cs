using System;
using System.Collections.Generic;
using System.IO;
using Dsht.Domain.Abstractions;

namespace Dsht.Platform.Shared
{
    /// <summary>profile 清单来源（Windows/Linux 共用实现 · B-05 平台层去重）：`&lt;数据根&gt;/profiles/&lt;name&gt;/package.json`，只读。
    /// 数据根来自 IPaths（支持 $DSH_HOME）。</summary>
    public abstract class ProfileManifestSourceCommon : IProfileManifestSource
    {
        private readonly string _dataRoot;

        protected ProfileManifestSourceCommon(IPaths paths) { _dataRoot = paths == null ? "" : (paths.DataRoot ?? ""); }

        public string ProfilesRoot { get { return Path.Combine(_dataRoot, "profiles"); } }

        public string[] ListProfiles()
        {
            try
            {
                if (!Directory.Exists(ProfilesRoot)) return new string[0];
                string[] dirs = Directory.GetDirectories(ProfilesRoot);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                List<string> names = new List<string>();
                for (int i = 0; i < dirs.Length; i++) names.Add(Path.GetFileName(dirs[i].TrimEnd('\\', '/')));
                return names.ToArray();
            }
            catch { return new string[0]; }
        }

        public string ReadPatch(string profileName)
        {
            try
            {
                if (string.IsNullOrEmpty(profileName)) return null;
                string p = Path.Combine(Path.Combine(ProfilesRoot, profileName), "cordis.patch.yml");
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }
            catch { return null; }
        }

        public string ReadBundleManifest(string profileName, string bundleId)
        {
            try
            {
                if (string.IsNullOrEmpty(profileName) || string.IsNullOrEmpty(bundleId)) return null;
                string p = Path.Combine(Path.Combine(Path.Combine(ProfilesRoot, profileName), "node_modules"), Path.Combine(bundleId.Split('/')));
                p = Path.Combine(p, "package.json");
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }
            catch { return null; }
        }

        /// <summary>写回补丁文本：备份 → 写 → 复检 → 不一致回滚（与 v2.x 同纪律）。保留原文件的 BOM 状态。</summary>
        public bool ApplyPatch(string profileName, string newText, out string backupPath, out string error)
        {
            backupPath = ""; error = "";
            try
            {
                if (string.IsNullOrEmpty(profileName)) { error = "empty-profile"; return false; }
                string p = Path.Combine(Path.Combine(ProfilesRoot, profileName), "cordis.patch.yml");
                if (!File.Exists(p)) { error = "file-not-found"; return false; }

                byte[] raw = File.ReadAllBytes(p);
                bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
                string original = new System.Text.UTF8Encoding(false).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));

                backupPath = p + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
                File.WriteAllText(backupPath, original, new System.Text.UTF8Encoding(bom));
                File.WriteAllText(p, newText, new System.Text.UTF8Encoding(bom));

                byte[] now = File.ReadAllBytes(p);
                string readBack = new System.Text.UTF8Encoding(false).GetString(now, bom ? 3 : 0, now.Length - (bom ? 3 : 0));
                if (readBack != newText)
                {
                    File.WriteAllText(p, original, new System.Text.UTF8Encoding(bom));   // 回滚
                    error = "verify-failed";
                    return false;
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
        public string ReadManifest(string profileName)
        {
            try
            {
                if (string.IsNullOrEmpty(profileName)) return null;
                string p = Path.Combine(Path.Combine(ProfilesRoot, profileName), "package.json");
                return File.Exists(p) ? File.ReadAllText(p) : null;
            }
            catch { return null; }
        }
    }
}
