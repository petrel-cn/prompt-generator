# 离线回归测试

改完代码跑一条命令，确认没有把老功能改坏：

```cmd
tests\run.cmd
```

全绿输出 `PASS=n  FAIL=0`，退出码 0；有任何失败退出码为 1。

## 特点

- **零依赖**：只用系统自带的 `csc.exe`，不装测试框架、不导入 dll
- **不联网、不写用户数据**：不调用 DeepSeek 接口，不触碰 `%APPDATA%\prompt-generator\`；临时文件只建在 `tests\bin\tmp-atomic\` 并在测试结束时删除
- **可测私有逻辑**：通过反射调用 `private static` 方法（`TryExtractContent`、`TryParseBalance`、`DescribeError`、`IsRetryableStatus`、`WriteFileAtomic` 等），不用为了测试把接口改成 `public`
- **测行为不测实现**：改实现不必改测试；断言失败会打印实际值，便于定位

## 覆盖范围

| 区域 | 断言内容 |
|------|----------|
| JSON 序列化 | 嵌套 Dictionary/数组回读、中文与 emoji 保真、`bool` 不被写成字符串、`[ScriptIgnore]` 不泄露明文 Key、非法 JSON 安全拦截 |
| 换行归一化 | LF / CRLF / 孤立 CR / 混合 / null / 空串 → 统一 CRLF；默认系统提示词为多行 |
| `saved.json` | 新格式（含中文原文 `source`）可解析、旧格式（无 `source`）兼容、`source` 参与序列化、空标题显示为「无标题」 |
| 生成响应 | `choices[0].message.content` 提取、首尾空白裁剪、空内容/空 choices/非 JSON 响应判定为失败 |
| 请求体 | 思考模式 `disabled` → `thinking.type=disabled` 且不发 `reasoning_effort`；`max` → `type=enabled` + `reasoning_effort=max`；非法值归一化为 `disabled`；system 固定为 `messages[0]` 且仅两条消息；`stream=false` |
| 余额解析 | CNY→￥、USD→$、未知币种原样、多币种空格分隔、`is_available=false` 判定失败、金额按字符串保真（`0.10` 不被写成 `0.1`）、数字型金额不崩溃 |
| 错误归一化 | 400 / 401 / 402 / 422 / 429 / 503 / 网络异常文案；401 标记鉴权失败；`error` 为数组时不泄露 CLR 类型名 |
| 重试判定 | 429 / 5xx / ConnectFailure / NameResolutionFailure 可重试；400 / 401 / 402 / 422 / Timeout / TrustFailure 不重试 |
| 原子写 | 首次创建、覆盖已有文件、无 `.tmp`/`.bak` 残留、目标被占用时抛异常且旧内容仍在 |
| DPAPI | 密文不含明文、加解密往返、密文篡改后抛异常 |

## 覆盖不到（必须人工验证）

- WinForms 界面与布局（无头环境测不了）
- 真实 DeepSeek 接口往返、模型名有效性、内容策略
- 跨 Windows 用户／跨机器的 DPAPI 解密失败场景
- 记事本交互、剪贴板占用等外部程序协作

## 复用这套写法（给其他 .NET 小工具）

1. 建 `tests\` 目录，放一个 `TestMain.cs`（`internal static class` + `static int Main`）和 `run.cmd`
2. `run.cmd` 里把**项目源码 `.cs`**（不是 exe/dll）和 `TestMain.cs` 一起编译成控制台 exe，输出到 `tests\bin\`
3. 断言写成 `Check(名称, 条件, 详情)`，维护 `_pass/_fail` 计数，`Main` 返回失败数决定退出码
4. 想测私有成员就反射：`typeof(T).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)`；带 `out` 参数的方法用 `object[] args` 传入再从 `args` 取回
5. 任何临时文件都建在 `AppDomain.CurrentDomain.BaseDirectory` 下并在测试内删除，绝不写用户数据目录
6. 控制台输出前设置 `Console.OutputEncoding = Encoding.UTF8`；`.cmd` 脚本里不要写中文注释（cmd 按 OEM 代码页解析，会报错）

这套做法的价值在于：**判据稳定、跑得快、无外部依赖**，因此可以在每次改动后顺手执行，而不是等到出问题再回头找。
