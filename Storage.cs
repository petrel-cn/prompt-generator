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

    /// <summary>查看窗口的几何与分栏信息（config.json 的 viewWindow）。</summary>
    public class ViewConfig
    {
        public int x { get; set; }
        public int y { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        /// <summary>左右分栏的左栏宽度（px）。</summary>
        public int listWidth { get; set; }
        /// <summary>右侧上下分栏的上栏高度（px）。</summary>
        public int sourceHeight { get; set; }

        public ViewConfig()
        {
            x = -1;
            y = -1;
            width = Defaults.ViewWindowWidth;
            height = Defaults.ViewWindowHeight;
            listWidth = Defaults.ViewListWidth;
            sourceHeight = Defaults.ViewSourceHeight;
        }

        /// <summary>坐标是否已设置（-1 表示未设置，首次打开应居中）。</summary>
        [ScriptIgnore]
        public bool HasPosition
        {
            get { return x != -1 && y != -1; }
        }
    }

    /// <summary>应用配置（config.json）。</summary>
    public class AppConfig
    {
        /// <summary>
        /// 配置结构版本。构造函数默认 0（不是 Defaults.SchemaVersion）是刻意的：
        /// JavaScriptSerializer 只对 JSON 中出现的键赋值，因此旧 config.json 缺该键时
        /// 保留 0，才能被识别为「需要升级」；全新安装同样从 0 起步，加载时统一升级到 2。
        /// </summary>
        public int schemaVersion { get; set; }

        public string keyName { get; set; }
        public string apiKeyProtected { get; set; }
        public string model { get; set; }
        public string thinking { get; set; }
        /// <summary>文本区字号（磅，2.1.0 新增）：只作用于主窗口与查看窗口的「用户输入」「英文提示词」。</summary>
        public int fontSize { get; set; }
        public WindowConfig window { get; set; }
        /// <summary>查看窗口几何与分栏（2.0 新增，旧配置缺失时自动填默认）。</summary>
        public ViewConfig viewWindow { get; set; }

        /// <summary>运行期使用的明文 Key，不落盘。</summary>
        [ScriptIgnore]
        public string apiKeyPlain { get; set; }

        public AppConfig()
        {
            schemaVersion = 0;
            keyName = Defaults.KeyName;
            apiKeyProtected = string.Empty;
            model = Defaults.Model;
            thinking = Defaults.ThinkingDefault;
            fontSize = Defaults.FontSizeBase;
            window = new WindowConfig();
            viewWindow = new ViewConfig();
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
            fontSize = Defaults.NormalizeFontSize(fontSize);
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
            if (viewWindow == null)
            {
                viewWindow = new ViewConfig();
            }
            if (viewWindow.width < Defaults.ViewWindowMinWidth)
            {
                viewWindow.width = Defaults.ViewWindowWidth;
            }
            if (viewWindow.height < Defaults.ViewWindowMinHeight)
            {
                viewWindow.height = Defaults.ViewWindowHeight;
            }
            if (viewWindow.listWidth <= 0)
            {
                viewWindow.listWidth = Defaults.ViewListWidth;
            }
            if (viewWindow.sourceHeight <= 0)
            {
                viewWindow.sourceHeight = Defaults.ViewSourceHeight;
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
        /// <summary>用户输入原文（旧版本记录可能没有该字段）。</summary>
        public string source { get; set; }
        public string time { get; set; }
        /// <summary>生成时所用图片的原始路径；无图为空串（2.0 新增）。</summary>
        public string imagePath { get; set; }
        /// <summary>缩略图文件名（仅文件名，位于 thumbs\ 下）；无图为空串（2.0 新增）。</summary>
        public string thumbFile { get; set; }
        /// <summary>生成时用户输入是否包含 Pony Mode 标签（2.0 新增）。</summary>
        public bool isPony { get; set; }

        public SavedEntry()
        {
            title = string.Empty;
            content = string.Empty;
            source = string.Empty;
            time = string.Empty;
            imagePath = string.Empty;
            thumbFile = string.Empty;
            isPony = false;
        }

        /// <summary>
        /// 用户可编辑的标题文本：只含用户自己输入的部分，不含列表显示时派生的
        /// <Pony> 前缀与保存时间。「修改标题」以此预填，也以此写回。
        /// </summary>
        [ScriptIgnore]
        public string EditableTitle
        {
            get { return title == null ? string.Empty : title; }
        }

        /// <summary>
        /// 列表显示文本。Pony 前缀按 isPony 派生，不写入 title / content 字段。
        /// </summary>
        [ScriptIgnore]
        public string Display
        {
            get
            {
                string t = string.IsNullOrEmpty(title) ? "无标题" : title;
                string prefix = isPony ? Defaults.PonyDisplayPrefix : string.Empty;
                if (string.IsNullOrEmpty(time))
                {
                    return prefix + t;
                }
                return prefix + t + "（" + time + "）";
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

        /// <summary>缩略图目录（保存带图记录时才创建）。</summary>
        public static string ThumbsDir
        {
            get { return Path.Combine(AppDir, "thumbs"); }
        }

        /// <summary>
        /// 判断是否需要写入内置默认提示词：文件不存在（全新安装）或配置来自 2.0 之前的版本（升级重置）。
        /// 单独抽成纯函数，便于离线回归测试守住「只在升级时重置一次」这条关键约束。
        /// </summary>
        public static bool ShouldResetPrompt(bool promptFileExists, int schemaVersion)
        {
            if (!promptFileExists)
            {
                return true;
            }
            return schemaVersion < Defaults.SchemaVersion;
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

                bool needSaveConfig = !File.Exists(ConfigPath);

                // 2.0 升级策略：内置默认提示词换为综合版英文，升级时直接重置 prompt.txt，
                // 不保留旧的自定义内容（见技术方案 7.3）
                if (ShouldResetPrompt(File.Exists(PromptPath), cfg.schemaVersion))
                {
                    WritePrompt(Defaults.DefaultSystemPrompt);
                }

                if (cfg.schemaVersion < Defaults.SchemaVersion)
                {
                    cfg.schemaVersion = Defaults.SchemaVersion;
                    needSaveConfig = true;
                }

                if (!File.Exists(SavedPath))
                {
                    SaveSaved(new List<SavedEntry>());
                }
                if (needSaveConfig)
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
                            // 旧版本保存的记录没有用户输入字段
                            e.source = string.Empty;
                        }
                        if (e.time == null)
                        {
                            e.time = string.Empty;
                        }
                        if (e.imagePath == null)
                        {
                            // 旧版本记录没有图片字段
                            e.imagePath = string.Empty;
                        }
                        if (e.thumbFile == null)
                        {
                            e.thumbFile = string.Empty;
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
        /// 删除列表第 index 条的可测事务：先用 <see cref="RemoveEntryAt"/> 算出新列表，
        /// 再交给 save 写盘；只有写盘成功才通过 committed 交出提交后的列表。
        /// 写盘失败（或 save 为 null）时返回 false，entries 与 committed 均不受影响。
        /// 调用方必须用 committed 替换自己的列表引用，不得先改原列表再写盘。
        /// 写盘动作以委托注入，因此「写盘失败不得提交」这条顺序性质可离线断言。
        /// </summary>
        public static bool TryRemoveEntryAt(List<SavedEntry> entries, int index,
            Action<List<SavedEntry>> save, out List<SavedEntry> committed, out string error)
        {
            committed = null;
            error = string.Empty;
            if (save == null)
            {
                error = "内部错误：未提供写盘委托。";
                return false;
            }

            List<SavedEntry> remaining = RemoveEntryAt(entries, index);
            try
            {
                save(remaining);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            committed = remaining;
            return true;
        }

        /// <summary>
        /// 返回「移除 index 处记录」后的新列表，原列表不变（index 越界时返回等长副本）。
        /// 删除流程必须先拿到这份新列表、写盘成功后再把它换成内存列表：
        /// 若先改内存再写盘，写盘失败时列表行号与内存下标就会错位（详见 CHANGELOG 2.1.0 修复）。
        /// 抽成纯函数以便离线回归测试覆盖。
        /// </summary>
        public static List<SavedEntry> RemoveEntryAt(List<SavedEntry> entries, int index)
        {
            List<SavedEntry> remaining = new List<SavedEntry>();
            if (entries == null)
            {
                return remaining;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                if (i != index)
                {
                    remaining.Add(entries[i]);
                }
            }
            return remaining;
        }

        /// <summary>
        /// 原子写文件：先写 .tmp，再替换目标文件，避免中断产生半截内容覆盖原文件。
        /// 保证：本方法抛异常时目标文件内容必为旧值（故调用方可把「抛异常」当作「未写入」）。
        /// 为此，目标被替换成功之后只允许做 best-effort 的收尾（清理 .bak），不得再抛异常。
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
            try
            {
                File.Delete(backup);
            }
            catch (Exception)
            {
                // 此时目标文件已是新内容，所以清 .bak 失败只能忽略：
                // 若让它冒泡，调用方会误以为「未写入」，内存与磁盘将分叉
                // （残留的 .bak 无害，下次写入时会先清掉）
            }
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
