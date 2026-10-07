# 存档格式（逆向记录）

> 基于 Steam buildid **21393610**（appmanifest LastUpdated 2026-01-15）的存档分析。游戏更新后需复核。
> 证据来源：用户自己的存档 + 游戏自动备份（`SaveGamesBackup`，2026-09-26 ~ 2026-10-06 共 18 个时间点）。
> 研究脚本见 `research/`。字段路径写法：`.1.1.2.1` = 顶层 field 1 → field 1 → field 2 → field 1。

## 1. 文件与目录
```
<游戏>\b1\Saved\SaveGames\<SteamID>\
  ArchiveSaveFile.1.sav      槽位 1（约 2.3MB）
  ArchiveSaveFile.2.sav      槽位 2
  ShareArchiveSaveFile.sav   跨槽位共享数据（120B）
  UserSettingSaveGame.sav    设置
  steam_autocloud.vdf        说明 Steam 云存档开启
<游戏>\b1\Saved\SaveGamesBackup\
  01RealtimeBackup\<时间>\  02HourlyBackup\<时间>\  03DailyBackup\<日期>\  04ArchiveFixBackup\
```
游戏自己会做多级备份（每份是整个 SteamID 目录的拷贝）。

## 2. 外层结构（已确认）
整个 `.sav` 是一个 protobuf 消息：
| field | 类型 | 含义 |
|---|---|---|
| 1 | message | **存档头**（明文，见 §3） |
| 2 | bytes | **存档主体**（XOR 加密的 protobuf，见 §4） |

## 3. 存档头 (field 1) = `ArchiveB1.ArchiveInfo`
字段名来自游戏程序集 `Protobuf.RunTime.dll`（2026-10-07 用 `research/ILScan` 读取）。
| field | 类型 | 示例 | C# 名 / 含义 |
|---|---|---|---|
| 1 | string(32 hex) | `7ef2b52c46c0...` | **`Md5Str` 校验值** ✅ 算法见 §3.1，读档时校验，不符 → 错误码 3"存档完整性校验失败，数据已损坏或被篡改" |
| 4 | varint | 4 → 2220 | `ArchiveVersion`（随每次保存递增，推测为保存次数） |
| 5 | varint | 1791300673 | `LastSaveTime`：Unix 时间戳，最后保存时间 |
| 6 | varint | 1790393691 | `CreateTime`：存档创建时间 |
| 7 | varint | 14 | `ProtocolTag`（读档时 `CheckFixArchiveData` 可能把它设为 14） |
| 8 | varint | 1 | `EnableEncrypt`：主体是否 XOR 加密 |
| 9 | string(uuid) | | `ArchiveUid` |
| 10 | varint | 23831 | `CreateBuildRevision`：创建存档时的游戏 build 号（游戏版本 1.0.21.23831） |
| 11 | varint | 23831 | `SaveBuildRevision`：最后保存时的 build 号。**读档时若游戏 build < 该值 → 错误码 1** |
| 12 | string | SteamID | `SdkUid` |
| 13 | string(uuid) | | `SharedArchiveUid`，与 ShareArchiveSaveFile `.1.1` 相同 |

### 3.1 校验值算法（✅ 2026-10-07 从游戏代码确认，80 个存档中 79 个吻合，唯一不符的是我们改坏的那份）
```
Md5Str = lowercase_hex( MD5( 加密后的主体字节 ‖ UTF8("lhx2tkh6lj1wj8jmrgs3k1xb2brusehx") ) )
```
- 来源：`b1.BGW_GameArchiveMgr::PackArchiveFileData` → `CalcByteArrayMD5(SerializeArchiveDataToBytes(EnableEncrypt, data), ARCHIVE_MD5_MAGIC_SALT)`；
  读档 `ReadArchiveData` 用同一函数比对（`ARCHIVE_CHECK_MD5_FLAG = true`）
- 盐 `ARCHIVE_MD5_MAGIC_SALT = "lhx2tkh6lj1wj8jmrgs3k1xb2brusehx"`（`BGW_GameArchiveMgr..cctor`）
- XOR 密钥 `ARCHIVE_XOR_ENCRYPT_MATIC_KEY_LONG = BitConverter.GetBytes(4024806562674138235L)` = `7B 5C DA 91 3E FC DA 37`（与 §4 逆向结果一致）
- ShareArchiveSaveFile 用同一个盐（`PackShareArchiveFile` / `UnpackShareArchiveFile`）
- 实现：`src/WukongMM.Core/Saves/SaveChecksum.cs`，`SaveFile.ToByteArray()` 在主体改动后自动重算

### 3.2 读档时的检查顺序（`b1.BGW_GameArchiveMgr::ReadArchiveData`）
1. `LoadArchiveFileSync` 读文件失败 → 返回其错误码
2. 版本：`GSVersionUtil.GetBuildRevision() < ArchiveInfo.SaveBuildRevision` → 1
3. MD5：`CalcByteArrayMD5(GameArchivesDataBytes, salt) != Md5Str` → **3**
4. 解密 + 反序列化失败 → 2
5. `ArchiveProtoVersionCheck.CheckFixArchiveData`（协议版本修正）
6. `MergeShareArchiveData`（合并共享存档）
7. `RoleDataValidCheck.CheckRoleDataValid(RoleData.RoleCs)`：不通过 → `TryRepairArchive`（成功则写 `04ArchiveFixBackup`）→ 仍不通过 → **4**
   详细规则见 §8（多 agent 分析结果）

## 4. 主体加密（已确认）
- 算法：**8 字节循环 XOR**，密钥 `7B 5C DA 91 3E FC DA 37`
- `plain[i] = cipher[i] XOR key[i % 8]`，i 从 field 2 的 payload 第一个字节开始计
- 两个槽位、所有备份都用同一密钥 → 是固定密钥，与账号无关
- 解密结果是标准 protobuf（无压缩）
- ShareArchiveSaveFile.sav 结构类似：`{1:{1:uuid, 2:32hex, 3:1}, 2:bytes(XOR)}`

发现过程：周期自相关在 8 的倍数处有峰值 → 按位置 mod 8 做频率分析，得到每一位最常见的密文字节 → 再异或 0x30 修正，得到正确密钥（明文立刻出现中文和 UGuid 字符串）。

## 5. 主体 protobuf 结构（= `ArchiveB1.FUStBEDArchivesData`）
顶层：`.1` = `RoleData`（角色数据）、`.2`~`.4`、`.6` 世界/关卡/单位状态，`.5` = `NeverPlaySequences`（packed int32）。
**权威完整字段表**（93 个类型，从游戏 `Protobuf.RunTime.dll` 的 WriteTo IL 提取）：`research/notes/protobuf-field-map.txt`（生成脚本 `research/notes/pbmap.py`）。
关键名称：`.1.1` = `RoleData.RoleCs`(`RoleDataCS`)；`.1.1.1` = `RoleBase`；`.1.1.2` = `RoleBag`；`.1.1.3` = `RoleActor`；`.1.1.3.2` = `ActorProgress`；`.1.1.6` = `RedPoint`。

### 5.1 `.1.1.1` 角色基本信息
| field | 类型 | 示例 | 含义 |
|---|---|---|---|
| 1 | fixed64 | 10000 | 未知（恒定，疑似角色 ID） |
| 2 | string | （玩家名） | 名称（疑似玩家昵称） |
| 4 | varint | 92 | **等级** ✅ 已确认（1→17→24→41→60→…→92 随时间单调增长） |
| 11 | varint | 14 | 未知 |

### 5.2 `.1.1.2` 背包
| 子路径 | 结构 | 含义 / 证据 |
|---|---|---|
| `.1.1.2.1` | repeated `RoleMoney {1: MoneyType, 2: MoneyValue(int64)}` | **灵蕴** ✅ 游戏代码确认：`MoneyType` 只有 `Spirit = 1`（灵蕴）。2026-10-07 实测改值后游戏正常读取 |
| `.1.1.2.2` | repeated `RoleItem {1: ItemId, 2: Num, 3: StoreNum}`（均 int32） | **普通道具** ✅。`StoreNum` = 超出携带上限后的仓储数量（只对可仓储丹药 ConsumeType=4 有效，休息时补回 Num），不是"未查看计数" |
| `.1.1.2.3` | repeated `{1: 装备ID, 4: 获得序号, 5: packed}` | 装备类（无数量，唯一） |
| `.1.1.2.4` | repeated `{1: id, 2: 值}` | id 1003/1004/1005/1156~1162，值 1→4 缓慢增长：疑似葫芦/酒/泡酒物等的等级或数量（待确认） |
| `.1.1.2.5` | repeated，含子列表 | 未知（id 2006/2009/2010/2019…） |
| `.1.1.2.6` | `{1, 2, 3, 4, 6, 7}` | 计数器：1 = `.3` 的下一个获得序号，2 = `.7` 的下一个获得序号 |
| `.1.1.2.7` | repeated `{1: id, 2: 获得序号}` | 未知类别 |

### 5.3 `.1.2` 统计
- `.1.2.1.1.2`（float）= 总游戏时长（秒），例 86588
- `.1.2.1.3.2` = 按关卡的游戏时长（`LevelStaticsDataOne`），`.1.2.1.3.3` = 按章节的游戏时长（`ChapterStaticsDataOne`）

### 5.4 `.1.1.3.2` 成长 / 经验（✅ 已确认；C# 名 `ActorProgress`：1 `Xp`，2 `TalenPoint`（灵光点，游戏原拼写），3 `SpellList`，4 `TalenList{Id,Level}`，7 `XpTotal`，8 `LegacyList`，9 `AwardMeditationList`，11 `MeditationsList`，12 `AwardTalentPointMeditationList`）
| field | 类型 | 示例 | 含义 |
|---|---|---|---|
| 1 | varint | 4695 | **当前等级内经验**（升级后扣掉升级所需，余数保留）✅ |
| 2 | varint | 3 | **未分配灵光点**（高可信：0~5 间波动，升级增加、加点减少） |
| 3 | packed varint | | 未知（疑似已学天赋列表） |
| 4 | repeated `{1: 天赋ID(100101…), 2: 等级}` | | 天赋/技能等级（推测） |
| 7 | varint | 410914 | **累计总经验** ✅（单调增长；与 field 1 同步增加） |
| 8 | repeated | | 未知 |
| 9, 12 | packed varint | | 未知（同内容，含 1006/2002…/5002） |
| 11 | repeated `{1: id}` | | 未知 |

证据（`research/xp_timeline.py`）：`总经验 − 当前经验` 在同一等级下恒定 = 升到该等级的累计所需经验 S(L)：
| L | S(L) | 升级所需 req(L)=S(L+1)−S(L) |
|---|---|---|
| 87 | 355090 | 9905 |
| 88 | 364995 | 10068 |
| 91 | 395677 | 10542 |
| 92 | 406219 | — |

**等级表（✅ 2026-10-07 02:21 由导出 mod v0.2 导出，`<b1>\ModManager\GameData\levels.json`）**：Id 1~342 连续，最高 342 级；
NextLevelExp 从 342（1 级）增长到 38912（342 级，最后一行也不为 0）；每级 NextLevelGainTalent 都是 1（1→342 共 341 点）。
用它算出的 S(87/88/91/92) 与上表完全一致，且 **54 个真实存档（含 SaveGamesBackup）全部满足 `XpTotal − Xp = S(Level)`**（`wmm info` 最后一行可查）。

修改策略（已实现，见 §8.3 末尾与 `ProgressPlanner`）：
- 改"当前经验"时，总经验同步加同样的差值（保持 S(L) 一致）；当前经验 ≤ NextLevelExp(L) − 1
- 改等级：累计经验按 S(新等级) − S(原等级) 同步；可选补发灵光点（每级 1 点）
- 游戏从不按总经验重算等级；当前经验超过本级所需时登录会压回 NextLevelExp − 1，不会自动升级（IL 已确认，§8.2）

### 5.5 存档头 field 4
不是经验（经验不变时它也在增长，例 20:45→22:12 经验不变，2150→2161）。含义未知，写回时保持原值。

## 6. 写回存档的要求（✅ 已实现并经游戏实测）
1. 无损：未修改的字段必须原样保留（字段顺序、未知字段、packed 数据）。实现：wire 级解析树，只替换被改的叶子，重算各层长度前缀
2. 解析后不修改直接序列化必须与原文件逐字节一致（`wmm roundtrip` 测试）
3. **主体改动后按 §3.1 重算 `Md5Str`**（`SaveFile.ToByteArray()` 自动完成）；不要改存档头其他字段（`SaveBuildRevision` 不能超过游戏 build，`EnableEncrypt` 保持 1，`ProtocolTag` 保持 14）
4. 写入前备份；游戏运行时拒绝写入（游戏只在启动时在标题画面读一次各槽位并缓存结果，运行中改存档无效且会被游戏覆盖）
5. 临时文件不要放在存档目录：游戏自动备份会把目录里名字含 `.sav` / `ArchiveSaveFile.N` 的文件都拷走（`SaveWriter` 用 `<b1>\ModManager\Temp`）
6. 2026-10-07 实测：灵蕴 44519→54519 + 重算校验值 → 游戏正常读档；游戏随后自己保存时沿用了修改后的值

## 7. 道具 ID → 名称（✅ 已解决）
- 网上没有公开道具表；pak 为 AES 加密索引 + Oodle 压缩（`research/pakinfo.py`），没走这条路
- **方案**：自写游戏内 CSharpLoader mod `mods/WukongMMDataExport`，读档后在游戏线程读取
  `GSProtobufRuntimeAPI<TBItemDesc, ItemDesc>.Get().GetAll()`，名称用本地化键 `ItemDesc.{id}.Name`
  → `GSLocalization.GetLocaliztionalFText(key, false)`，导出到 `<b1>\ModManager\GameData\items.json`
  （v0.2 另导出 `levels.json`：`PlayerLevelDesc{Id, NextLevelExp, NextLevelGainTalent}`；items 增加 `isLevelItem`、`consumeType`）
- 2026-10-07 实测导出 952 条（zh-Hans），与游戏代码里硬编码的 `RoleDataValidCheck/TableIdWhiteList.ItemDescIdList`（`research/notes/tableidwhitelist.json`）完全一致
- `ItemDesc.TypeName` 的本地化键取不到翻译，管理器改用 `ItemType` 枚举显示类别
- 用户备注（`%APPDATA%\WukongMM\item_names.json`）优先于游戏表

## 8. 读档检查与编辑规则（多 agent 工作流 wf_280bef9f-778 的结论，IL 证据见 `research/notes/`）
完整规格：`research/notes/save-edit-spec.md`；各分析原始结论：`research/notes/save-validation-findings-raw.txt`。

### 8.1 读档时会不会拒绝（错误码 = 弹窗【N】里的数字）
| 码 | 含义 | 触发 |
|---|---|---|
| 1 | AppRevisionTooLow | 游戏 build < `SaveBuildRevision` |
| 2 | DeserializeArchiveFailed | 外层/主体 protobuf 解析失败（长度前缀、varint 错） |
| 3 | CheckMd5Failed | `Md5Str` 不符（2026-10-07 第一次实验就是这个） |
| 4 | RoleDataInvalid | `CheckRoleDataValid` 失败且 `TryRepairArchive` 修不好 |
| 10 | Unknown | 文件不存在/读不了 |

- `CheckRoleDataValid` **只检查各种 ID 是否存在于数据表**（ID 为 0 跳过），**从不读取**等级、灵蕴、经验、总经验、灵光点、道具数量。所以这些数值修改不会导致读档失败
- `TryRepairArchive` 只能修三类：背包道具 ID、装备 ID、天赋 ID（不在表里就删掉，并写 `04ArchiveFixBackup`、弹"存档已修复"确认框）；其他列表的无效 ID → 错误码 4
- 不能删掉/清空的子消息（校验时无空检查会抛异常）：`RoleDataCS.Bag`(2)、`Actor`(3).`Progress`、`RedPoint`(6)、`Actor.Wear.WearSoulSkill`(.1.1.3.1.9)、`WearAccessory`(.1.1.3.1.10) 等 —— 本工具无损编辑不会碰它们

### 8.2 登录后游戏会静默修正的（`CsB1.RoleSvc::LoginCheckFixRoleData`）
- 当前经验 `Xp` ≥ 本级 `NextLevelExp` → 改成 `NextLevelExp - 1`（不会升级；升级只在下次获得经验时发生）
- 等级不在 `PlayerLevelDesc` 表里（0、负数、超过最高级）→ `Xp` 置 0，且以后**获得经验全部无效**（再也升不了级）
- 没有灵蕴条目 → 自动补一个 `{1:1, 2:0}`
- `IsLevelItem` 的道具每次登录都会被清掉（`PlayerBag::ClearLevelItem`）

### 8.3 编辑器应遵守的规则
| 修改 | 规则 | 不遵守的后果 |
|---|---|---|
| 等级 | 1 ≤ L ≤ 等级表最大 Id（`levels.json` 最后一行） | 经验永久失效 |
| 等级 | 改等级**不会**自动给灵光点；每升一级游戏给 `NextLevelGainTalent(L)` 点 → 编辑器可选"按等级表补发灵光点" | 只是少点数 |
| 当前经验 | 0 ≤ Xp < `NextLevelExp(L)` | 登录时被压回 `NextLevelExp-1` |
| 总经验 | 纯展示/统计，游戏从不用它重算等级；建议保持 `XpTotal = S(L) + Xp`（S 由等级表累加） | 无 |
| 当前经验 | 不能为负（登录修正不处理负数，负值会一直留着） | 异常 |
| 灵光点 | 无校验；**建议 ≤ 9999**（游戏升级时 int32 无检查累加） | 接近 int32 上限再升级会溢出为负 |
| 灵蕴 | **建议 ≤ 999,999,999**（存储 int64，但商店/UI 按 int32 读取，且获得灵蕴不设上限）——界面已按此限制 | 接近 21 亿时再捡就溢出，购买判断变负数 |
| 总经验 | 建议远低于 2^31 | 获得经验时 int32 溢出 |
| 道具数量 | 0 ≤ Num ≤ `CarryMax`（0 = 无上限；可仓储丹药 ConsumeType=4 例外：超出部分进 StoreNum） | 超上限不会被改，但之后**捡不到、买不到**该道具 |
| 道具仓储 `StoreNum` | 在神龛休息时，游戏把 StoreNum 补进 Num（**任何 CarryMax>0 的道具**，不只丹药，`PlayerBag::RefillBagItem`）。降低 Num 时注意原有 StoreNum 会被补回来；新条目 StoreNum 必须为 0 | 改少的数量被补回 |
| 新增道具 | ID 必须在道具表（以游戏导出的 items.json 为准）；不能是 0；已存在就改数量（不要重复条目）；`StoreNum` 置 0 | 不在表里 → 读档时被自动删除 + 修复弹窗 |
| 新增道具 | 允许的类型（住在背包道具列表）：材料(3)、消耗品(4，不含酒 ConsumeType=1)、酒升级(9)、葫芦升级(10)、配方(11)、催化剂(13)、古董(15)、珍玩(16)、地图(17)、人种袋(21)、解锁传承天赋(22)；**禁止**：灵蕴(1)、经验(2)、装备(6)、属性道具(7)、法术(8)、灵光点(12)、传承(14)、精魄(18)、20/23/24 —— 它们在别的列表，计数被游戏忽略 | 无效 |
| 新增道具 | **种子(19)**：背包里只要有条目（哪怕数量 0），以后就不会再掉落这种种子（`IsSeedDropAlready`）→ 禁止或强警告 | 永久拿不到掉落 |
| 新增道具 | **任务道具(5)**：任务阶段条件按背包数量判断，加/改任务道具可能推进或跳过任务阶段 → 禁止或强警告 | 任务流程异常 |
| 新增道具 | 不要加 `isLevelItem = true` 的道具 | 登录即被清除 |
| 新增道具 | 直接改存档不会触发获得道具的副作用（成就、红点、自动快捷栏） | 成就要等游戏内正常获得才触发 |

**编辑器落实情况（2026-10-07 会话 2）**：以上规则已全部在 Core 实现（`ProgressPlanner`、`ItemRules`，见 ARCHITECTURE §3.2.1）并接入存档页：
等级限制在等级表范围内、当前经验 ≤ NextLevelExp−1（未手动改经验时自动压回）、累计经验按 S(L) 同步、可选补发灵光点（出发等级行的 NextLevelGainTalent 之和）、
灵光点 ≤ 9999、累计经验 ≤ 20 亿、灵蕴 ≤ 999,999,999；新增道具：不在表里 / ID≤0 / 类型 1,2,6,7,8,12,14,18,20,23,24 / 关卡道具 / 酒 → 禁止，种子 / 任务道具 / 旧表消耗品 → 警告；
超携带上限（可仓储丹药除外）→ 确认框提醒；"仓储"列可编辑，减少数量但仓储仍有存货时提醒会被休息补回。新增条目不写 StoreNum。

### 8.4 复核补充（verify:completeness / verify:correctness，2026-10-07）
- 两个复核 agent 均判定"规格可以安全实现"，MD5 流程、XOR、错误码、字段表全部确认；原文见 `research/notes/save-edit-spec-verification.txt`
- 用户看到的【3】确认就是 `CheckMd5Failed`（本地化文本"存档完整性校验失败，数据已损坏或被篡改"）
- 标题画面只缓存"槽位摘要"；真正读档时会重新从磁盘读取并重新校验（读档阶段失败时【N】显示的是加载状态码，不是 1/2/3/4）
- 登录时还会：用平台用户名覆盖 `RoleBase.Name`、重算套装天赋、刷新花园配置——都不碰我们编辑的字段，但游戏重新保存后文件差异会多于我们改的叶子
- 游戏零售版日志不会记录 MD5 失败/修复，诊断只能看弹窗【N】或离线重算 MD5（`wmm info`）
- 外层文件必须正好一个 field 1、一个 field 2（游戏 MergeFrom 取最后一个 field 2）；当前所有存档都满足
