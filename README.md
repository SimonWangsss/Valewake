# Valewake

Valewake 是一款面向《星露谷物语》玩家的 AI NPC Mod。它让鹈鹕镇居民能够在保留原有人设和游戏关系的基础上，与玩家进行连续自然语言对话、记住彼此的互动，并在愿意且条件合适时协助农场工作或陪伴玩家下矿。

当前版本为 `1.1.0`。玩家仍然可以像往常一样右键与 NPC 交谈：Mod 默认保留当天首次原版台词，之后再次互动即可继续自由输入。NPC 会参考当前地点、时间、关系阶段和共同记忆作出回应，不会无条件知道视野之外发生的事情。

## v1.1.0 有什么新内容

- **完整的约会互动**：通过对话商定时间和地点，确认后到点会面。走近后可同行；告别或点“结束约会”，NPC 会尝试返回当前日程。相较早期的预约见面雏形，这是新增的完整互动流程。
- **更可靠的安排与移动**：修复改约时误用旧时间、台词与确认时间不一致、邀约答应却没弹确认框等问题。加入会面落点检查、掉队恢复与返程重试；可在确认后从约会转入下矿。
- **共同经历与今日回顾**：增加今日对话记录；NPC 能参考更新后实际收到的礼物、双方实际看过的原版剧情台词和已选回答，不追溯升级前未记录的内容。
- **可自定义 NPC**：通过游戏内部名给原版或扩展 NPC 添加人格和专属知识，无需修改内置资料。聊天输入现在支持较长文本，默认上限 600 字。
- **Windows 升级工具**：保留旧记忆、日志及模型配置，不覆盖新版知识库。

原有农场浇水、除草、砍树，以及矿洞同行、防御和采矿仍可使用。NPC 的行动需要通过游戏规则检查和玩家确认。

[完整更新日志](CHANGELOG.md) · [English Release Notes](docs/release_notes_v1.1.0.md)

## 玩家功能

- 与全部原版社交 NPC 连续对话，不受固定台词选项限制。
- 每个存档、每位 NPC 分别保存记忆，NPC 可以在之后的对话中想起重要互动和玩家偏好。
- NPC 的语气会随原版好感、恋爱或婚姻关系变化，并通过原版肖像显示相应表情。
- 对话可以影响原版好感度；增加或降低时会显示克制的 HUD 提示。
- 可以用自然语言请 NPC 浇水、除草、砍树，或邀请 NPC 陪同下矿、战斗和采矿。
- 可以与 NPC 商定未来的见面时间和地点。
- 所有实际行动都会检查关系、时间和游戏状态，并在执行前由玩家确认。
- 后端随 Mod 自动启动，正常游玩不需要另外打开终端。

## NPC 可以做什么

玩家可以直接在对话中提出请求。NPC 会根据性格和关系决定接受或拒绝；接受后还会经过游戏条件检查和玩家确认，避免对存档进行未授权操作。

当前可执行能力包括：

- 农场浇水。
- 清除杂草。
- 有界砍树，默认最多处理 `3` 棵成熟且未安装树液采集器的农场树木。
- 跨地图派工、离屏继续工作、日程占用与恢复、失败恢复。
- 矿洞同行、跨地图与换层跟随。
- 防御玩家，只攻击附近敌对怪物。
- 光标指定矿石、附近采矿和资源偏好探险。
- 未来时间与地点的约见提案。

农场任务默认要求 NPC 至少 `2` 心，矿洞同行默认要求至少 `4` 心。年龄、关系、信任、时间、事件状态、多人模式主机权限以及功能开关都是不可通过反复请求绕过的硬条件。对于满足硬条件但被 NPC 主观拒绝的同类请求，默认第 `3` 次会接受。

## 安装

### 普通玩家

1. 使用 Windows，安装《星露谷物语》1.6 和 [SMAPI 4.0 或更高版本](https://smapi.io/)。
2. 从 [GitHub Releases](https://github.com/SimonWangsss/Valewake/releases) 下载 Valewake 的 Mod 压缩包，不要下载 GitHub 自动生成的 `Source code` 压缩包。
3. 解压到游戏的 `Mods` 目录，最终结构应为：

```text
Stardew Valley/
└─ Mods/
   └─ Valewake/
      ├─ manifest.json
      ├─ Valewake.dll
      ├─ config.json
      ├─ Backend/
      └─ data/
```

4. 配置模型 API，然后通过 SMAPI 启动游戏。

### 配置模型

推荐安装可选 Mod [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098)，在游戏设置中选择服务商、填写 API Key 和模型名。也可以退出游戏后直接编辑：

```text
Stardew Valley/Mods/Valewake/config.json
```

默认配置使用 DeepSeek：

```json
{
  "LlmProvider": "deepseek",
  "LlmApiKey": "你的 API Key",
  "LlmModel": "deepseek-v4-flash",
  "LlmApiBase": ""
}
```

当前内置服务商配置包括 DeepSeek、OpenAI、Anthropic Claude、通义千问、Google Gemini、OpenRouter，以及兼容 OpenAI 接口的自定义或本地服务。模型名称和账号额度由对应服务商决定。

也可以在 SMAPI 控制台输入 `agent_setkey <api-key>` 快速写入密钥。该方式可能让输入内容出现在本机控制台历史中，因此共享日志或录屏前请检查敏感信息。不要提交包含真实密钥的 `config.json`。

## 从旧版升级

1. 关闭游戏、SMAPI 和旧后端，备份游戏存档。将旧 `Mods/Valewake` 整个文件夹移到你自己选择的、位于 `Mods` 之外的备份位置（例如桌面），保留它供下一步迁移使用。不要删除旧文件夹，也不要只在 `Mods` 内改名。
2. 解压干净的新版本到 `Mods/Valewake`，**先别启动游戏**。
3. 双击新版目录的 `Upgrade-Valewake.cmd`。选择旧 Mod 根目录、其 `data` 目录，或只包含一份旧 Mod 的外层解压目录。核对 `OLD source` 与 `NEW destination`，看到 `Migration completed` 再启动。

| 会迁移 | 不会覆盖或迁移 |
| --- | --- |
| `data/memory`，包括当前文件与 committed 检查点 | 新版 RAG、内置 NPC 人格和会面点 |
| `data/traces` 与标注、自建 `data/custom_npcs` | 程序、旧后端路径、测试开关等其他设置 |
| 旧 `config.json` 的服务商、API Key、模型及 API 地址 | `.env` 或 `llm_config.json` 独有的配置，需要重新填写 |

只选独立的 `data` 备份也可导入个人记录，但没有 API 配置。遇到两份旧数据或新版已有冲突记录，工具会停止，不自动合并；旧目录不被修改。好感等游戏状态仍依赖原存档，Trace 本身不是 NPC 记忆。手动迁移、预览和冲突处理见[升级指南](docs/upgrading_zh.md)（包内 `UPGRADE_zh.md`）。

## 自定义 NPC

加载存档后，在 SMAPI 控制台输入 `agent_npcs`，查找 NPC 内部名。将 `data/custom_npcs/_template` 复制成新目录，编辑并启用 `pack.json`，可选加 `lore.jsonl`，然后重启。详情见[人物包教程](docs/custom_npc_packs_zh.md)。

扩展角色需要先由游戏加载且允许社交。能接入聊天不等于扩展地图、日程和行动已经兼容；SVE 尚未完成实机验证。

## 限制、隐私与反馈

- 普通约会同行目前仅覆盖镇上、海滩、山地、森林和农场等支持的户外地图。钓鱼主题不会让 NPC 自动钓鱼；改图、特殊桥梁、多人与少数返程路线仍需验证。
- AI 用的是有限的文本状态，不读玩家屏幕。采用外部模型 API 时，对话与必要上下文会发送给所选服务商；Memory 和 Trace 保存在本地。
- 已知有退出时后端清理报红的报告。发布包内含 Windows 后端可执行文件；若安全软件提示异常，先核对来源与发布校验值，不建议关闭防护。
- 反馈请附复现步骤、NPC、地图、游戏内时间和相关 SMAPI/Trace 片段；分享前去除密钥和私人内容。

## 游戏内使用

靠近任意受支持的社交 NPC，右键完成当天首次原版对话；原版文本结束后再次右键，即可打开 Valewake 输入框并连续交谈。

可以直接用自然语言提出任务，例如：

```text
你能帮我给农场的作物浇水吗？
能帮我清理农场里的杂草吗？
陪我下矿，优先找铁矿。
保护我，帮我打附近的怪物。
明天傍晚六点在海边见面吧。
```

通过本地规则验证的行动提案会弹出原生 Yes/No 确认框。接受后，农场任务进入持久 Job Queue；矿洞请求进入同一次连续探险，由本地控制器负责跟随、战斗和采矿。矿洞探险中可将鼠标放在可开采节点附近并按 `G`，将其设为优先目标。

常用 SMAPI 控制台命令：

| 命令 | 用途 |
|---|---|
| `agent_npcs` | 导出当前 NPC 内部名，用于自定义人物包 |
| `agent_state` | 输出当前游戏状态与 NPC 感知快照 |
| `agent_chat <内容>` | 与附近 NPC 对话的控制台备用入口 |
| `agent_jobs` | 查看活动中的农场任务 |
| `agent_cancel_jobs` | 取消所有任务并恢复 NPC 日程 |
| `agent_expedition` | 查看当前矿洞探险状态 |
| `agent_end_expedition` | 结束探险并让 NPC 进入返程 |
| `agent_mine_target` | 将光标所在位置设为优先采矿目标 |
| `agent_mark <标签> [备注]` | 标注最近一轮 AI 交互 |
| `agent_setkey <密钥>` | 写入并同步模型 API Key |

可用标注标签：`keep`、`reject`、`boundary`、`memory_good`、`memory_bad`、`action_good`、`action_bad`。

更完整的任务限制与结束条件见 [docs/action_catalog.md](docs/action_catalog.md)。

## 数据与隐私

- 对话记忆按存档与 NPC 隔离，保存在本机 Mod 数据目录中。
- Trace 会记录模型输入链路、检索结果、记忆变化、关系判断、Action Proposal、执行结果和延迟信息。
- 存档保存时提交本次会话数据；未保存退出时会记录 rollback 边界，避免把未发生的游戏进度当作已提交事实。
- `agent_mark` 可给最近一轮添加人工标签，供后续筛选真实对话数据集。
- 使用外部模型时，对话及必要上下文会发送给对应服务商。不要提交 API 密钥或私人游戏数据。

游戏记录字段、存档提交语义与导出方式见 [docs/playtest_dataset.md](docs/playtest_dataset.md)。

## 开发与构建

开发环境需要 .NET SDK 6 或兼容 SDK、Python，以及已经安装 SMAPI 的《星露谷物语》。本机游戏路径在 `Valewake/Valewake.csproj` 的 `GamePath` 中配置。

仅构建 C# Mod：

```powershell
cd Valewake
dotnet build Valewake.csproj
```

从仓库根目录执行完整发行构建：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package_portable_test.ps1 -Label v1-1-0
```

该脚本构建 Windows 后端和 Release Mod，生成白名单 ZIP，不覆盖本机游戏安装。

## 使用文档

- [任务能力、限制与结束条件](docs/action_catalog.md)
- [Playtest JSONL 字段与导出命令](docs/playtest_dataset.md)

## 参考与边界

本项目研究了 ValleyTalk、StardewGPT、stardew-mcp、SMAPI，以及游戏 Agent 领域的相关项目。参考内容用于理解 SMAPI 生命周期、对话接入方式、状态抽象和任务控制思路；Valewake 的记忆、检索、边界策略、评估链路与行动控制器均在本仓库中独立实现。第三方参考仓库不随本项目分发。
