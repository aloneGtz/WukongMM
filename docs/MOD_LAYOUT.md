# 各类 Mod 的安装位置与启用/禁用机制

> `<游戏>` = `<Steam 库>\steamapps\common\BlackMythWukong`，`<b1>` = `<游戏>\b1`，`<Win64>` = `<b1>\Binaries\Win64`
> 状态：✅ 已核实  ⚠ 来自网上资料，未在本机核实（本机当前没有安装任何 mod）

## 1. Mod 类型
| 类型 | 位置 | 一个"mod"的识别规则 | 依赖 |
|---|---|---|---|
| Pak 资源 mod | `<b1>\Content\Paks\~mods\` ⚠ | 直接放在 `~mods` 下的文件：同名主干（stem）的 `.pak/.utoc/.ucas/.sig` 为一个 mod；放在 `~mods\<子文件夹>\` 下的：整个一级子文件夹为一个 mod | 无（引擎原生加载） |
| Pak 放在 Paks 根目录 | `<b1>\Content\Paks\*.pak` | **没有对应 `.sig` 的 pak** 视为 mod（原版 pak 全部带 `.sig`，✅ 已核实原版清单见 §4） | 无 |
| 蓝图 mod (LogicMods) | `<b1>\Content\Paks\LogicMods\` ⚠ | 同 ~mods 规则 | UE4SS + BPModLoaderMod |
| CSharpLoader mod | `<Win64>\CSharpLoader\Mods\<Name>\<Name>.dll` ⚠ | 每个子文件夹为一个 mod；`Common` 是共享依赖目录，**不是** mod | CSharpLoader（`<Win64>\version.dll` + `<Win64>\CSharpLoader\`） |
| 数据表 mod (.data) | 在 Protobuf Loader 的目录下，具体路径待核实 ⚠ | 按文件夹，字典序加载（如 `100_xxx` 先于 `200_xxx`） | CSharpLoader + Protobuf Loader |
| UE4SS Lua/DLL mod | `<Win64>\ue4ss\Mods\<Name>\`（3.x 布局）或 `<Win64>\Mods\<Name>\`（旧布局）⚠ | 每个子文件夹为一个 mod；`mods.txt`、`shared` 不是 mod | UE4SS（`<Win64>\dwmapi.dll` + `UE4SS.dll` 等） |
| 自定义 mod | 用户手动指定游戏目录下任意文件/文件夹 | 用户自定义分组（用于布局特殊的 mod / 自制 mod） | — |

## 1.1 Pak mod 生效的前提：`-fileopenlog` 启动参数 ⚠
多个 mod 发布页（如 3DM 227058）都要求：用 `-fileopenlog` 参数启动游戏，`~mods` 里的 pak 才会被加载。
方式：Steam → 属性 → 启动选项填 `-fileopenlog`；或直接启动 `b1\Binaries\Win64\b1-Win64-Shipping.exe -fileopenlog`。
→ 管理器需要提供"带 -fileopenlog 启动游戏"按钮，并在检测到 pak mod 时提示。

## 1.2 Mod 来源调查（2026-10-07）
- Nexus Mods（`nexusmods.com/blackmythwukong`）：内置浏览器被 Cloudflare 人机验证拦截（不能绕过），下载也需登录
- 3DM Mod 站（`mod.3dmgame.com/BlackWukong`，game_id=376）：可访问。接口：`POST /api/search/getModlist`，body `{page,pageSize,search,original,time,order,key,gameType,gameId:[376]}`；详情 `GET /api/mods/<id>`（含 `mods_resource[].mods_resource_url` 直链，部分资源是迅雷/UC 网盘链接无法直接下载）
- GitHub：`czastack/B1CSharpLoader`（官方，release 最新 v0.0.8；Nexus 664 上有 v0.1.0）、`xyzkljl1/MyBlackMythWukongMods`（效果详细描述 CSharp、ProtobufLoader 等源码 + `CSharpMods/GameDll` 游戏程序集）
- **Nexus 公开 GraphQL**（不用登录、不过 Cloudflare，只能查不能下载）：`POST https://api.nexusmods.com/v2/graphql`，
  `query { mods(filter:{gameDomainName:[{value:"blackmythwukong"}]}, sort:[{downloads:{direction:DESC}}], count:40) { totalCount nodes { modId name downloads endorsements version updatedAt adultContent } } }`
- 3DM 列表接口 `order=2` = 按下载量降序；PowerShell 5.1 要用 `WebClient` + `Encoding=UTF8`（Invoke-RestMethod 会把中文解乱）

## 1.3 热门 mod 调研（2026-10-07，用户要求"把公认热门 mod 前 5~10 个放进管理器、默认不启用"）
Nexus 共 940 个 mod，下载量前列（2026-10-07）：UE4SS 49.0 万（框架）· SPF Redux 25.2 万 · simple map 19.9 万 · CSharpLoader 15.8 万（框架，已装）·
Signature Bypass 15.0 万（框架）· 筋斗云随处用 14.1 万 · Better dodge 8.3 万 · 稀有掉落提升 7.7 万 · BossRushV3 7.1 万 · 更强金箍棒 6.8 万 ·
TrueDasheng 5.2 万 · Enhanced Wukong Visuals 5.1 万 · Protobuf Loader 4.5 万（框架）· 双刃光剑 3.9 万 · 迷你地图 2.8 万。
3DM 共 1074 个，下载量前列：大地图 11.4 万 · 整合包（不考虑）· 全图筋斗云 7.5 万 · UE4SS 7.4 万 · 天命演武 2.0（2026-10 更新）3.5 万 · 胡桃 3.1 万 · SPF 搬运 2.8 万。

| 候选 | Nexus | 3DM 镜像（直链） | 大小 | 依赖 / 备注 |
|---|---|---|---|---|
| 大地图 simple map 1.5.1 | 60 | 212623（.gmm） | 12.02 MB | UE4SS |
| 全图筋斗云 2.1.1 | 92 | 212729（zip） | 9.19 KB | UE4SS |
| 稀有物品掉落率增加 1.0 | 405 | 213238（rar） | 8.66 KB | pak（~mods） |
| 迷你地图 1.3.4 | 233 | 212947（zip） | 142 KB | UE4SS（LogicMods） |
| 双刃光剑 1.2 | 42 | 226592（zip，另有音效 7.26 MB） | 2.36 MB | pak；与 Enhanced Wukong Visuals 冲突 |
| 最强快速闪避 2.3 | —（3DM 1.1 万） | 212883（zip） | 80.6 KB | pak；Nexus 的 Better dodge 在 3DM 无镜像 |
| 天命演武 2.1 | —（3DM 3.5 万） | 235261（zip，红眼/无红眼两版） | 100.76 MB | 2026-10-02 更新，兼容新版游戏可能性最高 |
| BMWK-SPF 性能优化 | 13 | 212635（zip，v3.0；Nexus 已 v4.5） | 3.63 KB | **Engine.ini 放 `%LocalAppData%\b1\Saved\Config\Windows`，不在游戏目录，管理器目前管不了** |
| 前置 UE4SS 1.3 | 19 | 212626（.gmm）/ 212599（zip 28.23 MB） | 15.41 MB | 框架；`Win64\ue4ss` 是受保护目录不能整体移动，所以只把入口 `Win64\dwmapi.dll` 登记成自定义 mod 来开关（已实施，见 §1.4） |
| Better dodge / BossRushV3 / 更强金箍棒 / TrueDasheng | 55 / 668 / 291 / 426 | 只有网盘或无镜像 | — | 只能从 Nexus 下载（需登录） |

⚠ 兼容性：多数热门 mod 最后更新于 2024 年，用户游戏是 1.0.21（2026）。默认不启用没有风险；启用后需逐个进游戏验证。

## 1.4 已装入的热门 mod（2026-10-07，全部默认禁用）
用户最终选定 9 个（光剑不要；功能重复的只留一个：闪避留 Nexus Better dodge、地图留大地图）。清单、哈希、安装位置、冲突与卸载方法见 `popular-mods/README.md`（开发者本地目录，第三方 mod 不随公开仓库发布）。
要点（做法以后可复用）：
- **框架类**（UE4SS）：本体 `Win64\ue4ss\` 常驻启用位置（没有入口 dll 就不会加载），只把入口 `Win64\dwmapi.dll` 放进禁用仓库并登记为自定义 mod。
  `ModLayout.UE4SSBuiltinMods` 已补上黑神话版自带的 AllowModsMod、KismetDebuggerMod；Mod 页默认隐藏框架内置组件。
  `FrameworkStatus.UE4SSDisabled`：入口在禁用仓库时显示"UE4SS：已安装（在管理器里禁用）"。
- **跨两处位置的 mod**（UE4SS 脚本文件夹 + LogicMods/~mods 里的 pak，如全图筋斗云、真大圣）：两个路径合并成一个自定义 mod，避免只开一半。
  这类 mod 自带 `enabled.txt`，这版 UE4SS 会加载，不用改 `mods.txt`。
- **装入方式**：`popular-mods/install/install.cs`（本地工具）直接把要开关的文件复制进禁用仓库（不经过启用位置）→ 扫描确认为禁用 → 写元数据；出错只回滚当前 mod。
- pak 放在 `~mods` 的子文件夹里也会被引擎挂载（UE 递归挂载 Paks 下所有目录），管理器把 `~mods\<文件夹>` 识别为一个 pak 文件夹 mod。

## 2. 启用/禁用机制（设计决策）
**统一做法：禁用 = 把 mod 的文件移动到管理器的禁用仓库；启用 = 移回原位。**
- 禁用仓库：`<b1>\ModManager\Disabled\`，其下**保留相对 `<b1>` 的原始路径**
  例：`<b1>\Content\Paks\~mods\Foo_P.pak` → `<b1>\ModManager\Disabled\Content\Paks\~mods\Foo_P.pak`
- 为什么不在 Paks 里建 `~mods_disabled`：UE 会**递归挂载 `Content\Paks` 下所有 pak**，放在 Paks 子目录里仍会被加载。
- 为什么不改扩展名：移动是通用方案，不依赖各加载器的规则（UE4SS 的 enabled.txt/mods.txt、CSharpLoader 的目录约定等），也不会在游戏目录里留下 `.disabled` 垃圾文件。
- 同一卷内移动 = 重命名，几 GB 的 pak 也是瞬间完成。
- **状态以文件实际位置为准**：在加载目录里 = 已启用；在禁用仓库里 = 已禁用；两边都有 = 冲突（用户在禁用期间又装了一份），由用户选择保留哪个。元数据库只存备注，不存启停状态，避免不同步。
- 切换是事务性的：一个 mod 有多个文件时，任一步失败则回滚已移动的文件。
- 游戏进程 `b1-Win64-Shipping.exe` 运行时禁止切换（文件被占用，且运行中改动不会生效）。
- UE4SS mod 在 `mods.txt` 里的条目：移走文件夹后 UE4SS 只会提示找不到，无害；不改 mods.txt。

## 3. 元数据
`<b1>\ModManager\mods.json`：以 mod ID（类型 + 相对路径）为键，存显示名、备注、来源链接、标签、首次发现时间等。

## 4. 原版 Paks 目录清单（✅ buildid 21393610）
`filehash.dat`，`pakchunk{0..17,20}-Windows.pak`，`pakchunk23-Windows-WinStoreSteam.pak`，`pakchunk26-Windows_P.pak`，每个 pak 都有同名 `.sig`。管理器**绝不移动带 .sig 的原版 pak**。

## 5. Pak 索引与资源冲突检测（2026-10-07 会话 4）
管理器只读 pak 的**索引**（不解压内容），列出每个 pak 改了哪些游戏文件，找出改了相同文件的 mod（`Core/Mods/PakIndexReader.cs`、`PakConflicts.cs`）。
- 文件尾（FPakInfo）：`[加密密钥 GUID 16][索引已加密 1][Magic 0x5A6F12E1][Version int32][IndexOffset int64][IndexSize int64][SHA1 20][压缩方法名 32×5]`。
  **黑神话的 pak 在 Version 后面多 2 个字节**（值 0x0010），所以文件尾是 223 字节；读取时两种布局都试，取 offset+size 落在文件内的那种。
- 版本 11（UE5 "Fnv64BugFix"）：主索引 = 挂载点 FString、条目数、PathHashSeed、[路径哈希索引 offset/size/hash]、[完整目录索引 offset/size/hash]、编码条目…；
  文件名在**完整目录索引**里：`int32 目录数 { FString 目录; int32 文件数 { FString 文件名; int32 条目位置 } }`，完整路径 = 挂载点 + 目录 + 文件名（去掉开头的 `../../../`）。
  FString：正长度 = Latin-1（含结尾 0），负长度 = UTF-16（含结尾 0），中文文件名是 UTF-16。
- 原版 pakchunk 的索引是加密的（读不了，也不需要）；mod 的 pak 不加密。
- 比较时同一资源的 `.uasset/.uexp/.ubulk/.uptnl/.umap` 合并成一项；忽略 `AssetRegistry.bin`。缓存键 = 文件名 + 大小 + 修改时间（启停只移动文件，命中缓存）。
- 黑神话的数据表是 `b1/Content/00Main/PBTable/{Runtime,NoneRuntime}/*.data`（protobuf），改数值的 mod 一般整张替换这些表，所以两个改同一张表的 mod 必然冲突。
- 实测（2026-10-07）：天命演武 ⟂ 更强的金箍棒（FUStBuffDesc-Talent / FUStSkillSMappingDesc / TalentSDesc 3 张表）、天命演武 ⟂ 真大圣（FUStTransQiTianDaShengConfigDesc），与人工核对一致。
- 命令行：`wmm pak <文件.pak>` 列出文件；`wmm mods conflicts` 列出冲突。
