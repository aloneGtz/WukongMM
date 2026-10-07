# 架构设计

## 1. 技术栈（2026-10-07 决定）
**C# / .NET 10 (LTS) + WPF**，目标框架 `net10.0-windows`。
- 只在 Windows 11 运行 → WPF 是最成熟的原生桌面方案；.NET 9+ 自带 Windows 11 Fluent 主题（`ThemeMode`），不需要第三方 UI 库
- 用户机器已装 .NET 10 Desktop Runtime 10.0.10 → 框架依赖发布的单文件 exe 只有几 MB
- 以后自制 CSharpLoader mod 也用 C#，同一套工具链
- **零 NuGet 依赖**：protobuf 用自写的 wire 级无损解析（游戏没有公开 .proto，用 Google.Protobuf 也没意义）；MVVM 基类自己写（几十行）；测试用 Cli 工程的 `selftest` 命令，不引入 xUnit
- 逆向研究用 Python 脚本（`research/`），不进入产品

## 2. 工程结构（计划）
```
WukongMM\（仓库根目录）
  WukongMM.sln
  src\
    WukongMM.Core\        类库（net10.0-windows）— 不依赖 UI
      Protobuf\           ProtoNode 无损解析树 / 读写
      Saves\              SaveFile（头 + XOR 主体）、ArchiveView（等级/灵蕴/经验/道具/仓储 访问器）、SaveLocator、SaveBackup、
                          ProgressPlanner（等级/经验/累计经验/灵光点按游戏规则换算与校验）
      Items\              ItemCatalog（游戏导出道具表 + 用户备注）、ItemRules（新增道具 / 改数量的游戏规则，SAVE_FORMAT §8.3）
      Mods\               GameLocator（解析 Steam libraryfolders.vdf）、IModProvider 各类型扫描、ModToggleService、ModMetadataStore
      Game\               GameProcess（检测游戏是否运行）、GamePaths、LevelTable（游戏导出的等级表 levels.json）
    WukongMM.App\         WPF 界面：Mod 管理 / 存档修改 / 设置
    WukongMM.Cli\         命令行：dump / roundtrip / selftest / 读写字段（开发与测试用）
  mods\WukongMMDataExport\ 管理器自带的游戏内 CSharpLoader mod（net472）：导出道具表 / 等级表 → <b1>\ModManager\GameData\items.json、levels.json
  research\               Python 逆向脚本；research\ILScan\ = 自写 .NET IL 扫描/反汇编工具（System.Reflection.Metadata，只读）
  research\samples\       存档样本（ACCEPTED = 游戏接受；REJECTED = 改了内容没更新校验值、被游戏拒绝）
  tools\                  开发辅助 PowerShell 脚本（截图、UI 自动化测试；必须纯 ASCII）
  test-mods\              用户要求下载的测试 mod（见其 README）
  docs\                   文档
```
- `mods/` 与 `research/ILScan` 不在 `WukongMM.slnx` 里（前者依赖游戏程序集，后者是研究工具），各自 `dotnet build -c Release`。

## 3. 关键设计
### 3.1 无损 protobuf
- `ProtoNode` 保存每个字段的 (field number, wire type, 原始字节)；length-delimited 字段按需"懒展开"成子消息
- 只有被修改的路径会重新编码，父节点长度前缀自动重算；未修改部分直接拷贝原始字节 → 保证未知字段、字段顺序、packed 数据不变
- 自测：解析 → 序列化 必须与原文件逐字节一致

### 3.2 存档写入流程
1. 检查游戏未运行
2. 备份：`<b1>\ModManager\SaveBackups\<SteamID>\<yyyyMMdd-HHmmss>\`（拷贝被修改的槽位文件 + ShareArchiveSaveFile）
3. 修改 → 序列化 → 重新解析验证 → 写临时文件 → `File.Replace` 原子替换
4. 存档头校验字段：主体改动后自动重算 `Md5Str`（SAVE_FORMAT.md §3.1、§6）

### 3.2.1 编辑规则（SAVE_FORMAT.md §8.3 的落实）
- 界面只负责收集输入和展示；规则都在 Core：`ProgressPlanner.Plan()`（等级/经验/灵光点）、`ItemRules.CheckAdd/CheckCount/CountChangeWarning`（道具），由 `wmm selftest` 覆盖
- 规则分三级：`Blocked`（不允许，弹错误）、`Warning`（需用户确认）、说明（`ProgressPlan.Notes`，列在保存确认框"注意"里）
- 依赖游戏导出表：没有 `levels.json` 时等级只做 1~9999 防呆、累计经验跟随当前经验差值；没有 `items.json` 时新增道具一律警告；旧版 items.json（schema 1）无法区分酒 → 消耗品警告
- 累计经验 = 原累计经验 + [S(新等级)+新经验] − [S(原等级)+原经验]（保留存档原有偏差；只改经验时退化为"同步加差值"）

### 3.3 Mod 启停
见 `MOD_LAYOUT.md` §2：移动到 `<b1>\ModManager\Disabled\`（保留相对路径），状态以文件位置为准，事务性回滚。

### 3.4 数据存放
| 内容 | 位置 |
|---|---|
| 应用设置（游戏路径等） | `%APPDATA%\WukongMM\settings.json` |
| mod 元数据 | `<b1>\ModManager\mods.json` |
| 已禁用的 mod 文件 | `<b1>\ModManager\Disabled\` |
| 存档备份 | `<b1>\ModManager\SaveBackups\` |
| 道具表 / 等级表 | 游戏内导出 mod 生成 `<b1>\ModManager\GameData\items.json`、`levels.json` |
| 道具名称备注 | 用户自定义 `%APPDATA%\WukongMM\item_names.json`（优先于游戏表） |

### 3.5 发布
`dotnet publish src/WukongMM.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true`
→ 单个 exe（依赖 .NET 10 Desktop Runtime）。游戏在非系统盘、非 Program Files，普通权限即可；遇到拒绝访问时提示以管理员运行。
