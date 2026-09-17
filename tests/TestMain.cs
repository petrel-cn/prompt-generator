using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PromptGenerator;

/// <summary>
/// 离线回归测试。直接编译项目源码（Defaults / JsonUtil / DeepSeekClient / Storage），
/// 不联网、不触碰 %APPDATA%，可反复执行。
///
/// 覆盖范围：
///   - JSON 序列化/反序列化（含中文与 emoji 保真、[ScriptIgnore] 不泄露明文 Key）
///   - 生成响应解析（choices[0].message.content）
///   - 余额解析与币种符号映射
///   - HTTP 错误归一化与重试判定
///   - prompt.txt 换行归一化
///   - 数据文件原子写（失败不丢旧内容）
///   - DPAPI 加解密
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

        JsonTests();
        NewlineTests();
        SavedEntryTests();
        AsStringTests();
        ParseTests();
        RetryTests();
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
        Check("换行归一化：" + name, actual == expected, "得到 [" + actual.Replace("\r", "\\r").Replace("\n", "\\n") + "]");
    }

    #endregion

    #region saved.json 结构

    private static void SavedEntryTests()
    {
        // 含中文原文的新格式
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

        // 请求体：思考模式开关
        DeepSeekClient off = new DeepSeekClient("sk-x", "deepseek-flash", "disabled");
        Dictionary<string, object> offBody = (Dictionary<string, object>)InvokeInstance(off, "BuildChatBody", new object[] { "SYS", "用户输入" });
        Dictionary<string, object> offThinking = offBody["thinking"] as Dictionary<string, object>;
        Check("disabled 模式 thinking.type=disabled",
            offThinking != null && JsonUtil.GetString(offThinking, "type") == "disabled", "结构异常");
        Check("disabled 模式不发送 reasoning_effort", !offBody.ContainsKey("reasoning_effort"), "含该字段");

        DeepSeekClient on = new DeepSeekClient("sk-x", "deepseek-flash", "max");
        Dictionary<string, object> onBody = (Dictionary<string, object>)InvokeInstance(on, "BuildChatBody", new object[] { "SYS", "用户输入" });
        Dictionary<string, object> onThinking = onBody["thinking"] as Dictionary<string, object>;
        Check("max 模式 thinking.type=enabled",
            onThinking != null && JsonUtil.GetString(onThinking, "type") == "enabled", "结构异常");
        Check("max 模式发送 reasoning_effort=max",
            onBody.ContainsKey("reasoning_effort") && (string)onBody["reasoning_effort"] == "max", "缺失或值错");

        // 非法思考模式值被归一化为 disabled
        DeepSeekClient bad = new DeepSeekClient("sk-x", "deepseek-flash", "ultra");
        Dictionary<string, object> badBody = (Dictionary<string, object>)InvokeInstance(bad, "BuildChatBody", new object[] { "SYS", "x" });
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
