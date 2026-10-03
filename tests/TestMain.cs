using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PromptGenerator;

/// <summary>
/// 离线回归测试。直接编译项目源码（Defaults / JsonUtil / DeepSeekClient / ImageUtil / Storage），
/// 不联网、不触碰 %APPDATA%，可反复执行。
///
/// 覆盖范围：
///   - JSON 序列化/反序列化（含中文与 emoji 保真、[ScriptIgnore] 不泄露明文 Key）
///   - 生成响应解析（choices[0].message.content）
///   - 文本 / 图片请求体组装（内容块数组、data URL、不传 detail）
///   - 余额解析与币种符号映射
///   - HTTP 错误归一化与重试判定
///   - prompt.txt 换行归一化
///   - 数据文件原子写（失败不丢旧内容）
///   - DPAPI 加解密
///   - 额外指令 / Pony Mode 标签增删
///   - 图片格式校验、BMP 转 PNG、缩略图尺寸与文件名
///   - config.json（schemaVersion / viewWindow）与 saved.json（imagePath / thumbFile / isPony）新旧兼容
///
/// 未覆盖（需人工验证）：WinForms 界面、真实 API 往返、跨用户 DPAPI 场景。
/// </summary>
internal static class TestMain
{
    private static int _pass;
    private static int _fail;

    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        ConstantsTests();
        JsonTests();
        NewlineTests();
        SavedEntryTests();
        ConfigTests();
        TagTests();
        AsStringTests();
        ParseTests();
        ImageRequestTests();
        ImageUtilTests();
        RetryTests();
        RemoveEntryTests();
        AtomicWriteTests();
        DpapiTests();

        Console.WriteLine();
        Console.WriteLine("PASS=" + _pass + "  FAIL=" + _fail);
        return _fail == 0 ? 0 : 1;
    }

    private static void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            _pass++;
            Console.WriteLine("[PASS] " + name);
        }
        else
        {
            _fail++;
            Console.WriteLine("[FAIL] " + name + " -> " + detail);
        }
    }

    /// <summary>把换行与制表符转成可见形式，便于失败时定位。</summary>
    private static string Escape(string text)
    {
        if (text == null)
        {
            return "<null>";
        }
        return text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }

    #region 常量与默认值

    private static void ConstantsTests()
    {
        Check("版本号为 2.1.0", Defaults.AppVersion == "2.1.0", Defaults.AppVersion);
        Check("配置结构版本为 2", Defaults.SchemaVersion == 2, Defaults.SchemaVersion.ToString());
        Check("额外指令标签常量", Defaults.ExtraInstructionTag == "<额外指令>:", Defaults.ExtraInstructionTag);
        Check("Pony Mode 标签常量", Defaults.PonyModeTag == "<Pony Mode>", Defaults.PonyModeTag);
        Check("Pony 显示前缀常量", Defaults.PonyDisplayPrefix == "<Pony> ", Defaults.PonyDisplayPrefix);
        Check("缩略图上限为 250×200",
            Defaults.ThumbMaxWidth == 250 && Defaults.ThumbMaxHeight == 200,
            Defaults.ThumbMaxWidth + "x" + Defaults.ThumbMaxHeight);
        Check("上传框固定 250×200",
            Defaults.UploadBoxWidth == 250 && Defaults.UploadBoxHeight == 200,
            Defaults.UploadBoxWidth + "x" + Defaults.UploadBoxHeight);
        Check("主窗口默认 920×640",
            Defaults.WindowWidth == 920 && Defaults.WindowHeight == 640,
            Defaults.WindowWidth + "x" + Defaults.WindowHeight);
        Check("主窗口最小 780×520",
            Defaults.WindowMinWidth == 780 && Defaults.WindowMinHeight == 520,
            Defaults.WindowMinWidth + "x" + Defaults.WindowMinHeight);
        Check("查看窗口默认 800×800",
            Defaults.ViewWindowWidth == 800 && Defaults.ViewWindowHeight == 800,
            Defaults.ViewWindowWidth + "x" + Defaults.ViewWindowHeight);
        Check("thumbs 目录位于应用目录下",
            Storage.ThumbsDir == Path.Combine(Storage.AppDir, "thumbs"),
            Storage.ThumbsDir);

        // 字号：基准 9 磅，可选 N、N+1 … N+4 共 5 档
        Check("字号基准 9 磅、5 档、上限 13",
            Defaults.FontSizeBase == 9 && Defaults.FontSizeOptionCount == 5 && Defaults.FontSizeMax == 13,
            Defaults.FontSizeBase + "/" + Defaults.FontSizeOptionCount + "/" + Defaults.FontSizeMax);
        Check("字号归一化：N…N+4 保持原值",
            Defaults.NormalizeFontSize(9) == 9 && Defaults.NormalizeFontSize(11) == 11
            && Defaults.NormalizeFontSize(13) == 13, "取值被改动");
        Check("字号归一化：缺省值 0 回落基准",
            Defaults.NormalizeFontSize(0) == Defaults.FontSizeBase, Defaults.NormalizeFontSize(0).ToString());
        Check("字号归一化：超上限钳制到上限",
            Defaults.NormalizeFontSize(99) == Defaults.FontSizeMax, Defaults.NormalizeFontSize(99).ToString());
        Check("字号显示名（基准带默认标注）",
            Defaults.FontSizeDisplayName(9) == "9 磅（默认）" && Defaults.FontSizeDisplayName(10) == "10 磅"
            && Defaults.FontSizeDisplayName(13) == "13 磅",
            Defaults.FontSizeDisplayName(9) + "/" + Defaults.FontSizeDisplayName(13));

        // 默认提示词为综合版英文：多段、含两种模式说明
        Check("默认提示词含 Pony Mode 说明",
            Defaults.DefaultSystemPrompt.IndexOf("<Pony Mode>") >= 0, "缺失");
        Check("默认提示词含额外指令说明",
            Defaults.DefaultSystemPrompt.IndexOf("<额外指令>") >= 0, "缺失");
        Check("默认提示词含图片反推说明",
            Defaults.DefaultSystemPrompt.IndexOf("reverse-engineer") >= 0, "缺失");
        Check("默认提示词以英文说明开头",
            Defaults.DefaultSystemPrompt.StartsWith("You are a professional prompt generator"),
            Defaults.DefaultSystemPrompt.Substring(0, 40));
    }

    #endregion

    #region JsonUtil

    private static void JsonTests()
    {
        // 1) Dictionary<string,object> 嵌套序列化 + 回读
        Dictionary<string, object> body = new Dictionary<string, object>();
        body["model"] = "deepseek-flash";
        body["stream"] = false;
        List<object> messages = new List<object>();
        Dictionary<string, object> sys = new Dictionary<string, object>();
        sys["role"] = "system";
        sys["content"] = "You are a prompt translator.";
        messages.Add(sys);
        Dictionary<string, object> user = new Dictionary<string, object>();
        user["role"] = "user";
        user["content"] = "白猫 窗台 😺";
        messages.Add(user);
        body["messages"] = messages;
        Dictionary<string, object> thinking = new Dictionary<string, object>();
        thinking["type"] = "disabled";
        body["thinking"] = thinking;

        string json = JsonUtil.Serialize(body);

        Dictionary<string, object> back;
        bool ok = JsonUtil.TryDeserializeObject(json, out back);
        Check("Dict 序列化可回读", ok, "反序列化失败");
        if (ok)
        {
            Check("model 字段保真", JsonUtil.GetString(back, "model") == "deepseek-flash", JsonUtil.GetString(back, "model"));

            // 注意：JavaScriptSerializer 把嵌套数组反序列化为 ArrayList，只能用 IList 接收
            IList msgs = back["messages"] as IList;
            Check("messages 反序列化为数组", msgs != null && msgs.Count == 2, "结构不是数组");
            if (msgs != null && msgs.Count == 2)
            {
                Dictionary<string, object> m0 = msgs[0] as Dictionary<string, object>;
                Dictionary<string, object> m1 = msgs[1] as Dictionary<string, object>;
                Check("messages[0] 为 system 且内容保真",
                    m0 != null && JsonUtil.GetString(m0, "role") == "system"
                    && JsonUtil.GetString(m0, "content") == "You are a prompt translator.",
                    m0 == null ? "null" : JsonUtil.GetString(m0, "content"));
                Check("messages[1] 为 user 且中文/emoji 保真",
                    m1 != null && JsonUtil.GetString(m1, "content") == "白猫 窗台 😺",
                    m1 == null ? "null" : JsonUtil.GetString(m1, "content"));
            }
            Dictionary<string, object> tk = back["thinking"] as Dictionary<string, object>;
            Check("thinking.type == disabled", tk != null && JsonUtil.GetString(tk, "type") == "disabled", "结构异常");
        }

        Check("stream 仍为 JSON 布尔值", json.IndexOf("\"stream\":false") >= 0, json);

        // 2) [ScriptIgnore] 生效
        string cfgJson = JsonUtil.Serialize(new TestConfig());
        Check("ScriptIgnore 的密钥明文不入 JSON", cfgJson.IndexOf("sk-secret") < 0, cfgJson);
        Check("ScriptIgnore 的只读派生属性不入 JSON", cfgJson.IndexOf("derived") < 0, cfgJson);
        Check("配置字段名与方案一致",
            cfgJson.IndexOf("\"keyName\"") >= 0 && cfgJson.IndexOf("\"apiKeyProtected\"") >= 0
            && cfgJson.IndexOf("\"model\"") >= 0 && cfgJson.IndexOf("\"window\"") >= 0, cfgJson);
        Check("API Key 密文可回读", cfgJson.IndexOf("\"apiKeyProtected\":\"QUJD\"") >= 0, cfgJson);

        // 3) 非法 JSON 不抛异常
        Dictionary<string, object> dummy;
        Check("非法 JSON 被安全拦截", !JsonUtil.TryDeserializeObject("{not-json", out dummy), "竟然解析成功");

        // 4) 缺失键返回空串
        Check("缺失键返回空字符串", JsonUtil.GetString(back, "nope") == "", "非空");
    }

    public class TestWindow
    {
        public int x { get; set; }
        public int y { get; set; }
        public int width { get; set; }
        public int height { get; set; }
    }

    public class TestConfig
    {
        public string keyName { get; set; }
        public string apiKeyProtected { get; set; }
        public string model { get; set; }
        public TestWindow window { get; set; }

        [System.Web.Script.Serialization.ScriptIgnore]
        public string apiKeyPlain { get; set; }

        [System.Web.Script.Serialization.ScriptIgnore]
        public string derived { get { return "x"; } }

        public TestConfig()
        {
            keyName = "DeepSeek";
            apiKeyProtected = "QUJD";
            model = "deepseek-flash";
            window = new TestWindow();
            apiKeyPlain = "sk-secret";
        }
    }

    #endregion

    #region 换行归一化

    private static void NewlineTests()
    {
        CHECK_NEWLINES("LF -> CRLF", "a\nb\nc", "a\r\nb\r\nc");
        CHECK_NEWLINES("CRLF 不变", "a\r\nb", "a\r\nb");
        CHECK_NEWLINES("孤立 CR -> CRLF", "a\rb", "a\r\nb");
        CHECK_NEWLINES("混合换行统一", "a\r\nb\nc\rd", "a\r\nb\r\nc\r\nd");
        CHECK_NEWLINES("null -> 空串", null, "");
        CHECK_NEWLINES("空串不变", "", "");

        // 默认系统提示词必须是多段（否则配置界面会显示成一整行）
        int lines = Defaults.DefaultSystemPrompt.Split('\n').Length;
        Check("默认系统提示词为多行", lines >= 10, "行数=" + lines);
    }

    private static void CHECK_NEWLINES(string name, string input, string expected)
    {
        string actual = Storage.NormalizeNewlines(input);
        Check("换行归一化：" + name, actual == expected, "得到 [" + Escape(actual) + "]");
    }

    #endregion

    #region saved.json 结构

    private static void SavedEntryTests()
    {
        // 含用户输入原文的新格式
        string json = "[{\"title\":\"窗台上的白猫\",\"content\":\"a white cat\",\"source\":\"窗台上的白猫，赛博朋克配色\",\"time\":\"2026-09-17 13:36\"}]";
        List<SavedEntry> entries = JsonUtil.Deserialize<List<SavedEntry>>(json);
        Check("saved.json 新格式（含 source）可反序列化",
            entries != null && entries.Count == 1 && entries[0].title == "窗台上的白猫"
            && entries[0].source == "窗台上的白猫，赛博朋克配色" && entries[0].time == "2026-09-17 13:36",
            "解析结果异常");

        // 旧格式（无 source 字段）不得抛异常
        List<SavedEntry> old = JsonUtil.Deserialize<List<SavedEntry>>(
            "[{\"title\":\"旧记录\",\"content\":\"old prompt\",\"time\":\"2026-02-13 21:30\"}]");
        Check("saved.json 旧格式（无 source）兼容",
            old != null && old.Count == 1 && old[0].title == "旧记录"
            && (old[0].source == null || old[0].source == ""),
            "兼容失败");

        // 序列化包含 source，供下个版本读回
        string roundTrip = JsonUtil.Serialize(old);
        Check("source 字段参与序列化", roundTrip.IndexOf("\"source\"") >= 0, roundTrip);

        // 列表显示：空标题显示为「无标题」
        SavedEntry entry = new SavedEntry();
        entry.title = "";
        entry.time = "2026-09-17 13:36";
        Check("空标题显示为 无标题", entry.Display == "无标题（2026-09-17 13:36）", entry.Display);

        // 2.0 新字段：新格式可读回
        string json20 = "[{\"title\":\"窗台上的白猫\",\"content\":\"cat\",\"source\":\"原文\",\"time\":\"2026-10-03 21:30\","
            + "\"imagePath\":\"D:\\\\Pictures\\\\cat.png\",\"thumbFile\":\"3f2ac1b6e8d24b0f9c7a5e1d2b3c4a55.png\",\"isPony\":true}]";
        List<SavedEntry> entries20 = JsonUtil.Deserialize<List<SavedEntry>>(json20);
        Check("saved.json 2.0 新字段可反序列化",
            entries20 != null && entries20.Count == 1
            && entries20[0].imagePath == "D:\\Pictures\\cat.png"
            && entries20[0].thumbFile == "3f2ac1b6e8d24b0f9c7a5e1d2b3c4a55.png"
            && entries20[0].isPony,
            entries20 == null || entries20.Count == 0 ? "解析失败" : Escape(entries20[0].imagePath + "|" + entries20[0].thumbFile));

        // 旧格式缺 2.0 字段时补齐默认
        SavedEntry legacy = old[0];
        Check("旧记录的 imagePath 补空串", legacy.imagePath == null || legacy.imagePath == "", "非空");
        Check("旧记录的 thumbFile 补空串", legacy.thumbFile == null || legacy.thumbFile == "", "非空");
        Check("旧记录的 isPony 为 false", !legacy.isPony, "为 true");

        // 序列化包含 2.0 字段
        string roundTrip20 = JsonUtil.Serialize(entries20);
        Check("imagePath / thumbFile / isPony 参与序列化",
            roundTrip20.IndexOf("\"imagePath\"") >= 0 && roundTrip20.IndexOf("\"thumbFile\"") >= 0
            && roundTrip20.IndexOf("\"isPony\":true") >= 0, roundTrip20);

        // <Pony> 前缀只作用于列表显示
        SavedEntry pony = new SavedEntry();
        pony.title = "窗前看书的女孩";
        pony.time = "2026-10-03 21:30";
        pony.isPony = true;
        Check("Pony 记录标题带 <Pony> 前缀",
            pony.Display == "<Pony> 窗前看书的女孩（2026-10-03 21:30）", pony.Display);

        SavedEntry normal = new SavedEntry();
        normal.title = "窗前看书的女孩";
        normal.time = "2026-10-03 21:30";
        Check("非 Pony 记录无前缀",
            normal.Display == "窗前看书的女孩（2026-10-03 21:30）", normal.Display);

        SavedEntry ponyNoTitle = new SavedEntry();
        ponyNoTitle.time = "2026-10-03 21:30";
        ponyNoTitle.isPony = true;
        Check("Pony 且无标题时前缀仍在前",
            ponyNoTitle.Display == "<Pony> 无标题（2026-10-03 21:30）", ponyNoTitle.Display);

        SavedEntry rename = new SavedEntry();
        rename.title = "旧标题";
        rename.time = "2026-10-03 21:30";
        rename.isPony = true;
        rename.title = "新标题";
        Check("修改标题后列表显示同步更新",
            rename.Display == "<Pony> 新标题（2026-10-03 21:30）", rename.Display);
        Check("修改标题不改动保存时间",
            rename.Display.IndexOf("（2026-10-03 21:30）") >= 0, rename.Display);
        Check("修改标题不改动 Pony 前缀",
            rename.Display.StartsWith("<Pony> "), rename.Display);
        Check("修改标题后 title 字段只存用户文字",
            JsonUtil.Serialize(rename).IndexOf("\"title\":\"新标题\"") >= 0, JsonUtil.Serialize(rename));

        // 可编辑标题必须只是 title 原文：不得混入派生的 <Pony> 前缀与保存时间（
        // 否则改名会把前缀/时间戳重复写进 title 字段）
        Check("可编辑标题等于 title 原文",
            pony.EditableTitle == "窗前看书的女孩", Escape(pony.EditableTitle));
        Check("可编辑标题不含 <Pony> 前缀",
            pony.EditableTitle.IndexOf("<Pony>") < 0, Escape(pony.EditableTitle));
        Check("可编辑标题不含保存时间",
            pony.EditableTitle.IndexOf("2026-10-03 21:30") < 0, Escape(pony.EditableTitle));
        Check("可编辑标题与列表显示文本不同",
            pony.EditableTitle != pony.Display, Escape(pony.EditableTitle));
        Check("无标题记录的可编辑标题为空串",
            ponyNoTitle.EditableTitle == string.Empty, Escape(ponyNoTitle.EditableTitle));
        Check("可编辑标题为派生属性，不写入 saved.json",
            JsonUtil.Serialize(pony).IndexOf("EditableTitle") < 0, JsonUtil.Serialize(pony));

        Check("title / content 字段不含 <Pony> 前缀",
            JsonUtil.Serialize(pony).IndexOf("<Pony>") < 0, JsonUtil.Serialize(pony));
    }

    #endregion

    #region config.json 结构

    private static void ConfigTests()
    {
        AppConfig fresh = new AppConfig();
        Check("新建配置的 schemaVersion 为 0（用于识别旧配置）",
            fresh.schemaVersion == 0, fresh.schemaVersion.ToString());
        Check("新建配置带 viewWindow 默认值",
            fresh.viewWindow != null
            && fresh.viewWindow.width == Defaults.ViewWindowWidth
            && fresh.viewWindow.height == Defaults.ViewWindowHeight,
            "缺失");
        Check("viewWindow 默认分栏值",
            fresh.viewWindow.listWidth == 300 && fresh.viewWindow.sourceHeight == 280,
            fresh.viewWindow.listWidth + "/" + fresh.viewWindow.sourceHeight);
        Check("新建配置的字号默认为基准字号",
            fresh.fontSize == Defaults.FontSizeBase, fresh.fontSize.ToString());

        // 旧 config.json：无 schemaVersion、无 viewWindow
        AppConfig legacy = JsonUtil.Deserialize<AppConfig>(
            "{\"keyName\":\"DeepSeek\",\"model\":\"deepseek-flash\",\"thinking\":\"disabled\","
            + "\"window\":{\"x\":10,\"y\":20,\"width\":920,\"height\":640}}");
        Check("旧 config.json 可反序列化且 schemaVersion 视为 0",
            legacy != null && legacy.schemaVersion == 0, "取值异常");
        legacy.Normalize();
        Check("旧 config.json 自动补齐 viewWindow",
            legacy.viewWindow != null && legacy.viewWindow.width == Defaults.ViewWindowWidth,
            "未补齐");
        Check("旧 config.json 自动补齐字号（回落基准）",
            legacy.fontSize == Defaults.FontSizeBase, legacy.fontSize.ToString());
        Check("旧 config.json 的 window 几何保持",
            legacy.window.x == 10 && legacy.window.y == 20
            && legacy.window.width == 920 && legacy.window.height == 640,
            "被改动");

        // 非法/过小值钳制
        AppConfig small = new AppConfig();
        small.window.width = 500;
        small.window.height = 300;
        small.viewWindow.width = 100;
        small.viewWindow.height = 100;
        small.viewWindow.listWidth = 0;
        small.viewWindow.sourceHeight = -5;
        small.Normalize();
        Check("主窗口几何小于最小值时回退默认",
            small.window.width == Defaults.WindowWidth && small.window.height == Defaults.WindowHeight,
            small.window.width + "x" + small.window.height);
        Check("查看窗口几何小于最小值时回退默认",
            small.viewWindow.width == Defaults.ViewWindowWidth && small.viewWindow.height == Defaults.ViewWindowHeight,
            small.viewWindow.width + "x" + small.viewWindow.height);
        Check("分栏非法值回退默认",
            small.viewWindow.listWidth == 300 && small.viewWindow.sourceHeight == 280,
            small.viewWindow.listWidth + "/" + small.viewWindow.sourceHeight);

        // 字号非法值钳制
        AppConfig oddFont = new AppConfig();
        oddFont.fontSize = 100;
        oddFont.Normalize();
        Check("字号超上限时钳制到上限",
            oddFont.fontSize == Defaults.FontSizeMax, oddFont.fontSize.ToString());
        oddFont.fontSize = -3;
        oddFont.Normalize();
        Check("字号为负数时回落基准",
            oddFont.fontSize == Defaults.FontSizeBase, oddFont.fontSize.ToString());

        // 序列化包含 2.0 新键
        string json = JsonUtil.Serialize(new AppConfig());
        Check("config.json 含 schemaVersion、viewWindow 与 fontSize",
            json.IndexOf("\"schemaVersion\"") >= 0 && json.IndexOf("\"viewWindow\"") >= 0
            && json.IndexOf("\"fontSize\"") >= 0, json);
        Check("viewWindow 的派生属性不入 JSON", json.IndexOf("HasPosition") < 0, json);

        // 升级重置判定：必须只在「文件不存在」或「旧版本配置」时为 true，
        // 否则会出现「每次启动都重置用户的 prompt.txt」这类严重回归
        Check("提示词重置：文件不存在时写入默认（全新安装）",
            Storage.ShouldResetPrompt(false, 0) && Storage.ShouldResetPrompt(false, Defaults.SchemaVersion), "判定错误");
        Check("提示词重置：旧版本配置（schemaVersion=0）",
            Storage.ShouldResetPrompt(true, 0), "未触发升级重置");
        Check("提示词重置：旧版本配置（schemaVersion=1）",
            Storage.ShouldResetPrompt(true, 1), "未触发升级重置");
        Check("提示词重置：已达当前版本（=2）不重置",
            !Storage.ShouldResetPrompt(true, Defaults.SchemaVersion), "会误重置用户提示词");
        Check("提示词重置：更高版本不重置",
            !Storage.ShouldResetPrompt(true, Defaults.SchemaVersion + 1), "会误重置用户提示词");
    }

    #endregion

    #region 额外指令 / Pony Mode 标签

    private static void TagTests()
    {
        string tag = Defaults.ExtraInstructionTag;
        string pony = Defaults.PonyModeTag;

        // 追加额外指令
        Check("额外指令：空文本直接写入标签",
            Defaults.AppendExtraInstruction("") == tag + " ", Escape(Defaults.AppendExtraInstruction("")));
        Check("额外指令：null 安全",
            Defaults.AppendExtraInstruction(null) == tag + " ", "异常");
        Check("额外指令：已有内容用空行分隔",
            Defaults.AppendExtraInstruction("白猫") == "白猫\r\n\r\n" + tag + " ",
            Escape(Defaults.AppendExtraInstruction("白猫")));

        string once = Defaults.AppendExtraInstruction("白猫");
        Check("额外指令：已含标签不重复追加",
            Defaults.AppendExtraInstruction(once) == once, Escape(Defaults.AppendExtraInstruction(once)));

        string withPony = "白猫\r\n\r\n" + pony;
        string both = Defaults.AppendExtraInstruction(withPony);
        Check("额外指令：Pony 已勾选时插入其上方",
            both == "白猫\r\n\r\n" + tag + " \r\n\r\n" + pony, Escape(both));

        // 移除额外指令
        Check("额外指令：无 Pony 时删除到文本末尾",
            Defaults.RemoveExtraInstruction(once) == "白猫", Escape(Defaults.RemoveExtraInstruction(once)));
        Check("额外指令：有 Pony 时只删到 Pony 段之前",
            Defaults.RemoveExtraInstruction(both) == "白猫\r\n\r\n" + pony,
            Escape(Defaults.RemoveExtraInstruction(both)));
        Check("额外指令：文本中无标签时原样返回（不破坏用户内容）",
            Defaults.RemoveExtraInstruction("白猫") == "白猫", Escape(Defaults.RemoveExtraInstruction("白猫")));
        Check("额外指令：空文本安全", Defaults.RemoveExtraInstruction("") == "", "异常");
        // 手工把 Pony 段调到额外指令之前时，不得复制 Pony 段、也不得残留额外指令段
        Check("额外指令：Pony 段在前时仍只删除额外指令段",
            Defaults.RemoveExtraInstruction(pony + "\r\n\r\n" + tag + " x") == pony,
            Escape(Defaults.RemoveExtraInstruction(pony + "\r\n\r\n" + tag + " x")));

        // Pony Mode
        Check("Pony：空文本直接写入标签",
            Defaults.AppendPonyMode("") == pony, Escape(Defaults.AppendPonyMode("")));
        // 按方案 8.3 伪代码，追加前先 TrimEnd()，因此「<额外指令>: 」末尾的占位空格会被去除
        Check("Pony：追加到末尾且位于额外指令下方",
            Defaults.AppendPonyMode(once) == "白猫\r\n\r\n" + tag + "\r\n\r\n" + pony,
            Escape(Defaults.AppendPonyMode(once)));
        Check("Pony：追加后标签仍位于额外指令下方（位置关系）",
            Defaults.AppendPonyMode(once).IndexOf(tag) < Defaults.AppendPonyMode(once).IndexOf(pony),
            Escape(Defaults.AppendPonyMode(once)));
        Check("Pony：已含标签不重复追加",
            Defaults.AppendPonyMode(withPony) == withPony, "重复追加");
        Check("Pony：取消时从标签行首截断",
            Defaults.RemovePonyMode(both) == "白猫\r\n\r\n" + tag.TrimEnd(),
            Escape(Defaults.RemovePonyMode(both)));
        Check("Pony：文本中无标签时原样返回",
            Defaults.RemovePonyMode("白猫") == "白猫", "异常");

        // 标签位于行中（非独占一行）时的 best-effort 行为
        string inline = "白猫" + tag + " 改成银白色";
        Check("额外指令：标签非行首时按行首定位删除",
            Defaults.RemoveExtraInstruction(inline) == "", Escape(Defaults.RemoveExtraInstruction(inline)));

        // LineStart
        Check("LineStart 首行返回 0", Defaults.LineStart("abc\ndef", 0) == 0, Defaults.LineStart("abc\ndef", 0).ToString());
        Check("LineStart 第二行返回行首", Defaults.LineStart("abc\ndef", 5) == 4, Defaults.LineStart("abc\ndef", 5).ToString());
        Check("LineStart 越界索引安全", Defaults.LineStart("abc", 99) == 0, Defaults.LineStart("abc", 99).ToString());
        Check("LineStart 空文本安全", Defaults.LineStart("", 0) == 0, "异常");

        // Pony 判定（生成时快照口径）
        Check("ContainsPonyMode 判定含标签文本", Defaults.ContainsPonyMode("白猫\r\n\r\n" + pony), "未识别");
        Check("ContainsPonyMode 对普通文本返回 false", !Defaults.ContainsPonyMode("白猫"), "误判");
        Check("ContainsExtraInstruction 判定", Defaults.ContainsExtraInstruction(once), "未识别");
    }

    #endregion

    #region AsString

    private static void AsStringTests()
    {
        Check("AsString(ArrayList) 返回空串", JsonUtil.AsString(new ArrayList()) == "", "非空");
        Check("AsString(object[]) 返回空串", JsonUtil.AsString(new object[] { 1 }) == "", "非空");
        Check("AsString(Dictionary) 返回空串", JsonUtil.AsString(new Dictionary<string, object>()) == "", "非空");
        Check("AsString(string) 原样返回", JsonUtil.AsString("a") == "a", "异常");
        Check("AsString(null) 返回空串", JsonUtil.AsString(null) == "", "异常");
        Check("AsString(12.5) 不受影响", JsonUtil.AsString(12.5) == "12.5", JsonUtil.AsString(12.5));
        Check("AsString(true) 不受影响", JsonUtil.AsString(true) == "True", JsonUtil.AsString(true));
    }

    #endregion

    #region DeepSeekClient 解析

    private static object InvokePrivate(string name, object[] args)
    {
        MethodInfo mi = typeof(DeepSeekClient).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        if (mi == null)
        {
            throw new InvalidOperationException("找不到私有方法 " + name);
        }
        return mi.Invoke(null, args);
    }

    private static void ParseTests()
    {
        // 正常响应
        string okBody = "{\"id\":\"abc\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"a white cat sitting on the windowsill, cyberpunk color scheme\"},\"finish_reason\":\"stop\"}],\"usage\":{\"total_tokens\":42}}";
        object[] a1 = new object[] { okBody, null, null };
        bool ok1 = (bool)InvokePrivate("TryExtractContent", a1);
        Check("正常响应提取 content",
            ok1 && ((string)a1[1]) == "a white cat sitting on the windowsill, cyberpunk color scheme",
            ok1 ? (string)a1[1] : (string)a1[2]);

        // content 两侧空白被裁剪
        object[] a2 = new object[] { "{\"choices\":[{\"message\":{\"content\":\"  hello  \"}}]}", null, null };
        bool ok2 = (bool)InvokePrivate("TryExtractContent", a2);
        Check("content 裁剪首尾空白", ok2 && ((string)a2[1]) == "hello", ok2 ? (string)a2[1] : (string)a2[2]);

        // content 为空
        object[] a3 = new object[] { "{\"choices\":[{\"message\":{\"content\":\"\"}}]}", null, null };
        bool ok3 = (bool)InvokePrivate("TryExtractContent", a3);
        Check("空 content 被判定为失败", !ok3 && ((string)a3[2]).Length > 0, "未拦截");

        // 结构缺失
        object[] a4 = new object[] { "{\"choices\":[]}", null, null };
        bool ok4 = (bool)InvokePrivate("TryExtractContent", a4);
        Check("choices 为空被判定为失败", !ok4 && ((string)a4[2]).Length > 0, "未拦截");

        object[] a5 = new object[] { "<html>502 Bad Gateway</html>", null, null };
        bool ok5 = (bool)InvokePrivate("TryExtractContent", a5);
        Check("非 JSON 响应被判定为失败", !ok5 && ((string)a5[2]).Length > 0, "未拦截");

        // error.message 提取
        string errBody = "{\"error\":{\"message\":\"Authentication Fails, Your api key is invalid\",\"type\":\"authentication_error\",\"code\":\"invalid_request_error\"}}";
        string msg = (string)InvokePrivate("ExtractErrorMessage", new object[] { errBody });
        Check("提取 error.message", msg == "Authentication Fails, Your api key is invalid", msg);

        // 错误归一化
        object[] a7 = new object[] { 401, errBody, "", null };
        bool unauthorized = (bool)InvokePrivate("DescribeError", a7);
        Check("401 归类为鉴权失败并带服务端说明",
            unauthorized && ((string)a7[3]).IndexOf("API Key 无效或未配置") >= 0
            && ((string)a7[3]).IndexOf("Authentication Fails") >= 0, (string)a7[3]);

        object[] a8 = new object[] { 402, "{\"error\":{\"message\":\"Insufficient Balance\"}}", "", null };
        InvokePrivate("DescribeError", a8);
        Check("402 提示余额不足", ((string)a8[3]).IndexOf("余额不足") >= 0, (string)a8[3]);

        object[] a9 = new object[] { 422, "{\"error\":{\"message\":\"Model Not Exist\"}}", "", null };
        InvokePrivate("DescribeError", a9);
        Check("422 提示模型名可能失效", ((string)a9[3]).IndexOf("模型名可能已失效") >= 0, (string)a9[3]);

        object[] a10 = new object[] { 429, "", "", null };
        InvokePrivate("DescribeError", a10);
        Check("429 提示限流", ((string)a10[3]).IndexOf("请求过于频繁") >= 0, (string)a10[3]);

        object[] a11 = new object[] { 503, "", "", null };
        InvokePrivate("DescribeError", a11);
        Check("503 提示服务不可用", ((string)a11[3]).IndexOf("服务暂时不可用") >= 0, (string)a11[3]);

        object[] a12 = new object[] { 0, "", "网络错误：请求超时（30 秒），请稍后重试。", null };
        InvokePrivate("DescribeError", a12);
        Check("网络异常原样反馈", ((string)a12[3]).IndexOf("请求超时") >= 0, (string)a12[3]);

        // error 为数组时不泄露 CLR 类型名
        object[] a13 = new object[] { 400, "{\"error\":[{\"message\":\"boom\"}]}", "", null };
        InvokePrivate("DescribeError", a13);
        Check("error 为数组时错误文案不含类型名", ((string)a13[3]).IndexOf("System.") < 0, (string)a13[3]);

        // 余额：文档示例
        string bal = "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"110.00\",\"granted_balance\":\"10.00\",\"topped_up_balance\":\"100.00\"}]}";
        object[] b1 = new object[] { bal, null, null };
        bool bok1 = (bool)InvokePrivate("TryParseBalance", b1);
        Check("余额解析 CNY -> ￥110.00", bok1 && ((string)b1[1]) == "\uFFE5110.00", bok1 ? (string)b1[1] : (string)b1[2]);

        // 多币种
        string bal2 = "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"110.00\"},{\"currency\":\"USD\",\"total_balance\":\"5.30\"}]}";
        object[] b2 = new object[] { bal2, null, null };
        bool bok2 = (bool)InvokePrivate("TryParseBalance", b2);
        Check("多币种空格分隔", bok2 && ((string)b2[1]) == "\uFFE5110.00 $5.30", bok2 ? (string)b2[1] : (string)b2[2]);

        // 未知币种原样显示
        object[] b3 = new object[] { "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"JPY\",\"total_balance\":\"900\"}]}", null, null };
        bool bok3 = (bool)InvokePrivate("TryParseBalance", b3);
        Check("未知币种原样显示", bok3 && ((string)b3[1]) == "JPY900", bok3 ? (string)b3[1] : (string)b3[2]);

        // is_available = false
        object[] b4 = new object[] { "{\"is_available\":false,\"balance_infos\":[]}", null, null };
        bool bok4 = (bool)InvokePrivate("TryParseBalance", b4);
        Check("is_available=false 被判定为失败", !bok4 && ((string)b4[2]).Length > 0, "未拦截");

        // 金额按字符串，不做浮点运算
        object[] b5 = new object[] { "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"0.10\"}]}", null, null };
        bool bok5 = (bool)InvokePrivate("TryParseBalance", b5);
        Check("金额按字符串保真（0.10 不被写成 0.1）", bok5 && ((string)b5[1]) == "\uFFE50.10", bok5 ? (string)b5[1] : (string)b5[2]);

        // 金额是 JSON 数字时不崩溃
        object[] b6 = new object[] { "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":110}]}", null, null };
        bool bok6 = (bool)InvokePrivate("TryParseBalance", b6);
        Check("数字型金额不崩溃", bok6 && ((string)b6[1]) == "\uFFE5110", bok6 ? (string)b6[1] : (string)b6[2]);

        // 请求体：思考模式开关（无图片）
        DeepSeekClient off = new DeepSeekClient("sk-x", "deepseek-flash", "disabled");
        Dictionary<string, object> offBody = (Dictionary<string, object>)InvokeInstance(off, "BuildChatBody", new object[] { "SYS", "用户输入", null });
        Dictionary<string, object> offThinking = offBody["thinking"] as Dictionary<string, object>;
        Check("disabled 模式 thinking.type=disabled",
            offThinking != null && JsonUtil.GetString(offThinking, "type") == "disabled", "结构异常");
        Check("disabled 模式不发送 reasoning_effort", !offBody.ContainsKey("reasoning_effort"), "含该字段");

        DeepSeekClient on = new DeepSeekClient("sk-x", "deepseek-flash", "max");
        Dictionary<string, object> onBody = (Dictionary<string, object>)InvokeInstance(on, "BuildChatBody", new object[] { "SYS", "用户输入", null });
        Dictionary<string, object> onThinking = onBody["thinking"] as Dictionary<string, object>;
        Check("max 模式 thinking.type=enabled",
            onThinking != null && JsonUtil.GetString(onThinking, "type") == "enabled", "结构异常");
        Check("max 模式发送 reasoning_effort=max",
            onBody.ContainsKey("reasoning_effort") && (string)onBody["reasoning_effort"] == "max", "缺失或值错");

        // 非法思考模式值被归一化为 disabled
        DeepSeekClient bad = new DeepSeekClient("sk-x", "deepseek-flash", "ultra");
        Dictionary<string, object> badBody = (Dictionary<string, object>)InvokeInstance(bad, "BuildChatBody", new object[] { "SYS", "x", null });
        Dictionary<string, object> badThinking = badBody["thinking"] as Dictionary<string, object>;
        Check("非法思考模式归一化为 disabled",
            badThinking != null && JsonUtil.GetString(badThinking, "type") == "disabled", "未归一化");

        // 缓存友好：system 在 messages[0]，且 system 与 user 相邻
        IList msgs = onBody["messages"] as IList;
        Check("messages 仅两条且 system 在前",
            msgs != null && msgs.Count == 2
            && JsonUtil.GetString(msgs[0] as Dictionary<string, object>, "role") == "system"
            && JsonUtil.GetString(msgs[1] as Dictionary<string, object>, "role") == "user", "结构异常");
        Check("stream 为 false", badBody.ContainsKey("stream") && ((bool)badBody["stream"]) == false, "非 false");
    }

    private static object InvokeInstance(object instance, string name, object[] args)
    {
        MethodInfo mi = instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (mi == null)
        {
            throw new InvalidOperationException("找不到私有方法 " + name);
        }
        return mi.Invoke(instance, args);
    }

    #endregion

    #region 图片请求体

    private static void ImageRequestTests()
    {
        DeepSeekClient client = new DeepSeekClient("sk-x", "deepseek-flash", "disabled");
        byte[] png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        ImagePayload payload = new ImagePayload(png, "image/png");
        string dataUrl = "data:image/png;base64," + Convert.ToBase64String(png);

        // 无图片：content 仍为字符串（与 1.x 逐字节一致）
        Dictionary<string, object> noImage = (Dictionary<string, object>)InvokeInstance(
            client, "BuildChatBody", new object[] { "SYS", "白猫", null });
        IList msgs = noImage["messages"] as IList;
        object noImageContent = (msgs[1] as Dictionary<string, object>)["content"];
        Check("无图片时 user.content 为字符串",
            noImageContent is string && (string)noImageContent == "白猫", "类型或内容不符");

        // 有图片 + 文本
        Dictionary<string, object> withImage = (Dictionary<string, object>)InvokeInstance(
            client, "BuildChatBody", new object[] { "SYS", "白猫", payload });
        IList msgs2 = withImage["messages"] as IList;
        Check("含图片时 messages 仍为两条", msgs2 != null && msgs2.Count == 2, "结构异常");
        object systemContent = (msgs2[0] as Dictionary<string, object>)["content"];
        Check("system.content 始终为字符串（图片不入 system）", systemContent is string, "类型不符");

        IList blocks = (msgs2[1] as Dictionary<string, object>)["content"] as IList;
        Check("含图片时 user.content 为内容块数组", blocks != null && blocks.Count == 2, "结构异常");
        if (blocks != null && blocks.Count == 2)
        {
            Dictionary<string, object> b0 = blocks[0] as Dictionary<string, object>;
            Dictionary<string, object> b1 = blocks[1] as Dictionary<string, object>;
            Check("文本块在前",
                JsonUtil.GetString(b0, "type") == "text" && JsonUtil.GetString(b0, "text") == "白猫", "结构异常");
            Check("image_url 块在后", JsonUtil.GetString(b1, "type") == "image_url", "结构异常");

            Dictionary<string, object> imageUrl = b1["image_url"] as Dictionary<string, object>;
            Check("data URL 形如 data:image/png;base64,...",
                imageUrl != null && JsonUtil.GetString(imageUrl, "url") == dataUrl,
                JsonUtil.GetString(imageUrl, "url"));
            Check("图片块不传 detail（块与对象两层都不得出现）",
                !b1.ContainsKey("detail") && imageUrl != null && !imageUrl.ContainsKey("detail"), "被传入了");
        }

        // 仅图片（文本为空）：只有 image_url 块
        Dictionary<string, object> onlyImage = (Dictionary<string, object>)InvokeInstance(
            client, "BuildChatBody", new object[] { "SYS", "", payload });
        IList msgs3 = onlyImage["messages"] as IList;
        IList blocks3 = (msgs3[1] as Dictionary<string, object>)["content"] as IList;
        Check("仅图片时只含 image_url 块",
            blocks3 != null && blocks3.Count == 1
            && JsonUtil.GetString(blocks3[0] as Dictionary<string, object>, "type") == "image_url",
            "结构异常");

        // 空载荷（无字节）按无图片处理
        Dictionary<string, object> emptyPayload = (Dictionary<string, object>)InvokeInstance(
            client, "BuildChatBody", new object[] { "SYS", "白猫", new ImagePayload(new byte[0], "image/png") });
        IList msgs4 = emptyPayload["messages"] as IList;
        Check("空载荷按无图片处理（content 为字符串）",
            (msgs4[1] as Dictionary<string, object>)["content"] is string, "变成了数组");

        // 序列化后仍是嵌套数组 + data URL
        string json = JsonUtil.Serialize(withImage);
        Check("请求体序列化含 image_url 与 data URL",
            json.IndexOf("\"image_url\"") >= 0 && json.IndexOf("data:image/png;base64,") >= 0,
            json.Substring(0, Math.Min(160, json.Length)));

        // 回读时嵌套数组为 ArrayList（守 IList 约定）
        Dictionary<string, object> back;
        JsonUtil.TryDeserializeObject(json, out back);
        IList backMsgs = back["messages"] as IList;
        IList backBlocks = (backMsgs[1] as Dictionary<string, object>)["content"] as IList;
        Check("含图片请求体可回读为嵌套数组",
            backBlocks != null && backBlocks.Count == 2
            && JsonUtil.GetString(backBlocks[1] as Dictionary<string, object>, "type") == "image_url",
            "结构异常");
    }

    #endregion

    #region ImageUtil

    private static void ImageUtilTests()
    {
        // 扩展名过滤
        Check("支持 jpg/jpeg/png/webp/bmp",
            ImageUtil.IsSupportedExtensionValue(".jpg") && ImageUtil.IsSupportedExtensionValue(".JPEG")
            && ImageUtil.IsSupportedExtensionValue(".png") && ImageUtil.IsSupportedExtensionValue(".webp")
            && ImageUtil.IsSupportedExtensionValue(".bmp"), "判定失败");
        Check("不支持 gif/txt/无扩展名",
            !ImageUtil.IsSupportedExtensionValue(".gif") && !ImageUtil.IsSupportedExtensionValue(".txt")
            && !ImageUtil.IsSupportedExtensionValue(""), "误判");

        // 缩略图文件名格式
        string name = ImageUtil.NewThumbFileName();
        Check("缩略图文件名为 32 位十六进制 + .png",
            Regex.IsMatch(name, "^[0-9a-f]{32}\\.png$"), name);
        Check("缩略图文件名各不相同", ImageUtil.NewThumbFileName() != name, "重复");

        // 删除辅助对空值安全
        ImageUtil.TryDeleteThumb(null);
        ImageUtil.TryDeleteThumb("");
        ImageUtil.TryDeleteThumb("..\\..\\evil.png");
        Check("删除辅助对 null / 空 / 穿越路径不抛异常", true, "");

        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp-image");
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
        Directory.CreateDirectory(dir);

        try
        {
            // 不支持的扩展名
            string gif = Path.Combine(dir, "note.gif");
            File.WriteAllBytes(gif, new byte[] { 1, 2, 3 });
            ImagePayload gifPayload;
            string gifError;
            bool gifOk = ImageUtil.LoadForUpload(gif, out gifPayload, out gifError);
            Check("不支持的扩展名被拒绝并给出提示",
                !gifOk && gifError == ImageUtil.UnsupportedFormatMessage, gifError);

            // 超过 32 MiB（稀疏文件，不实际占用磁盘）
            string big = Path.Combine(dir, "big.png");
            using (FileStream fs = new FileStream(big, FileMode.Create, FileAccess.Write))
            {
                fs.SetLength((long)Defaults.MaxImageBytes + 1);
            }
            ImagePayload bigPayload;
            string bigError;
            bool bigOk = ImageUtil.LoadForUpload(big, out bigPayload, out bigError);
            Check("超过 32MiB 的图片被拒绝并给出提示",
                !bigOk && bigError == ImageUtil.TooLargeMessage, bigError);
            File.Delete(big);

            // 文件不存在
            ImagePayload missing;
            string missingError;
            Check("文件不存在时安全失败",
                !ImageUtil.LoadForUpload(Path.Combine(dir, "nope.png"), out missing, out missingError)
                && missingError.Length > 0, missingError);

            // BMP -> PNG
            string bmpPath = Path.Combine(dir, "tiny.bmp");
            using (Bitmap bmp = new Bitmap(60, 40))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Red);
                }
                bmp.Save(bmpPath, ImageFormat.Bmp);
            }
            ImagePayload bmpPayload;
            string bmpError;
            bool bmpOk = ImageUtil.LoadForUpload(bmpPath, out bmpPayload, out bmpError);
            Check("BMP 被转码为 PNG（MIME 与魔数）",
                bmpOk && bmpPayload.MimeType == "image/png" && bmpPayload.Length > 8
                && bmpPayload.Data[0] == 0x89 && bmpPayload.Data[1] == 0x50
                && bmpPayload.Data[2] == 0x4E && bmpPayload.Data[3] == 0x47,
                bmpError.Length > 0 ? bmpError : bmpPayload == null ? "无载荷" : bmpPayload.MimeType);

            // JPEG 原样字节
            string jpgPath = Path.Combine(dir, "tiny.jpg");
            using (Bitmap bmp = new Bitmap(30, 30))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Green);
                }
                bmp.Save(jpgPath, ImageFormat.Jpeg);
            }
            ImagePayload jpgPayload;
            string jpgError;
            bool jpgOk = ImageUtil.LoadForUpload(jpgPath, out jpgPayload, out jpgError);
            Check("JPEG 原样上传（MIME + 字节不变）",
                jpgOk && jpgPayload.MimeType == "image/jpeg"
                && jpgPayload.Length == File.ReadAllBytes(jpgPath).Length,
                jpgError);

            // WebP 原样字节（GDI+ 不参与编解码）
            string webpPath = Path.Combine(dir, "fake.webp");
            File.WriteAllBytes(webpPath, new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0 });
            ImagePayload webpPayload;
            string webpError;
            bool webpOk = ImageUtil.LoadForUpload(webpPath, out webpPayload, out webpError);
            Check("WebP 原样上传（MIME image/webp）",
                webpOk && webpPayload.MimeType == "image/webp", webpError);

            // 缩略图：横图 1000×400 -> 250×100
            string wideSrc = Path.Combine(dir, "wide.png");
            using (Bitmap bmp = new Bitmap(1000, 400))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Blue);
                }
                bmp.Save(wideSrc, ImageFormat.Png);
            }
            string wideThumb = Path.Combine(dir, "wide-thumb.png");
            bool wideOk = ImageUtil.TryCreateThumbnail(wideSrc, wideThumb);
            int wideW = 0;
            int wideH = 0;
            if (wideOk)
            {
                using (Image img = Image.FromFile(wideThumb))
                {
                    wideW = img.Width;
                    wideH = img.Height;
                }
            }
            Check("横图缩略图等比缩放为 250×100（不超过 250×200）",
                wideOk && wideW == 250 && wideH == 100, wideW + "x" + wideH);

            // 缩略图：竖图 400×800 -> 100×200
            string tallSrc = Path.Combine(dir, "tall.png");
            using (Bitmap bmp = new Bitmap(400, 800))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Blue);
                }
                bmp.Save(tallSrc, ImageFormat.Png);
            }
            string tallThumb = Path.Combine(dir, "tall-thumb.png");
            bool tallOk = ImageUtil.TryCreateThumbnail(tallSrc, tallThumb);
            int tallW = 0;
            int tallH = 0;
            if (tallOk)
            {
                using (Image img = Image.FromFile(tallThumb))
                {
                    tallW = img.Width;
                    tallH = img.Height;
                }
            }
            Check("竖图缩略图等比缩放为 100×200",
                tallOk && tallW == 100 && tallH == 200, tallW + "x" + tallH);

            // 原图缺失时缩略图失败但不抛异常
            Check("原图缺失时缩略图生成安全失败",
                !ImageUtil.TryCreateThumbnail(Path.Combine(dir, "nope.png"), Path.Combine(dir, "x.png")),
                "竟然成功");

            // 预览解码
            ImagePayload previewPayload;
            string previewLoadError;
            ImageUtil.LoadForUpload(wideSrc, out previewPayload, out previewLoadError);
            ImagePreview preview;
            string previewError;
            bool previewOk = ImageUtil.TryCreatePreview(previewPayload, out preview, out previewError);
            Check("PNG 可建立界面预览",
                previewOk && preview != null && preview.Image.Width == 1000, previewError);
            if (previewOk && preview != null)
            {
                preview.Dispose();
            }

            // WebP（GDI+ 无法解码）时预览失败但不抛异常，且不影响上传
            ImagePreview webpPreview;
            string webpPreviewError;
            bool webpPreviewOk = ImageUtil.TryCreatePreview(webpPayload, out webpPreview, out webpPreviewError);
            Check("WebP 预览失败但不影响上传（返回 false）",
                !webpPreviewOk && webpPreviewError.Length > 0, "竟然成功");
            if (webpPreviewOk && webpPreview != null)
            {
                webpPreview.Dispose();
            }
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (Exception)
            {
                // 清理失败不影响测试结论
            }
        }
    }

    #endregion

    #region 重试判定

    private static void RetryTests()
    {
        MethodInfo miStatus = typeof(DeepSeekClient).GetMethod("IsRetryableStatus", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo miTransport = typeof(DeepSeekClient).GetMethod("IsRetryableTransport", BindingFlags.NonPublic | BindingFlags.Static);

        Check("429 可重试", (bool)miStatus.Invoke(null, new object[] { 429 }), "no");
        Check("503 可重试", (bool)miStatus.Invoke(null, new object[] { 503 }), "no");
        Check("500 可重试", (bool)miStatus.Invoke(null, new object[] { 500 }), "no");
        Check("400 不重试", !(bool)miStatus.Invoke(null, new object[] { 400 }), "yes");
        Check("401 不重试", !(bool)miStatus.Invoke(null, new object[] { 401 }), "yes");
        Check("402 不重试", !(bool)miStatus.Invoke(null, new object[] { 402 }), "yes");
        Check("422 不重试", !(bool)miStatus.Invoke(null, new object[] { 422 }), "yes");

        Check("ConnectFailure 可重试",
            (bool)miTransport.Invoke(null, new object[] { System.Net.WebExceptionStatus.ConnectFailure }), "no");
        Check("NameResolutionFailure 可重试",
            (bool)miTransport.Invoke(null, new object[] { System.Net.WebExceptionStatus.NameResolutionFailure }), "no");
        Check("Timeout 不重试",
            !(bool)miTransport.Invoke(null, new object[] { System.Net.WebExceptionStatus.Timeout }), "yes");
        Check("TrustFailure 不重试",
            !(bool)miTransport.Invoke(null, new object[] { System.Net.WebExceptionStatus.TrustFailure }), "yes");
    }

    #endregion

    #region 保存列表的删除（写盘失败时内存列表不得被改动）

    private static void RemoveEntryTests()
    {
        List<SavedEntry> entries = new List<SavedEntry>();
        for (int i = 0; i < 3; i++)
        {
            SavedEntry e = new SavedEntry();
            e.title = ((char)('A' + i)).ToString();
            e.content = "prompt-" + e.title;
            entries.Add(e);
        }

        List<SavedEntry> remaining = Storage.RemoveEntryAt(entries, 1);
        Check("删除中间记录：新列表不含该记录且长度减一",
            remaining.Count == 2 && remaining[0].title == "A" && remaining[1].title == "C",
            remaining.Count + " 条");
        Check("删除后原列表保持原样（写盘失败时内存与界面才不会错位）",
            entries.Count == 3 && entries[0].title == "A" && entries[1].title == "B" && entries[2].title == "C",
            entries.Count + " 条");
        Check("删除返回新列表而非原列表",
            !object.ReferenceEquals(entries, remaining), "引用了同一个对象");
        Check("删除第一条",
            Storage.RemoveEntryAt(entries, 0)[0].title == "B", "结果异常");
        Check("删除最后一条",
            Storage.RemoveEntryAt(entries, 2)[1].title == "B", "结果异常");
        Check("越界下标不得误删",
            Storage.RemoveEntryAt(entries, 3).Count == 3 && Storage.RemoveEntryAt(entries, -1).Count == 3,
            "长度被改动");
        Check("空列表与 null 安全",
            Storage.RemoveEntryAt(new List<SavedEntry>(), 0).Count == 0
            && Storage.RemoveEntryAt(null, 0).Count == 0, "抛异常或返回异常值");

        // TryRemoveEntryAt：写盘成功才提交（删除流程的顺序性质由它守护）
        List<SavedEntry> committed;
        string error;
        bool ok = Storage.TryRemoveEntryAt(entries, 1, FailSaver, out committed, out error);
        Check("写盘失败：TryRemoveEntryAt 返回 false 且带出原因",
            !ok && error.Length > 0, ok + " / " + Escape(error));
        Check("写盘失败：不交出提交结果", committed == null, "committed 非 null");
        Check("写盘失败：原列表仍为 3 条（修复前此处会被改掉，导致行号错位）",
            entries.Count == 3 && entries[1].title == "B", entries.Count + " 条");

        Check("未提供写盘委托时按失败处理",
            !Storage.TryRemoveEntryAt(entries, 1, null, out committed, out error) && committed == null,
            "返回了成功");

        _savedForWrite = null;
        ok = Storage.TryRemoveEntryAt(entries, 1, RecordSaver, out committed, out error);
        Check("写盘成功：交出移除后的新列表",
            ok && committed != null && committed.Count == 2
            && committed[0].title == "A" && committed[1].title == "C",
            ok + " / " + (committed == null ? "<null>" : committed.Count.ToString()));
        Check("写盘成功：交给写盘的与交出的是同一份新列表",
            object.ReferenceEquals(_savedForWrite, committed), "不是同一对象");
        Check("写盘成功：原列表仍未被改动（由调用方替换引用）",
            entries.Count == 3 && entries[1].title == "B", entries.Count + " 条");
    }

    private static List<SavedEntry> _savedForWrite;

    private static void FailSaver(List<SavedEntry> entries)
    {
        throw new IOException("模拟写盘失败");
    }

    private static void RecordSaver(List<SavedEntry> entries)
    {
        _savedForWrite = entries;
    }

    #endregion

    #region 原子写

    private static void AtomicWriteTests()
    {
        MethodInfo mi = typeof(Storage).GetMethod("WriteFileAtomic", BindingFlags.NonPublic | BindingFlags.Static);
        Check("找到 WriteFileAtomic", mi != null, "反射失败");
        if (mi == null)
        {
            return;
        }

        // 测试目录固定在测试程序自己的 bin 下，测试结束即删除
        string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp-atomic");
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "data.json");

        mi.Invoke(null, new object[] { file, "[\"old\"]" });
        Check("原子写：首次创建", File.Exists(file) && File.ReadAllText(file, Encoding.UTF8) == "[\"old\"]", "内容异常");

        mi.Invoke(null, new object[] { file, "[\"new\"]" });
        Check("原子写：覆盖已有文件", File.ReadAllText(file, Encoding.UTF8) == "[\"new\"]", File.ReadAllText(file, Encoding.UTF8));
        Check("原子写：无 .tmp/.bak 残留", !File.Exists(file + ".tmp") && !File.Exists(file + ".bak"), "有残留");

        // 目标被独占占用：必须抛异常，且旧内容仍在、无半截文件
        FileStream locker = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        bool threw = false;
        try
        {
            mi.Invoke(null, new object[] { file, "[\"blocked\"]" });
        }
        catch (TargetInvocationException)
        {
            threw = true;
        }
        finally
        {
            locker.Dispose();
        }
        Check("原子写：目标被占用时抛异常", threw, "未抛异常");
        Check("原子写：异常后原文件仍存在且为旧内容",
            File.Exists(file) && File.ReadAllText(file, Encoding.UTF8) == "[\"new\"]",
            File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : "文件丢失");
        Check("原子写：异常后无 .bak 残留", !File.Exists(file + ".bak"), "有残留");

        try
        {
            Directory.Delete(dir, true);
        }
        catch (Exception)
        {
            // 清理失败不影响测试结论
        }
    }

    #endregion

    #region DPAPI

    private static void DpapiTests()
    {
        string key = "sk-test-0123456789abcdef";
        byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
        string b64 = Convert.ToBase64String(cipher);
        Check("密文 Base64 不含明文 Key", b64.IndexOf("sk-test") < 0, b64);

        byte[] plain = ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser);
        Check("DPAPI 加解密往返一致", Encoding.UTF8.GetString(plain) == key, "不一致");

        bool threw = false;
        try
        {
            byte[] bad = new byte[cipher.Length];
            Array.Copy(cipher, bad, cipher.Length);
            bad[0] = (byte)(bad[0] ^ 0xFF);
            ProtectedData.Unprotect(bad, null, DataProtectionScope.CurrentUser);
        }
        catch (Exception)
        {
            threw = true;
        }
        Check("密文被篡改时抛异常（走重新输入 Key 分支）", threw, "未抛异常");
    }

    #endregion
}
