# 绘图提示词生成器

把中文描述翻译为英文绘图提示词（text-to-image prompt）的单机 Windows 桌面工具。

- 单窗口、单轮、无状态：每次生成相互独立，不保留上下文
- 系统提示词由用户自行编辑，与任何外部工具链无关
- 独立 API Key（仅支持 DeepSeek），不与外部软件共享
- 面向本地绘图工作流，忠实翻译成人向（NSFW）内容

## 构建

零依赖，使用系统自带的 .NET Framework 编译器：

```cmd
build.cmd
```

产物：`bin\PromptGenerator.exe`（图标由 `icon\prompt-generator.ico` 经 `/win32icon` 嵌入，资源管理器/任务栏/标题栏均取该图标）

## 运行前提

- Windows 10 / 11（自带 .NET Framework 4.8 运行时）
- 无需安装 .NET SDK、Node 或任何第三方组件

## 数据目录

```
%APPDATA%\prompt-generator\
├── config.json     配置（API Key 为 DPAPI 密文）
├── prompt.txt      系统提示词（UTF-8 纯文本，可用记事本直接编辑）
└── saved.json      已保存的提示词（标题 / 中文原文 / 英文提示词 / 时间）
```

- 首次运行自动创建目录，并写入默认 `prompt.txt`
- `prompt.txt` 每次生成前重新读取，改完即生效，无需重启；读写时统一归一化为 CRLF 换行（确定性归一化，不影响请求前缀的字节稳定性）
- API Key 使用 Windows DPAPI（作用域 CurrentUser）加密后以 Base64 存入 `config.json`，不落明文
- 更换 Windows 用户或机器后无法解密，程序会提示重新输入

## 界面说明

主窗口：中文描述输入框、英文提示词输出框、`配置 / 生成 / 复制 / 保存 / 查看 / 关于` 六个按钮，底部状态栏显示：

```
密钥：DeepSeek ｜ 思考：关 ｜ 余额：￥110.00
```

- 点击状态栏区域可手动刷新余额
- 余额状态：`获取中…` / 具体金额 / `获取失败`（悬停可看失败原因）/ `未配置 Key`
- 生成期间全部按钮禁用，余额位置显示 `生成中…`
- 快捷键：`Ctrl+Enter` 触发生成
- `关于`：显示版本号、版权行（`Copyright (c) 2026 petrel-cn & LanZi`）与 MIT 许可证名/链接，以及“按原样提供”的担保免责提示

### 配置对话框

| 项 | 说明 |
|----|------|
| 密钥名称 | 状态栏显示名，默认 `DeepSeek` |
| API Key | 密码框，可勾选「显示」查看 |
| 模型 | 默认 `deepseek-flash`，可自行更换 |
| 思考模式 | `关闭` / `低（low）` / `高（high）` / `最高（max）` |
| 系统提示词 | 加载 `prompt.txt` 内容，可在此编辑 |
| 用记事本打开 | 先落盘再调用记事本编辑，关闭记事本后自动重新读取 |
| 恢复默认 | 文本框重置为内置默认提示词（需点「保存」才写入文件） |
| 测试连接 | 用当前填写的 Key 调用余额接口，成功显示余额 |
| 保存 / 取消 | 保存写回 `config.json` 与 `prompt.txt`；取消放弃修改 |

思考模式说明：关闭时请求体为 `"thinking": {"type": "disabled"}`；开启时传 `"thinking": {"type": "enabled"}` 与 `"reasoning_effort": "low|high|max"`。思考模式下 DeepSeek 会忽略 `temperature` 等采样参数，且时延与费用更高。

### 查看对话框

左侧列表显示「标题 + 时间」（空标题显示 `无标题`）；右侧上下分栏，上栏为**中文原文**，下栏为**英文提示词**（旧版本保存的记录没有中文原文，会提示「该记录保存于旧版本」）。可复制英文、删除、关闭；双击列表项把原文与英文一并加载回主窗口。

- 左右分隔线可拖动：拖动即可调整左侧标题列表宽度（左栏不小于 140 px、右栏不小于 260 px）；上方中文原文/英文提示词的分隔线同样可拖动
- 标题过长时列表下方出现横向滚动条，可拖动查看被截断的部分
- 记录较多时列表出现纵向滚动条，可逐条翻阅全部标题

## 接口

| 用途 | 方法与地址 |
|------|-----------|
| 生成 | `POST https://api.deepseek.com/chat/completions` |
| 余额 | `GET https://api.deepseek.com/user/balance` |

- 认证：`Authorization: Bearer <API Key>`
- 请求体显式 `"stream": false`
- 系统提示词固定为 `messages[0]`，逐字节不变（不注入时间戳/随机 ID），system 与 user 之间不插入额外消息，以保持前缀缓存友好
- 超时 30 秒；仅对 429 / 5xx / 网络异常重试（最多 2 次，指数退避），400 / 401 / 402 / 422 不重试

### 错误提示

| 状态码 | 界面提示 |
|--------|----------|
| 400 | 请求参数错误 + 服务端 `error.message` |
| 401 | API Key 无效或未配置（可直接跳转「配置」） |
| 402 | 账户余额不足 |
| 422 | 参数不合法，提示模型名可能已失效 |
| 429 | 请求过于频繁，请稍后重试 |
| 500 / 503 | 服务暂时不可用，请稍后重试 |
| 网络异常 | DNS / 连接 / TLS / 超时等具体原因 |

## 源码结构

| 文件 | 职责 |
|------|------|
| `Program.cs` | 入口；TLS 1.2 初始化；单实例互斥；全局异常兜底 |
| `MainForm.cs` | 主窗口；布局；按钮编排；状态栏；窗口几何持久化 |
| `ConfigForm.cs` | 配置对话框 |
| `ViewForm.cs` | 查看对话框 |
| `SaveDialog.cs` | 保存对话框（标题可留空） |
| `DeepSeekClient.cs` | HTTP 调用层：生成、余额、错误归一化、有限重试 |
| `Storage.cs` | `config.json` / `prompt.txt` / `saved.json` 读写，DPAPI 加解密 |
| `JsonUtil.cs` | `JavaScriptSerializer` 薄封装 |
| `Defaults.cs` | 默认提示词、默认模型名、常量与显示映射 |
| `build.cmd` | 编译脚本（嵌入 `icon\prompt-generator.ico`） |
| `icon\` | 程序图标源文件（`prompt-generator.ico`） |
| `tests\` | 离线回归测试（`run.cmd` + `TestMain.cs`） |
| `LICENSE` | MIT 许可证（英文原文） |
| `CHANGELOG.md` | 更新日志 |

实现细节：

- 语言约束 C# 5（不使用内插字符串、`?.`、表达式体成员），由 .NET Framework 自带 `csc.exe` 编译
- JSON 使用系统自带 `System.Web.Extensions.dll` 的 `JavaScriptSerializer`；注意它把嵌套 JSON 数组反序列化为 `ArrayList`，接收时必须用 `IList`（`object[]` 会恒为 `null`）
- 网络请求在 `Task.Factory.StartNew` 的后台线程执行，通过 `Control.BeginInvoke` 回主线程更新界面
- 窗口几何在 `Resize` / `Move` 时标记脏位，800 ms 去抖后写入 `config.json`；最小化/最大化状态不记录；启动时校验坐标是否落在任一显示器可见区域内，越界则居中
- `config.json` / `prompt.txt` / `saved.json` 均采用「临时文件 + 替换」的原子写，异常路径下目标文件始终存在（旧内容或新内容），不会出现半截文件

## 测试与更新日志

```cmd
tests\run.cmd      :: 离线回归测试（78 项断言，不联网、不写 %APPDATA%）
```

详见 `tests\README.md`（含覆盖范围与复用写法）与 `CHANGELOG.md`。

## 许可

本项目采用 MIT 许可证，版权归 petrel-cn & LanZi 所有，详见 [LICENSE](LICENSE)。

Copyright (c) 2026 petrel-cn & LanZi —— 本软件按“原样”提供，不附带任何明示或暗示的担保。

> 中文译文仅供参考，如与英文原文冲突，以 [LICENSE](LICENSE) 中的英文原文为准。
>
> 特此免费授予任何获得本软件及相关文档文件（“软件”）副本的人不受限制地处置本软件的权利，
> 包括但不限于使用、复制、修改、合并、发布、分发、再许可和/或销售本软件副本的权利，
> 并允许获得本软件的人在符合以下条件的前提下这样做：上述版权声明和本许可声明应包含在本软件的所有副本或主要部分中。
> 本软件按“原样”提供，不附带任何形式的明示或暗示担保，包括但不限于对适销性、特定用途适用性和非侵权的担保。
> 在任何情况下，作者或版权持有人均不对因本软件或本软件的使用或其他处置而产生的任何索赔、损害或其他责任负责。

无第三方代码依赖；`DeepSeek` 为 API 服务，其使用受服务方条款约束，不在本许可证覆盖范围内。
