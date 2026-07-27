# Minecraft LLM Agent 项目代码评审与 Stardew 迁移建议

日期：2026-07-16

## 评审范围

本次将以下项目浅克隆到 `references/minecraft/` 并检查当前源码，而不只依据 README：

| 项目 | 本地目录 | 当前浅克隆提交 | 许可证 | 主要定位 |
| --- | --- | --- | --- | --- |
| SecondBrain | `SecondBrain/` | `1b0d932` (2026-05-28) | LGPL-3.0 | Fabric 内的 LLM 假玩家伙伴 |
| AI-Player | `AI-Player/` | `6631ebb` (2026-06-08) | MIT | Fabric/Carpet 假玩家、RAG、规划和自治实验 |
| Mindcraft / MineCollab | `Mindcraft/` | `5f3acc8` (2026-06-08) | MIT | Mineflayer Agent 平台及多智能体评估任务 |
| Voyager | `Voyager/` | `55e45a8` (2023-07-27) | MIT | 自动课程、迭代代码生成、critic 和技能库研究基线 |

还参考了 Microsoft Research 的 Minecraft 协作任务论文。其工程源码未公开在论文页面，因此只分析论文描述，不把推测当作代码事实。

## 先给结论

这些项目最值得迁移到 Stardew 的不是 Minecraft 动作代码，而是下面这条控制链：

```text
Perception Snapshot
  -> Goal / Dialogue Interpretation
  -> Typed Action Proposal
  -> Deterministic Policy Gate
  -> Game-thread Executor
  -> State-based Verifier
  -> Result / Failure Feedback
  -> Trace
  -> Memory or Skill Consolidation (only when eligible)
```

你当前的 Stardew 项目已经有这条链的前半段：NPC 有界感知、对话、RAG、玩家记忆、关系提案、关系 authority 和 trace。下一步不应先加入“每天自由规划一切”，而应补齐通用的 action envelope、policy、executor、verifier 和任务评估工具。

Minecraft 项目也印证了一个关键设计：LLM 负责理解和提出意图，确定性游戏代码负责权限、执行和验收。模型不能直接写好感、扣钱、移动物品，也不能凭一句“完成了”宣告动作成功。

## 1. SecondBrain

### 实际结构

核心对象 `NPC` 聚合：

- Fabric `ServerPlayerEntity`
- `LLMClient`
- `ConversationHistory`
- `NPCEventHandler`
- AltoClef/Automatone controller
- `ContextProvider`
- NPC config

`ContextProvider` 每次请求采集 NPC 自身位置、生命、饥饿、生物群系、装备/背包/快捷栏、附近方块和附近实体。`PromptFormatter` 再将结构压成自然语言，限制附近实体 10 个、方块 15 个。

`NPCEventHandler` 是主闭环：

```text
event/chat
  -> rebuild context
  -> append history
  -> LLM returns { command, message }
  -> parse JSON
  -> AltoClef executes one command
  -> command error is sent back as a new event
```

事件使用单线程、容量 10 的队列；溢出时直接丢弃。这个选择简单地避免同一 NPC 并发执行多个 LLM 回合。

### 记忆和 RAG

它没有 lore RAG 或向量数据库。记忆主要是：

- 最多 30 条的对话上下文。
- 达到上限后，摘要较旧的三分之一并放回上下文。
- NPC 移除/关闭时把对话按 NPC UUID 写入 SQLite。
- 下次加载最近 100 条记录。

这更接近“滚动对话摘要”，不是可审计的事实记忆系统。摘要没有证据、类型、冲突处理、重要度或遗忘策略。

### 值得借鉴

- 用 NPC 视角采集附近环境，不把整个世界状态给角色。
- 单 NPC 串行队列，避免异步 API 回包造成状态竞争。
- 每次动作前重建快照，错误回灌给模型。
- 对话和动作共用一个严格、很小的输出 envelope。
- OpenAI/Ollama/Player2 通过 `LLMClient` 抽象切换。

### 不建议照搬

- `command` 是自由文本命令，虽然 prompt 列出 allowlist，但缺少独立 policy authority。
- 命令完成后的自动续规划目前被注释，闭环主要覆盖报错，不覆盖可靠的成功验证。
- 对话历史里存入的是包含环境快照的格式化 prompt，长期会重复大量易过期状态。
- SQLite 只存整段对话，不能区分 lore、玩家事实、关系事件与技能。
- 当前源码的系统 prompt 模板选择/参数顺序看起来存在可疑错配；这也是不要直接复制 prompt 组装代码的理由。
- LGPL 代码若直接合并，需要认真履行许可证义务。架构思想可以借，代码不应复制进当前项目。

## 2. AI-Player

### 实际结构

AI-Player 的功能面最宽：

- `FunctionCallerV2`：LLM pipeline、工具调用、shared state、占位符解析、重规划。
- `ToolRegistry`：11 个显式注册工具。
- `ToolVerifiers`：8 个状态验证器。
- `GameAI/autonomous`：优先级目标队列、世界事件注入、空闲重规划。
- `GameAI/planner`：goal mapping、hybrid planner、Markov/RL 相关结构和 action log。
- `GameAI/proximity` 及 detector：附近对象、威胁、生命、饥饿等感知。
- `RAG2` + `SQLiteDB`：对话/事件向量记忆与 Web 搜索。
- `persona` + `mood`：身份、persona 和实时 mood 三层 prompt。

自治循环把目标分为 LLM 计划、玩家指令、世界事件，并放进最大深度 10 的优先级队列。玩家直接控制时自治循环让步。这种“控制权仲裁”很适合未来 Stardew 的玩家命令与 NPC 日程并存场景。

### RAG 和记忆

`SQLiteDB` 建立 `memories(type, prompt, response, embedding)`，`RAG2` 默认 top-k 5：

- 普通对话从 `conversation` 类型向量检索。
- 世界事件从 `event` 类型检索。
- 信息查询可调用 Web 搜索，并可能将结果回写成本地 conversation memory。
- 每次最终回复又被写回 conversation memory。

它确实有一个向量 RAG 数据库，但主要检索“过去的问答/事件”，不是精心维护的世界 lore 数据库。没有看到成熟的 memory candidate 审批、证据约束、冲突合并或生命周期管理。

### 值得借鉴

- 目标来源与优先级显式化：`PLAYER > WORLD_EVENT > IDLE_PLAN` 一类仲裁应保留。
- 工具 schema、执行器、shared state 和 verifier 分开。
- 后续步骤用 typed shared-state key 引用前一步结果，而非让模型复述坐标。
- verifier 读取游戏真实状态，例如到达距离、方块是否真的放下。
- persona 是静态身份，mood 是动态 overlay，二者不混进长期记忆。
- 玩家控制时暂停自治，是混合主动式 agent 必需的边界。

### 不建议照搬

- `FunctionCallerV2.java` 超过两千行，含多套执行/规划路径和重复重载，维护成本很高。
- persona、personality、mood 等目录存在重叠演进痕迹；需求面可以参考，模块布局不适合复制。
- 只有 11 个注册工具却只有 8 个 verifier；源码中还有“假定成功”和待验证 TODO。
- 一些 verifier 只验证值非负，证明“读到了状态”，并不证明任务完成。
- 将 Web 搜索答案自动写进 conversation memory 会污染角色记忆，也可能把不可信信息长期化。
- 让 LLM 一次生成 4-6 个自由文本目标，再用规则映射到已知 goal，容易丢弃无法映射的目标。
- 代码包含大量静态共享状态，未来多 NPC/多存档隔离风险较高。

AI-Player 更适合作为“功能需求目录”和失败案例来源，而不是当前 Stardew 工程的代码骨架。

## 3. Mindcraft / MineCollab

### 实际结构

Mindcraft 的边界最清楚：Mineflayer 是游戏 adapter，Agent 负责对话循环，command registry 负责工具，`ActionManager` 负责执行生命周期。

当前源码中可数到 41 个 action、8 个 query，共 49 个命令。命令解析器会：

- 只识别一个 `!command(args)`。
- 校验参数数量和 int/float/bool/item/block 类型。
- 校验数字 domain 和 Minecraft item/block 名称。
- 忽略命令后的额外内容。
- 按任务配置 blacklist 命令，但 `stop/stats/inventory/goal` 不允许屏蔽。

`ActionManager` 提供：

- 动作互斥和中断。
- 分钟级 timeout。
- resume action。
- 快速循环和无限循环检测。
- 输出截断。
- `{success, message, interrupted, timedout}` 结果对象。
- 异常、行为日志和执行结果回灌 history。

这部分是四个项目里最适合直接转译成 Stardew C# 接口设计的结构。

### 感知

`getFullState` 输出结构化状态：位置、维度、模式、生命、饥饿、生物群系、天气、时间、脚下/身体/头部方块、装备、背包、附近人类/机器人/实体和当前 action activity。

对话 prompt 不一定每回合塞入全量 `getFullState`，而是使用 query commands、行为日志与 profile prompt 组合。这提醒我们应区分：

- 调试/评估用 full snapshot。
- NPC 对话用 bounded perception。
- action verifier 用按需精确状态。

你当前项目已经有 `PerceptionSnapshot` 与 `NpcPerceptionSnapshot` 两层，方向是对的。

### 记忆

Mindcraft 的通用记忆其实很轻：

- `History.turns` 保存当前窗口。
- 超过 `max_messages` 后每次取 5 条生成不超过 500 字符的自然语言摘要。
- 被移出的完整消息写到按时间命名的 history JSON。
- `memory.json` 持久化摘要、当前 turns、self-prompt 状态、task start 和 last sender。
- `MemoryBank` 仅保存命名地点到坐标。

它没有成熟的人物事实图或 lore RAG。这里最值得借的是“当前工作记忆、压缩摘要、完整审计历史分离”，不是它的具体 memory schema。

### MineCollab 评估

任务 JSON 显式定义 goal、初始背包、agent 数量、target、timeout、blocked actions 和地图/蓝图。评估采用：

- crafting/cooking：确定性检查目标物品和数量，0/1 成功率。
- construction：与目标蓝图比较，使用编辑距离类分数。
- 每次实验保存结果和 agent 结束时的 memory。
- 支持多个世界并行跑相同任务。

这是对当前 Stardew 项目价值最高的部分之一。星露谷可以用固定存档 fixture 和确定性 postcondition 做同类评估。

### 值得借鉴

- 命令注册表同时生成 prompt 文档和运行时校验，不维护两套工具说明。
- action 与 query 分开；query 默认无副作用。
- 每项任务可以禁用动作，policy 不只依赖全局 prompt。
- action manager 统一处理中断、超时、循环和结果。
- full state、对话上下文、validator 使用不同信息粒度。
- 任务成功由环境 validator 判定。
- 保存完整 history 作审计，同时只把压缩 memory 放进模型上下文。

### 不建议照搬

- `!command(...)` 的正则协议适合聊天式 bot，但 Stardew 后端已有 JSON，继续使用 typed JSON 更稳。
- 49 个工具对 MVP 太宽。星露谷第一批只应有 3-5 个低风险工具。
- 大量通用 Mineflayer 动作会扩大 prompt、选择错误率和安全面。
- 自生成 JavaScript/code execution 不适合嵌入 SMAPI；Stardew 只执行预注册 C# executor。
- 当前源码仍有实验性代码和明显 TODO，不能把“研究平台可运行”理解为生产级可靠。

## 4. Voyager

### 实际结构

Voyager 把长期自治拆成四个 agent：

- Curriculum Agent：根据进度和观察提出下一个任务。
- Action Agent：生成可执行 Mineflayer JavaScript。
- Critic Agent：根据执行后状态判断成功并给 critique。
- Skill Manager：成功后保存代码技能，并向量检索相关技能供后续复用。

每个任务最多重试若干次。每轮将旧代码、执行错误、聊天日志、环境状态、任务、context 和 critique 重新交给 Action Agent。只有 critic 判定成功时，代码才进入技能库。

### 技能库不是人物记忆

Skill Manager 为成功代码生成自然语言描述，保存：

- `skills.json` 元数据。
- `skill/code/*.js`。
- `skill/description/*.txt`。
- Chroma embedding index。

新任务按 context 与执行反馈检索 top-k 技能。这个库回答的是“我会怎么做”，不是“玩家喜欢什么”或“Abigail 经历过什么”。

Stardew 应明确保留四种不同持久化：

1. Lore：作者定义的世界与角色事实。
2. Player/NPC memory：玩家偏好、承诺、共同经历。
3. Procedural capability：已注册工具和可靠工作流。
4. Trace/eval：发生了什么、为什么、是否成功。

### Critic 和 trace

`EventRecorder` 保存每轮原始事件，并统计新物品、时间、迭代和位置。Critic 根据最终生命、饥饿、位置、背包、附近方块和箱子判断任务成功。

Critic 的问题是它本身仍由 LLM 驱动。简单任务可用背包规则判断，却仍让模型输出 success。这个结构适合产生自然语言 critique，但不宜作为权限或经济状态的最终 authority。

### 值得借鉴

- 只有成功且可复用的动作流程才沉淀为技能。
- 失败反馈与上轮代码一起进入下一次尝试。
- curriculum、action、critic、skill retrieval 使用不同 prompt 和模型配置。
- trace 保存原始环境事件，而不是只保存模型摘要。
- 技能检索与人物记忆检索完全分离。

### 不建议照搬

- 运行模型生成的任意 JavaScript 风险过大，也不适合 Stardew 的稳定游戏状态。
- 让 LLM critic 做最终成功判定不够可靠。
- 自动 curriculum 的目标是开放世界探索，不符合有日程、关系和原版剧情约束的 NPC。
- 这是 2023 研究基线，依赖和 API 较旧，应该借概念而非代码。

## 5. Microsoft 协作 NPC 与 MineCollab 的额外提醒

Microsoft 的 28 人用户研究使用两个 GPT-4 NPC 与玩家共同完成 Minecraft quest。论文强调 persona/backstory、子目标引导、函数调用结果回灌，也报告了仅语言模型缺少丰富游戏状态/视觉理解的限制。

MineCollab 的研究结论进一步指出，多 agent 详细沟通会拖累任务表现，复杂协作中会出现无效对话和互相干扰。这对 Stardew 的含义是：

- “Abigail 帮我浇附近 6 格作物”应是明确委托，不应触发一段自由协商。
- 一次只允许一个 active goal，或者明确优先级和取消语义。
- 对其他 NPC 的行为不能默认拥有控制权。
- 协作提示应简短展示目标、范围、完成/失败，而不是暴露内部思维链。
- NPC persona 可以影响措辞和是否愿意接受任务，但不能改变 executor 的事实判断。

## 五模块对照

| 模块 | SecondBrain | AI-Player | Mindcraft | Voyager | 对当前 Stardew 的建议 |
| --- | --- | --- | --- | --- | --- |
| Lore RAG | 无 | 对话/事件向量检索，非正式 lore | profile/examples，非 lore DB | 技能向量库，不是 lore | 保留独立 JSONL lore；不要混入聊天记录或技能 |
| Player Memory | 对话摘要 + SQLite | conversation/event embeddings | 工作窗口 + 摘要 + 完整历史 | 主要记录任务进度 | 继续 evidence-gated memory，新增摘要/冲突/生命周期层 |
| Dialogue Policy | prompt + 单命令 JSON | prompt、goal mapping、部分 verifier | allow/blacklist + typed params | prompt + critic | policy 必须是后端/SMAPI 确定性 authority |
| Evaluation & Trace | 日志和聊天持久化 | action log/部分 verifier | 任务 fixture + 确定性评分 | event recorder + LLM critic | 建立固定存档场景、postcondition、回归数据集 |
| Perception Builder | NPC 本地环境 | 多 detector + compact state | full state + queries | task-specific observations | 保留 full/NPC/action 三种视图，不给 NPC 全知状态 |

## 对现有 Stardew 项目的具体映射

当前已有：

- `PerceptionSnapshot.cs`：全量调试/未来 farmhand 感知。
- `NpcPerceptionSnapshot.cs`：普通 NPC 有界感知。
- `backend/stardew_backend/retrieval.py`：JSONL lore 检索。
- `backend/stardew_backend/memory.py`：按存档和 NPC 隔离的事实记忆。
- `RelationshipManager.cs`：LLM 关系提案后的确定性 authority。
- `backend/stardew_backend/trace.py`：逐回合 JSONL trace。
- `action_proposal`：协议占位，但尚不执行。

与 Minecraft 项目相比，主要缺口不是“再加一个 planner”，而是：

1. Action contract：统一 action id、typed args、proposal id、actor、target、范围和风险级别。
2. Policy decision：返回 allow/deny/confirm，附 rule id，而非布尔值。
3. Action manager：单 NPC 互斥、取消、超时、每日/每次预算、主线程队列。
4. Executor registry：每个 action 对应固定 C# executor，不接受模型代码。
5. State verifier：执行后从游戏重新读取状态，输出可审计 postcondition。
6. Goal state：pending/accepted/running/succeeded/failed/cancelled。
7. Eval harness：固定初始状态、动作请求、预期 policy 和最终状态。

## 推荐目标结构

```text
SMAPI Game Adapter
  Perception/
    DiagnosticPerceptionBuilder
    NpcPerceptionBuilder
    ActionPerceptionBuilder
  Actions/
    ActionRegistry
    ActionManager
    Executors/
    Verifiers/
  Policy/
    ActionPolicy
    RelationshipPolicy
  Runtime/
    GoalState
    AgentTurnCoordinator

FastAPI Backend
  dialogue/
  memory/
  lore/
  planning/
    action_schema.py
    proposal_parser.py
  trace/
  eval/
```

建议 action response：

```json
{
  "proposal_id": "proposal_...",
  "actor": "Abigail",
  "action": "follow_player",
  "args": {"max_tiles": 12},
  "reason": "The player explicitly asked Abigail to follow.",
  "evidence": "跟着我",
  "confidence": 0.91,
  "risk": "low"
}
```

SMAPI policy decision：

```json
{
  "decision": "confirm",
  "rule_id": "follow.requires_player_confirmation",
  "normalized_args": {"max_tiles": 12},
  "limits": {"timeout_seconds": 30}
}
```

执行结果：

```json
{
  "status": "succeeded",
  "action": "follow_player",
  "started_at": "...",
  "finished_at": "...",
  "postconditions": [
    {"name": "distance_to_player", "expected_max": 2, "actual": 1, "passed": true}
  ],
  "side_effects": [],
  "failure_code": null
}
```

## 分阶段实施建议

### A. 先做 action 基础设施，不执行有副作用动作

- 新建 action schema、registry、policy result 和 trace 字段。
- 实现 `inspect_nearby_crops` 这类 query，只读取状态。
- 实现 action timeout/cancel/one-active-goal。
- 为 malformed proposal、未注册 action、越界参数写测试。

### B. 第一个动作做 `follow_player`

- 它不改经济、物品、作物或关系。
- 要求玩家明确请求和确认。
- 限制同地点、最大距离、最长时间。
- 切图、事件开始、对话开始、玩家取消时停止。
- verifier 检查最终距离和中断原因。

### C. 再做 `water_nearby_crops`

- 固定半径和最大格数。
- 只允许可浇、未浇、可达的农田。
- 不消耗玩家背包物品，不改变种子或作物类型。
- 逐格记录 before/after，部分成功也要显式报告。
- 原版一天只能推进一次状态，重复请求应幂等。

### D. 最后做收获和购买

- 收获涉及物品归属、背包满、品质与多人模式，风险高于浇水。
- 购买涉及金钱 authority，必须显示物品、数量、总价并二次确认。
- 任何价格/库存变化都要在执行前重新验证，不能使用 LLM 请求时的旧快照。

### E. 建立 StardewAgentEval

最小评估集合：

- 10 条 dialogue/persona 测试。
- 10 条 memory write/retrieval/contradiction 测试。
- 10 条 relationship evidence/daily cap 测试。
- 15 条 action policy allow/deny/confirm 测试。
- 5 个固定存档动作场景，每个重复运行 3 次。

动作指标：proposal parse rate、policy precision、execution success、partial success、timeout、unintended side effects、平均 API 延迟和每次成功成本。对话指标与动作指标分开，不合成一个含糊总分。

## 借鉴边界与许可证

- 可以借鉴公开论文中的架构思想、协议形状和测试方法。
- MIT 项目允许复用代码，但若实际复制，应保留许可证和版权声明，并在项目中记录来源。
- SecondBrain 是 LGPL-3.0，直接复制/链接其实现会带来额外合规要求；当前项目应只借鉴设计思路。
- Mineflayer/AltoClef 的 Minecraft 执行代码对 SMAPI 不可复用，没有复制价值。
- 最稳妥的做法仍是“读实现、写自己的接口和测试”，并在文档中记录参考来源与 commit。

## 最终取舍

优先借：

- Mindcraft 的 command registry、ActionManager、blocked actions 和确定性 benchmark。
- Voyager 的成功后技能沉淀、critic feedback 与原始事件记录。
- SecondBrain 的 NPC 局部感知和单 NPC 串行事件队列。
- AI-Player 的控制权优先级、typed shared state 和 state verifier 思路。

暂不借：

- 任意代码生成与执行。
- 大而全的工具集。
- LLM 自己判定动作成功。
- 把所有聊天自动写入向量记忆。
- 开放式每日自主目标生成。
- 仅靠 prompt 做安全和权限控制。

这个取舍能让项目保持“角色首先是 Abigail，而不是全知游戏助手”，同时为后续真正能跟随、浇水、收获和购买的 agent 留出完整且可评估的执行路径。

## 外部资料

- SecondBrain: https://github.com/sailex428/SecondBrain
- AI-Player: https://github.com/shasankp000/AI-Player
- Mindcraft: https://github.com/mindcraft-bots/mindcraft
- MineCollab project: https://mindcraft-minecollab.github.io/
- MineCollab paper: https://arxiv.org/abs/2504.17950
- Voyager: https://github.com/MineDojo/Voyager
- Voyager project: https://voyager.minedojo.org/
- Microsoft collaborative quest paper: https://arxiv.org/abs/2407.03460
- Grounded Conversational Characters: https://www.microsoft.com/en-us/research/project/grounded-conversational-characters/in-depth/
