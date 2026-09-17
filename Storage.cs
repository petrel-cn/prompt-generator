using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace PromptGenerator
{
    /// <summary>窗口几何信息。</summary>
    public class WindowConfig
    {
        public int x { get; set; }
        public int y { get; set; }
        public int width { get; set; }
        public int height { get; set; }

        public WindowConfig()
        {
            x = -1;
            y = -1;
            width = Defaults.WindowWidth;
            height = Defaults.WindowHeight;
        }

        /// <summary>坐标是否已设置（-1 表示未设置，首次启动应居中）。</summary>
        [ScriptIgnore]
        public bool HasPosition
        {
            get { return x != -1 && y != -1; }
        }

        public WindowConfig Clone()
        {
            WindowConfig copy = new WindowConfig();
            copy.x = x;
            copy.y = y;
            copy.width = width;
            copy.height = height;
            return copy;
        }
    }

    /// <summary>应用配置（config.json）。</summary>
    public class AppConfig
    {
        public string keyName { get; set; }
        public string apiKeyProtected { get; set; }
        public string model { get; set; }
        public string thinking { get; set; }
        public WindowConfig window { get; set; }

        /// <summary>运行期使用的明文 Key，不落盘。</summary>
        [ScriptIgnore]
        public string apiKeyPlain { get; set; }

        public AppConfig()
        {
            keyName = Defaults.KeyName;
            apiKeyProtected = string.Empty;
            model = Defaults.Model;
            thinking = Defaults.ThinkingDefault;
            window = new WindowConfig();
            apiKeyPlain = string.Empty;
        }

        /// <summary>补齐缺失/非法字段。</summary>
        public void Normalize()
        {
            if (string.IsNullOrEmpty(keyName))
            {
                keyName = Defaults.KeyName;
            }
            if (string.IsNullOrEmpty(model))
            {
                model = Defaults.Model;
            }
            thinking = Defaults.NormalizeThinking(thinking);
            if (window == null)
            {
                window = new WindowConfig();
            }
            if (window.width < Defaults.WindowMinWidth)
            {
                window.width = Defaults.WindowWidth;
            }
            if (window.height < Defaults.WindowMinHeight)
            {
                window.height = Defaults.WindowHeight;
            }
            if (apiKeyProtected == null)
            {
                apiKeyProtected = string.Empty;
            }
            if (apiKeyPlain == null)
            {
                apiKeyPlain = string.Empty;
            }
        }
    }

    /// <summary>已保存的提示词记录。</summary>
    public class SavedEntry
    {
        public string title { get; set; }
        public string content { get; set; }
        /// <summary>中文原文（旧版本记录可能没有该字段）。</summary>
        public string source { get; set; }
        public string time { get; set; }

        public SavedEntry()
        {
            title = string.Empty;
            content = string.Empty;
            source = string.Empty;
            time = string.Empty;
        }

        /// <summary>列表显示文本。</summary>
        [ScriptIgnore]
        public string Display
        {
            get
            {
                string t = string.IsNullOrEmpty(title) ? "无标题" : title;
                if (string.IsNullOrEmpty(time))
                {
                    return t;
                }
                return t + "（" + time + "）";
            }
        }
    }

    /// <summary>
    /// 配置与数据读写：config.json / prompt.txt / saved.json，以及 API Key 的 DPAPI 加解密。
    /// </summary>
    internal static class Storage
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object SyncRoot = new object();
        private static AppConfig _config;
        private static bool _decryptFailed;
        private static bool _configLoadFailed;
        private static string _decryptMessage;

        /// <summary>应用数据目录。</summary>
        public static string AppDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Defaults.AppFolderName); }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(AppDir, "config.json"); }
        }

        public static string PromptPath
        {
            get { return Path.Combine(AppDir, "prompt.txt"); }
        }

        public static string SavedPath
        {
            get { return Path.Combine(AppDir, "saved.json"); }
        }

        /// <summary>解密 API Key 是否失败（换用户/换机器）。</summary>
        public static bool DecryptFailed
        {
            get { return _decryptFailed; }
        }

        /// <summary>config.json 是否解析失败（已恢复默认配置）。</summary>
        public static bool ConfigLoadFailed
        {
            get { return _configLoadFailed; }
        }

        /// <summary>解密失败的原因说明。</summary>
        public static string DecryptMessage
        {
            get { return _decryptMessage; }
        }

        /// <summary>当前配置（首次访问时自动加载）。</summary>
        public static AppConfig Config
        {
            get
            {
                lock (SyncRoot)
                {
                    if (_config == null)
                    {
                        Load();
                    }
                    return _config;
                }
            }
        }

        /// <summary>确保数据目录与各文件存在，并载入配置。</summary>
        public static void Load()
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(AppDir);

                AppConfig cfg = new AppConfig();
                bool configLoadFailed = false;
                if (File.Exists(ConfigPath))
                {
                    try
                    {
                        string json = ReadAllText(ConfigPath);
                        AppConfig loaded = JsonUtil.Deserialize<AppConfig>(json);
                        if (loaded != null)
                        {
                            cfg = loaded;
                        }
                    }
                    catch (Exception ex)
                    {
                        // 配置损坏：使用默认值，但保留原文件以便排查，并在启动时提示用户
                        TryBackupCorrupt(ConfigPath);
                        configLoadFailed = true;
                        _decryptMessage = "配置文件 config.json 解析失败，已临时使用默认配置（原文件已备份为 config.json.bad）：" + ex.Message;
                    }
                }
                cfg.Normalize();

                _configLoadFailed = configLoadFailed;
                _decryptFailed = false;
                cfg.apiKeyPlain = string.Empty;
                if (!string.IsNullOrEmpty(cfg.apiKeyProtected))
                {
                    string plain;
                    string error;
                    if (TryUnprotect(cfg.apiKeyProtected, out plain, out error))
                    {
                        cfg.apiKeyPlain = plain;
                    }
                    else
                    {
                        _decryptFailed = true;
                        _decryptMessage = error;
                    }
                }

                _config = cfg;

                if (!File.Exists(PromptPath))
                {
                    WritePrompt(Defaults.DefaultSystemPrompt);
                }
                if (!File.Exists(SavedPath))
                {
                    SaveSaved(new List<SavedEntry>());
                }
                if (!File.Exists(ConfigPath))
                {
                    SaveConfig();
                }
            }
        }

        /// <summary>把当前配置写回 config.json。</summary>
        public static void SaveConfig()
        {
            lock (SyncRoot)
            {
                if (_config == null)
                {
                    return;
                }
                Directory.CreateDirectory(AppDir);
                string json = JsonUtil.Serialize(_config);
                WriteFileAtomic(ConfigPath, json);
            }
        }

        /// <summary>更新内存中的 API Key（明文 + 密文），并落盘。</summary>
        public static void SetApiKey(string plainKey)
        {
            lock (SyncRoot)
            {
                if (_config == null)
                {
                    Load();
                }
                _config.apiKeyPlain = plainKey == null ? string.Empty : plainKey.Trim();
                if (_config.apiKeyPlain.Length == 0)
                {
                    _config.apiKeyProtected = string.Empty;
                }
                else
                {
                    _config.apiKeyProtected = Protect(_config.apiKeyPlain);
                }
                SaveConfig();
            }
        }

        /// <summary>读取系统提示词（每次生成前调用，改完即生效）。</summary>
        public static string ReadPrompt()
        {
            try
            {
                if (!File.Exists(PromptPath))
                {
                    return Defaults.DefaultSystemPrompt;
                }
                string text = ReadAllText(PromptPath);
                return NormalizeNewlines(text);
            }
            catch (Exception ex)
            {
                throw new IOException("读取系统提示词失败：" + ex.Message, ex);
            }
        }

        /// <summary>写入系统提示词（临时文件 + 原子替换）。</summary>
        public static void WritePrompt(string text)
        {
            Directory.CreateDirectory(AppDir);
            WriteFileAtomic(PromptPath, NormalizeNewlines(text));
        }

        /// <summary>
        /// 统一换行为 CRLF：WinForms 文本框只有 CRLF 才能正确显示换行；
        /// 归一化是确定性的，不影响请求前缀的字节稳定性。
        /// </summary>
        public static string NormalizeNewlines(string text)
        {
            if (text == null)
            {
                return string.Empty;
            }
            string unified = text.Replace("\r\n", "\n").Replace("\r", "\n");
            return unified.Replace("\n", "\r\n");
        }

        /// <summary>读取已保存的提示词列表（读取失败时返回空列表，详见重载）。</summary>
        public static List<SavedEntry> LoadSaved()
        {
            bool failed;
            return LoadSaved(out failed);
        }

        /// <summary>
        /// 读取已保存的提示词列表。loadFailed 为 true 表示 saved.json 解析失败
        /// （此时返回空列表，调用方不得把空列表写回，否则会丢光用户记录）。
        /// </summary>
        public static List<SavedEntry> LoadSaved(out bool loadFailed)
        {
            loadFailed = false;
            List<SavedEntry> list = new List<SavedEntry>();
            try
            {
                if (!File.Exists(SavedPath))
                {
                    return list;
                }
                string json = ReadAllText(SavedPath);
                if (string.IsNullOrEmpty(json) || json.Trim().Length == 0)
                {
                    return list;
                }
                List<SavedEntry> loaded = JsonUtil.Deserialize<List<SavedEntry>>(json);
                if (loaded != null)
                {
                    for (int i = 0; i < loaded.Count; i++)
                    {
                        SavedEntry e = loaded[i];
                        if (e == null)
                        {
                            continue;
                        }
                        if (e.title == null)
                        {
                            e.title = string.Empty;
                        }
                        if (e.content == null)
                        {
                            e.content = string.Empty;
                        }
                        if (e.source == null)
                        {
                            // 旧版本保存的记录没有中文字段
                            e.source = string.Empty;
                        }
                        if (e.time == null)
                        {
                            e.time = string.Empty;
                        }
                        list.Add(e);
                    }
                }
            }
            catch (Exception)
            {
                // 解析失败：返回空列表但置位失败标志，原文件保留并另行备份
                TryBackupCorrupt(SavedPath);
                loadFailed = true;
            }
            return list;
        }

        /// <summary>整体写回已保存的提示词列表（临时文件 + 原子替换）。</summary>
        public static void SaveSaved(List<SavedEntry> entries)
        {
            Directory.CreateDirectory(AppDir);
            if (entries == null)
            {
                entries = new List<SavedEntry>();
            }
            string json = JsonUtil.Serialize(entries);
            WriteFileAtomic(SavedPath, json);
        }

        /// <summary>
        /// 原子写文件：先写 .tmp，再替换目标文件，避免中断产生半截内容覆盖原文件。
        /// 任何异常路径下目标文件都存在，内容要么是旧值要么是新值。
        /// </summary>
        private static void WriteFileAtomic(string path, string content)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, content, Utf8NoBom);

            if (!File.Exists(path))
            {
                File.Move(temp, path);
                return;
            }

            try
            {
                File.Replace(temp, path, null);
                return;
            }
            catch (Exception)
            {
                // File.Replace 在个别文件系统上不可用，退化为“先保底改名，再放入新文件”
            }

            string backup = path + ".bak";
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }
            File.Move(path, backup);
            try
            {
                File.Move(temp, path);
            }
            catch (Exception)
            {
                // 放新文件失败：回滚保存旧文件，保证目标文件始终存在
                File.Move(backup, path);
                throw;
            }
            File.Delete(backup);
        }

        /// <summary>使用 DPAPI（CurrentUser）加密，输出 Base64。</summary>
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain))
            {
                return string.Empty;
            }
            byte[] data = Encoding.UTF8.GetBytes(plain);
            byte[] cipher = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipher);
        }

        /// <summary>解密 Base64 中的 DPAPI 密文。</summary>
        public static bool TryUnprotect(string protectedBase64, out string plain, out string error)
        {
            plain = string.Empty;
            error = string.Empty;
            if (string.IsNullOrEmpty(protectedBase64))
            {
                return false;
            }
            try
            {
                byte[] cipher = Convert.FromBase64String(protectedBase64);
                byte[] data = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
                plain = Encoding.UTF8.GetString(data);
                return true;
            }
            catch (Exception ex)
            {
                error = "API Key 解密失败（可能更换了 Windows 用户或机器），请在配置中重新输入：" + ex.Message;
                return false;
            }
        }

        /// <summary>错误信息脱敏：把可能出现的 Key 片段替换掉。</summary>
        public static string Sanitize(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }
            string plain = null;
            try
            {
                plain = Config.apiKeyPlain;
            }
            catch (Exception)
            {
                plain = null;
            }
            if (!string.IsNullOrEmpty(plain) && plain.Length >= 6)
            {
                message = message.Replace(plain, "***");
                // 同时屏蔽常见的前缀/后缀片段
                if (plain.Length > 12)
                {
                    message = message.Replace(plain.Substring(0, 6), "***");
                    message = message.Replace(plain.Substring(plain.Length - 4), "***");
                }
            }
            return message;
        }

        private static string ReadAllText(string path)
        {
            // 自动识别 BOM；无 BOM 时按 UTF-8 读取
            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static void TryBackupCorrupt(string path)
        {
            try
            {
                string bak = path + ".bad";
                if (File.Exists(bak))
                {
                    File.Delete(bak);
                }
                File.Copy(path, bak);
            }
            catch (Exception)
            {
                // 备份失败不影响主流程
            }
        }
    }
}
