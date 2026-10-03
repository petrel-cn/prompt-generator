# 离线回归测试

改完代码跑一条命令，确认没有把老功能改坏：

```cmd
tests\run.cmd
```

全绿输出 `PASS=n  FAIL=0`，退出码 0；有任何失败退出码为 1。

## 特点

- **零依赖**：只用系统自带的 `csc.exe`，不装测试框架、不导入 dll
- **不联网、不写用户数据**：不调用 DeepSeek 接口，不触碰 `%APPDATA%\prompt-generator\`；临时文件只建在 `tests\bin\tmp-atomic\`、`tests\bin\tmp-image\` 并在测试结束时删除
- **可测私有逻辑**：通过反射调用 `private static` 方法（`TryExtractContent`、`TryParseBalance`、`DescribeError`、`IsRetryableStatus`、`BuildChatBody`、`WriteFileAtomic` 等），不用为了测试把接口改成 `public`
- **测行为不测实现**：改实现不必改测试；断言失败会打印实际值，便于定位

## 覆盖范围

| 区域 | 断言内容 |
|------|----------|
| 常量与默认值 | 版本号 / `schemaVersion` / 标签常量 / 缩略图与窗口尺寸常量 / 文本区字号基准 9 磅、5 档、上限 13 / `thumbs` 目录位置 / 默认提示词含两种模式说明 |
| JSON 序列化 | 嵌套 Dictionary/数组回读、中文与 emoji 保真、`bool` 不被写成字符串、`[ScriptIgnore]` 不泄露明文 Key、非法 JSON 安全拦截 |
| 换行归一化 | LF / CRLF / 孤立 CR / 混合 / null / 空串 → 统一 CRLF；默认系统提示词为多行 |
| `saved.json` | 新格式（含 `source`）可解析、旧格式兼容、`imagePath`/`thumbFile`/`isPony` 新字段读写与旧记录补齐、`<Pony>` 前缀只在列表显示而不写入字段、空标题显示为「无标题」、修改标题后显示同步更新而前缀与时间戳不受影响 |
| `config.json` | 新建配置 `schemaVersion=0`（用于识别旧配置）、旧配置缺 `viewWindow` 时自动补齐、几何过小回退默认、分栏非法值回退默认、`viewWindow` 参与序列化且派生属性不入 JSON、`fontSize` 默认值 / 非法值钳制 / 参与序列化 |
| 升级重置 | `ShouldResetPrompt(文件是否存在, schemaVersion)`：文件不存在或配置低于当前版本时才写默认；已达到或高于当前版本时绝不重置（守住「只在升级时重置一次」） |
| 输入标签 | 额外指令 / Pony Mode 的追加与移除（空文本、已有内容、已含标签、Pony 在上时的插入与删除、两段顺序被手工调换时的容错、找不到标签时不动文本）、标签位置关系、`LineStart` 边界、`ContainsPonyMode` 判定 |
| 生成响应 | `choices[0].message.content` 提取、首尾空白裁剪、空内容/空 choices/非 JSON 响应判定为失败 |
| 请求体 | 思考模式 `disabled` → `thinking.type=disabled` 且不发 `reasoning_effort`；`max` → `type=enabled` + `reasoning_effort=max`；非法值归一化为 `disabled`；system 固定为 `messages[0]` 且仅两条消息；`stream=false` |
| 图片请求体 | 无图时 `user.content` 仍为字符串（1.x 逐字节兼容）；有图时为内容块数组且文本块在前、`image_url` 块在后；仅图片时只含图片块；`system` 始终为字符串；不传 `detail`；data URL 格式；空载荷按无图处理；序列化后可回读 |
| 图片处理 | 扩展名白名单（含大小写）、不支持格式与 >32MiB 被拒、文件不存在安全失败、BMP 转 PNG（MIME + PNG 魔数）、JPEG/WebP 原样字节、缩略图等比尺寸（横图 250×100、竖图 100×200）、原图缺失时失败不抛异常、缩略图文件名格式、删除辅助对穿越路径安全 |
| 余额解析 | CNY→￥、USD→$、未知币种原样、多币种空格分隔、`is_available=false` 判定失败、金额按字符串保真（`0.10` 不被写成 `0.1`）、数字型金额不崩溃 |
| 错误归一化 | 400 / 401 / 402 / 422 / 429 / 503 / 网络异常文案；401 标记鉴权失败；`error` 为数组时不泄露 CLR 类型名 |
| 重试判定 | 429 / 5xx / ConnectFailure / NameResolutionFailure 可重试；400 / 401 / 402 / 422 / Timeout / TrustFailure 不重试 |
| 原子写 | 首次创建、覆盖已有文件、无 `.tmp`/`.bak` 残留、目标被占用时抛异常且旧内容仍在 |
| 保存列表删除 | `Storage.RemoveEntryAt` 纯函数：返回移除后的新列表且原列表不变、删除首 / 中 / 尾条、越界下标不误删、空列表与 null 安全；`Storage.TryRemoveEntryAt` 事务函数：写盘委托抛异常时返回 false、不交出提交结果且原列表不变，写盘成功时交出的就是交给写盘的那份新列表，未提供写盘委托按失败处理。**注意**：`ViewForm` 的删除流程本身（调用事务函数并替换列表引用）不在本套件的编译范围内，靠带写盘失败注入的 GUI harness 兜底 |
| DPAPI | 密文不含明文、加解密往返、密文篡改后抛异常 |

当前共 203 项断言（`PASS=203 FAIL=0`）。

## 覆盖不到（必须人工验证）

- WinForms 界面与布局、以及依赖真写盘的失败路径（无头环境测不了；本次升级已用临时 GUI harness 截图核对配置对话框、主窗口 9 / 13 磅两组字号、查看窗口与改名对话框，并用独占锁向 `saved.json` / `config.json` 注入写盘失败验证「删除失败」「配置保存失败」两条路径下内存与磁盘均不分叉，harness 本身不属于项目产物）。`ViewForm` / `ConfigForm` / `SaveDialog` 未纳入本套件的编译，它们内部的流程顺序（如删除的「先算后提交」、配置保存失败的回滚）只能由 harness 或人工保证
- 真实 DeepSeek 接口往返、模型名有效性、内容策略
- 图片反推的真实模型输出质量
- 跨 Windows 用户／跨机器的 DPAPI 解密失败场景
- 记事本交互、剪贴板占用、资源管理器拖放等外部程序协作
- GDI+ 不支持的 WebP 预览（代码中已按「上传可用、界面不显示缩略图」处理）

## 复用这套写法（给其他 .NET 小工具）

1. 建 `tests\` 目录，放一个 `TestMain.cs`（`internal static class` + `static int Main`）和 `run.cmd`
2. `run.cmd` 里把**项目源码 `.cs`**（不是 exe/dll）和 `TestMain.cs` 一起编译成控制台 exe，输出到 `tests\bin\`（用到 `System.Drawing` 的源码需同时加 `/r:System.Drawing.dll`）
3. 断言写成 `Check(名称, 条件, 详情)`，维护 `_pass/_fail` 计数，`Main` 返回失败数决定退出码
4. 想测私有成员就反射：`typeof(T).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)`；带 `out` 参数的方法用 `object[] args` 传入再从 `args` 取回
5. 任何临时文件都建在 `AppDomain.CurrentDomain.BaseDirectory` 下并在测试内删除，绝不写用户数据目录
6. 控制台输出前设置 `Console.OutputEncoding = Encoding.UTF8`；`.cmd` 脚本里不要写中文注释（cmd 按 OEM 代码页解析，会报错）

这套做法的价值在于：**判据稳定、跑得快、无外部依赖**，因此可以在每次改动后顺手执行，而不是等到出问题再回头找。
