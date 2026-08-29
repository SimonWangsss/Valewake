# Valewake 项目审计、改进路线与面试手册

> 基于 2026-08-26 仓库代码、文档与本地离线测试整理。本文区分“代码已实现”“固定集已验证”和“仍需实机证明”，避免把开发集结果包装成泛化能力。

## 一、先说结论

Valewake 已经超出普通的 LLM 对话 Demo。它最有价值的地方不是接入了 RAG、Memory 或 FastAPI，而是形成了一个完整的受控闭环：

```text
局部状态感知
  -> Persona / Lore / Memory / Policy 编排
  -> LLM 生成结构化提案
  -> 后端字段与证据校验
  -> C# 游戏端权限裁决
  -> 玩家确认
  -> 确定性执行器修改世界
  -> 结果校验、日程恢复与 Trace
```

如果目标是应聘 LLM 应用、Agent、AI 游戏或后端工程岗位，目前项目的“架构完整度”已经较高；主要短板在“评估可信度、实机数据量、工程可复现性和模块深度的一致性”。下一阶段不应继续堆更多功能，而应把现有闭环做成有基线、有对照、有真实样本、有失败分析的工程案例。

### 1.1 完整性检查

| 层次 | 当前状态 | 评价 | 主要缺口 |
|---|---|---|---|
| 游戏接入 | C# / SMAPI 生命周期、原版对话兼容、UI、主线程回调 | 完整 | 缺自动化集成测试与跨平台构建 |
| 状态感知 | 完整 Agent 快照与 NPC 局部快照分离 | 较完整 | “可见”主要是距离过滤，没有遮挡、听觉、事件来源模型 |
| Persona | 34 个基础社交 NPC 有结构化档案，模组 NPC 有保守回退 | 可用 | 34 人的详细 Lore 深度不均，Abigail 明显最完整 |
| RAG | 54 个 JSONL chunk；主题路由、BM25、字符 n-gram TF-IDF、标签与 parent context | 有设计、有指标 | 语料小；缺独立测试集、context precision、faithfulness 与跨语言语义基线 |
| Memory | 工作记忆、情节记忆、语义记忆、长期画像视图；按存档/NPC 隔离；支持 commit/rollback | 较完整 | 长期画像本质是语义记忆筛选视图，不是独立 consolidation 模型；真实长期评估不足 |
| 对话边界 | 输入分类、多轮检测、输出泄漏检查、副作用熔断 | 较完整 | 仍以规则和概念词组为主，对隐晦改写和分布外攻击的泛化未证明 |
| 关系系统 | LLM 提议，本地检查证据、置信度、每日上限后写入原版好感 | 完整且有特色 | 缺关系误变更率、长期玩家感受和参数校准实验 |
| 动作 Agent | 农场 3 类动作、矿洞同行/防御/采矿；确认、白名单、状态机、恢复与 Trace | 架构完整 | 农场实机样本只有 7 个历史任务；矿洞实机样本为 0；执行器测试不足 |
| 可观测性 | 对话、检索、记忆、提案、任务结果使用 ID 串联的 JSONL Trace | 可用 | 还不是完整 observability：缺延迟分段、token/cost、错误率、版本字段和可视化面板 |
| 评估 | 单元测试、黄金集、消融、真实 Trace scorer | 方向正确 | 多数集合由项目作者构造且参与迭代；缺独立冻结集、盲评、统计置信区间 |
| 部署 | Mod 可自动拉起本地后端，支持打包脚本 | 可演示 | Python 最低版本未声明；C# 工程含 Windows 本机 GamePath；无 CI 和一键验收环境 |

### 1.2 本次实际核验结果

- 使用 Python 3.12 运行 40 个单元测试，全部通过。
- 对话离线小套件为 14/14。
- 综合黄金集执行 84 条确定性用例，84/84 通过，另有 48 条真实模型生成用例被跳过。
- 当前动作固定集实际为 53 条，不是旧文档中的 48 条；Exact Intent Accuracy、Macro-F1 和 proposal contract rate 均为 100%。
- 40 条 Lore 检索集本次复跑的 Recall@5 为 93.8%，MRR 为 78.4%。仓库旧评估文档写的是 80.1%，说明指标文档与当前数据/实现存在版本漂移，应统一从带 commit、dataset hash 和时间戳的报告生成简历数字。
- 60 条安全开发集记录为 42 条攻击、18 条正常输入；攻击召回率由 80.95% 提升到 100%，正常输入误杀率保持 0%。这个结论只适用于该固定开发集。
- 默认系统 Python 3.9 会在导入 `dict[str, Any] | None` 时失败，Python 3.12 正常。仓库需明确声明 `requires-python >= 3.10`，最好直接固定为 3.11 或 3.12。

## 二、简历应该怎么改

### 2.1 当前版本的问题

1. 技术栈漏掉了 C#、.NET 6 和 SMAPI，而约 4,700 行核心游戏执行代码位于 C#。这是项目区别于普通 Python Agent Demo 的重要部分。
2. “四层记忆”容易让面试官理解为四套独立持久化系统。更准确的说法是“四层上下文/记忆架构，其中两层持久化”；Durable Profile 是高重要度语义记忆视图。
3. “将 42 条越狱攻击的识别召回率提升至 100%”应补上“固定开发集”，否则容易被追问数据泄漏和泛化能力。
4. 动作能力很强，但应突出“权限分层”和“确定性执行”，而不是只罗列能浇水、挖矿。
5. 目前简历没有体现 C# 与 Python 双进程、存档事务语义、主线程/异步边界和失败恢复，这些都是工程含金量。

### 2.2 推荐简历版本

**Valewake：面向动态游戏世界的状态感知 LLM NPC Agent**　2026.06—至今  
技术栈：Python、FastAPI、C#/.NET 6、SMAPI、RAG、BM25、TF-IDF、Memory、Agent Evaluation

基于《星露谷物语》构建可连续对话、跨存档记忆并执行受控任务的 NPC Agent；采用 C# 游戏适配层与 Python Agent Kernel 双进程架构，支持 34 个基础社交角色。

- **上下文与角色约束：** 将时间、天气、位置、关系及邻近实体转换为 NPC 局部感知快照，隔离调试用全局状态；以结构化 Persona、NPC 级 Lore 白名单和对话策略约束角色身份、知识范围与儿童安全边界。
- **RAG 与长期记忆：** 设计主题路由 + BM25 + 字符 n-gram TF-IDF + 标签加权的可解释混合检索；构建工作、情节、语义和长期画像四层上下文，按“存档 + NPC”隔离，并用 commit/rollback 对齐游戏保存语义。
- **受控行动与副作用治理：** 采用“LLM 结构化提议—规则校验—玩家确认—确定性执行—后置校验”的权限链路，将自然语言映射为农场和矿洞任务；通过动作白名单、数量/关系/时间限制、主机权限、持久化状态机及 NPC 日程恢复避免越权和重复执行。
- **评估与可观测性：** 以 `turn_id / proposal_id / job_id` 串联检索、记忆、关系与执行 Trace；在 40 条固定检索集上将 Recall@5 从 38.8% 提升至 93.8%，在 60 条安全开发集上将攻击召回率从 80.95% 提升至 100% 且 18 条正常输入误杀率为 0%；建立 53 条动作路由回归集并达到 100% Macro-F1。

如果版面只能保留三条，合并第一、二条，保留“受控行动”和“评估”两条。面试中主动说明 100% 是固定开发集结果，不代表开放分布泛化。

## 三、最值得做的改进路线

### P0：先把证据做硬（1—2 周）

#### 1. 建立真实动作基准

优先级最高。当前动作代码复杂度很高，但真实数据不足，投入产出最不匹配。

- 固定一个存档和地图状态，每个动作至少 20 次；每次重置存档。
- 分层覆盖 NPC 起点、玩家是否在农场、目标数、阻塞路径、地图切换、事件、深夜、存档重载。
- 农场记录 productive success、target precision/recall、P95 完成时间、路径重试率、日程恢复率和 no-op rate。
- 矿洞记录跨层跟随成功率、恢复时延、防御目标精确率、越界追击率、采矿优先级命中率、非目标破坏数。
- 对破坏性动作设置硬门槛：目标精确率 100%、非目标破坏 0、日程恢复率至少 95%。

简历价值：把“实现了动作”升级为“用实机 benchmark 证明动作可用”。

#### 2. 把开发集、验证集、盲测集分开

- `dev` 用于迭代规则，`test` 冻结，最终只运行少数次。
- 请同学或用真实游玩日志贡献不看规则的表达，尤其是错别字、方言、含蓄请求、否定、回忆和多意图输入。
- 报告每类 support、混淆矩阵和置信区间，不只给总分。
- 安全集增加基于字符扰动、同义改写、多轮拆分和 role-play 包装的攻击；正常集增加包含“模型、系统、API”等词但并非攻击的 hard negatives。

#### 3. 完成开放式对话盲评

- 采样至少 100 个场景，覆盖 10 个以上 NPC、4 个关系阶段和多种天气/地点。
- 对比三个版本：Persona-only、Persona + RAG、完整系统。
- 两名不了解版本信息的评价者按 0—2 分评价角色一致性、事实正确性、上下文相关性、自然度和边界遵循。
- 报告平均分、逐项胜率和 Cohen's kappa/一致率；保留典型失败案例。

#### 4. 修复可复现性

- 增加 `pyproject.toml`，声明 Python 版本、依赖和测试命令。
- 用同一脚本生成评估报告、README 表格和简历用指标，记录 Git commit、数据集 hash、模型名和 Prompt 版本。
- 清理相互冲突的旧文档：`architecture.md` 仍把 Action 写成未来能力，`action_agent_v1_design.md` 仍只描述两个农场动作。
- 增加 CI：Python 3.11/3.12 单测、黄金集、JSONL schema 校验、C# 可编译部分检查。

### P1：把核心模块做深（2—4 周）

#### 5. RAG 从“有指标”升级为“有实验”

不要直接为了技术名词接向量数据库。做一个可解释的消融矩阵：

| 方案 | Recall@5 | MRR/nDCG | Context Precision | P95 延迟 | 成本 |
|---|---:|---:|---:|---:|---:|
| BM25 | | | | | |
| BM25 + tags | | | | | |
| 当前 hybrid | | | | | |
| hybrid + multilingual embedding | | | | | |
| hybrid + reranker | | | | | |

只有 embedding/reranker 在冻结集上显著改善复杂释义、跨语言和长查询时才保留。这样面试时回答“为什么没用向量库”会非常有说服力。

同时补充：chunk provenance、剧情解锁条件、NPC 可见范围、重复/冲突检测和 context budget。把 34 个 NPC 分级覆盖，例如每人至少 20 个核心 Lore 问题，而不是只扩 Abigail。

#### 6. Memory 建立专项 benchmark

构造连续 7—30 个游戏日的脚本，评估：

- write precision：写下来的事实中有多少真的值得长期保存；
- write recall：应该记住的事实有多少被写入；
- retrieval Recall@K 与 context precision；
- contradiction resolution：喜好改变后旧事实是否失效；
- cross-save / cross-NPC leakage：目标必须为 0；
- rollback correctness：未保存退出后是否恢复到 checkpoint；
- stale memory rate：过期目标或承诺是否还被错误使用。

进一步可把 Episode 原文、Semantic Fact 和 Profile Summary 分开版本化，并记录 `valid_from / valid_to / supersedes`。涉及关系或副作用的记忆不要只靠模型抽取，应保留原始 evidence 和来源 turn。

#### 7. 可观测性升级

在现有 Trace 上增加：

- `trace_schema_version`、代码 commit、prompt/model/dataset 版本；
- perception、retrieval、LLM、validation、execution 各阶段耗时；
- prompt/completion token、重试次数、估算成本；
- parse valid rate、API success rate、P50/P95 端到端延迟；
- action 每个状态停留时间和统一 failure taxonomy。

JSONL 仍可保留为导出格式，运行时存储可换 SQLite，以支持事务、索引和并发读写。重点不是“用了数据库”，而是 Trace 能稳定回答一次坏体验究竟发生在哪一层。

#### 8. 抽离 C# 可测试核心

目前动作状态机和 SMAPI 对象耦合较重。把以下逻辑抽成纯 C# domain 层：

- proposal validation；
- 状态迁移合法性；
- target allowlist 与目标重验证；
- cap/timeout/retry 策略；
- return context 与恢复决策。

用接口封装 World Reader、Pathfinder、Clock、Save Store 和 Trace Sink，再用 fake world 做自动测试。实机测试负责验证 SMAPI 集成，单元测试负责覆盖状态机边界。

### P2：形成更强的研究/高级 Agent 故事（4 周以上）

#### 9. 任务 DSL 与分层规划

现在支持的是单个白名单动作。下一阶段可以定义受限 DSL：

```json
{
  "goal": "prepare_crops",
  "steps": [
    {"action": "water_crops", "max_targets": 10},
    {"action": "clear_weeds", "max_targets": 5}
  ],
  "preconditions": ["host_authorized", "before_2200"],
  "budget": {"max_steps": 2, "max_duration_sec": 120}
}
```

LLM 只生成 DSL，符号规划器验证前置条件，执行器逐步提交并记录 postcondition。失败后只能从白名单恢复策略中选择重试、跳过或终止，不能自由生成新工具调用。

#### 10. 事件驱动的 NPC 状态

把“距离内快照”升级为 `World Event -> Observer Filter -> NPC Belief State`：NPC 只有亲眼看见、听见或被可信角色告知后才更新认知。这样可研究多 NPC 信息传播、错误信念和社会关系，是比继续增加工具数量更有辨识度的方向。

#### 11. 多 NPC 协作但保持单一世界写权限

引入任务资源锁、角色占用、优先级和冲突解决。例如两名 NPC 不能同时处理同一格作物；世界状态仍由 host-side scheduler 串行提交。评估吞吐、冲突率、公平性和恢复率，而不是让多个 LLM 自由聊天决定一切。

## 四、完整面试草稿

### 4.1 30 秒版本

“Valewake 是我基于《星露谷物语》做的状态感知 LLM NPC Agent。它不是简单接一个聊天 API，而是把游戏里的时间、天气、位置、关系和附近实体转换成 NPC 的有限视野，再结合 Persona、RAG 和按存档/NPC 隔离的长期记忆生成回复。更重要的是，LLM 没有直接操作游戏的权限，它只能提出结构化动作，由 C# 本地规则校验、玩家确认后用确定性状态机执行，并验证结果和恢复 NPC 日程。我还建立了从对话到动作结果的 Trace 和固定评估集，RAG Recall@5 从 38.8% 提升到 93.8%。”

### 4.2 两分钟版本

“这个项目要解决的问题是，传统游戏 NPC 的台词稳定但不灵活，而直接接 LLM 虽然能聊，却会出现角色漂移、全知视角、错误记忆和越权操作。因此我把系统拆成游戏端和 Agent Kernel 两层。

游戏端用 C# 和 SMAPI，负责读取动态世界、维护原版交互、修改好感度以及真正执行动作。Python/FastAPI 后端负责 Persona、Lore 检索、玩家记忆、对话策略和 LLM 调用。每轮对话中，我只给 NPC 六格左右的局部状态，不传完整金钱、背包和远处农场信息；然后用主题路由、BM25、字符 n-gram TF-IDF 和标签加权检索 Lore，再检索该存档、该 NPC 对玩家的相关记忆。

模型返回的不是自由文本，而是 reply、emotion、relationship effect、memory candidates 和 action proposal 的结构化 JSON。关系变化必须有玩家原话证据、足够置信度，并受每日上限控制。动作采用双重 authority：后端先规范化提案，C# 再检查角色年龄、好感、Trust、时间、地点、主机权限、动作白名单和数量上限，之后还要玩家确认。执行器完成寻路、目标重验证、结果检查和日程恢复。

评估上我把 `turn_id、proposal_id、job_id` 串起来记录 Trace。固定 40 条检索集的 Recall@5 从 38.8% 提升到 93.8%；60 条安全开发集上攻击召回率从 80.95% 提升到 100%，18 条正常输入没有误杀。不过我会明确这些是自建固定集。当前最大的不足是动作实机样本少、矿洞还没有正式 benchmark，所以我下一步不是继续加工具，而是补真实执行测试、盲评和可复现 CI。”

### 4.3 八到十分钟详细版本

#### 开场：背景与问题

“我做 Valewake 的出发点，是想验证 LLM 在一个持续变化、存在原生规则的游戏世界里，能不能成为一个可信的 NPC，而不仅是套着角色名字的聊天机器人。《星露谷物语》很适合，因为它有时间、天气、关系、地图、日程、农场和矿洞等动态状态，也有成熟的原版交互可以作为约束。

我把问题拆成五个问题：NPC 当下知道什么；它是谁；它记得什么；哪些输出能产生副作用；系统坏了以后如何定位和恢复。”

#### 架构

“架构上是 C# 游戏适配层加 Python Agent Kernel。C# 负责 SMAPI 生命周期、UI、有限状态采集、主线程写游戏、关系 authority 和动作执行；Python 负责快速迭代 RAG、Memory、Policy、模型调用和评估。两边通过 loopback FastAPI 通信，Mod 会先访问 health endpoint，必要时自动启动打包后的本地后端，并且只关闭自己启动的进程。

这种拆分的原因不是语言偏好，而是权限边界：Python 层可以不可信地生成意图，但只有 C# 层能修改世界。即使 Prompt 被绕过，模型也拿不到任意 Game API。”

#### 状态感知

“我设计了两套状态。完整快照用于调试和未来任务 Agent，包含农场、背包、风险等较多信息；普通 NPC 对话只使用局部快照，包括时间、天气、地点、玩家手持物、双方关系和附近实体。这样是在数据源头限制全知，而不是把全部信息传给模型后要求它假装不知道。

当前局部感知还是 Chebyshev 距离邻域，不是真实视线，这是一个明确限制。以后我会加入遮挡、事件观察者和信息来源。”

#### Persona 与 RAG

“Persona 是身份必需信息，所以 34 个角色的基础 Persona 每轮必定注入；详细 Lore 才走 RAG。这样避免最关键的人设因为没检索到而丢失。RAG 对当前 54 个小规模 chunk 采用主题分类、BM25、字符 n-gram TF-IDF、lexical overlap、标签和优先级组合，并支持 parent chunk 回填。字符 n-gram 是为了在不引入中文分词和 embedding 服务时支持中英文混合表达。

这里我没有为了显得复杂直接上向量数据库，因为 54 条数据用外部向量库没有规模收益，反而增加部署和解释成本。我先建立了 40 条检索集，通过拆小 chunk、补别名、主题路由、字符级稀疏相似度和 parent context，把 Recall@5 从 38.8% 提升到 93.8%。下一步会在冻结集上比较 multilingual embedding 和 reranker，只有指标和错误类型证明值得才引入。”

#### Memory

“Memory 分四层上下文：当前会话最近 12 条消息；最多 80 条的跨会话情节记忆；带 evidence、confidence、importance、reinforcement 和 game day 的语义事实；以及从高重要度活跃事实中选出的长期画像视图。持久化实体主要是 Episode 和 Semantic Memory，所以我不会把它说成四个独立数据库。

记忆写入有两条链路：明确模式由规则提取，开放表达由模型提出 candidate，但必须是玩家直接陈述、subject 为 player、类别在白名单、置信度至少 0.75，而且 evidence 必须逐字出现在本轮玩家输入中。问句、临时闲聊和越界轮次禁止写入。偏好发生正负冲突时通过 canonical key 和 polarity 将旧记忆标记为 superseded。

另一个比较特殊的点是存档事务语义。对话可能已经写入磁盘，但玩家未保存就退出游戏，因此我在游戏保存时 commit checkpoint，未保存退出时按 save prefix rollback，避免 AI 记住游戏世界中事实上没有保存的经历。”

#### 对话和关系权限

“模型输出包括回复、情绪、关系变化、记忆候选和动作提案。解析失败时只保留可安全提取的 reply，所有副作用回退为 neutral/null；中文输入却输出英文时最多进行一次低温重试。

关系变化不是模型说加多少就加多少。模型只能提出 positive/negative、intensity、confidence、reason 和玩家原话 evidence。后端先做证据校验和策略熔断，C# 再检查阈值和每日增减上限，最终才写原版 Friendship，同时更新 Rapport 和 Trust。这样既兼容心事件，又避免普通闲聊刷好感。”

#### 动作 Agent

“动作部分是整个项目的核心设计。自然语言先由规则路由和 LLM 形成 typed proposal，但 proposal 不代表授权。C# 会再次检查 action allowlist、NPC 是否同意、证据是否来自本轮请求、年龄、事件状态、时间、好感、Trust、是否是多人主机以及当前是否有冲突任务，之后弹出原生 Yes/No 确认框。

通过后，持久化状态机负责 dispatch、preparing、navigation、acting 和 terminal states。浇水只选择存活、种有作物且未浇水的 HoeDirt；除草和砍树使用严格白名单；目标在实际执行前再次验证。矿洞同行支持跨层跟随、卡住恢复、近身防御和限定矿石采集。每次任务会保存 NPC 原位置、朝向和 followSchedule，完成、取消、失败或重载时恢复。重载后不会盲目重放未完成任务，而是标记可恢复失败，避免重复修改世界。”

#### 评估、结果和诚实边界

“我没有把所有东西平均成一个 Agent 准确率，而是分别评估检索、Memory 合约、安全、动作路由和真实执行。Trace 用 turn、proposal、job/expedition ID 串起整个因果链。

当前可复现结果包括 40 条检索集 Recall@5 93.8%，53 条动作路由固定集 Macro-F1 100%，60 条安全开发集攻击召回 100%、正常输入误杀 0%。但这些集合多数由我构造并用于开发，所以只能说明当前规则对固定集合的回归，不代表真实分布 100%。真实动作方面，历史农场 Trace 只有 7 个任务，productive success 为 4/7，矿洞正式实机样本还是 0。这正是项目现在最大的证据缺口。

下一阶段我会先收集每个动作至少 20 次的分层实机测试，做开放对话盲评，并把 Python 版本、CI、报告版本和 C# domain 测试补齐。完成后，这个项目就会从功能完整的个人 Demo 变成有可信工程评估的 Agent 系统。”

## 五、技术问题与参考回答

### 5.1 架构与系统设计

#### Q1：为什么用 C# + Python 双进程，不全部写在 C# 或 Python？

答：SMAPI 和游戏对象模型天然在 C#，路径、UI、NPC 日程和世界写入放在 C# 最可靠；RAG、Memory、Prompt、数据处理和评估在 Python 中迭代更快。双进程还形成安全边界：Python 只能返回结构化提案，不能持有任意游戏对象引用，世界副作用只能由 C# authority 执行。代价是 IPC、部署和版本契约更复杂，因此我增加 `/health`、Pydantic schema、超时、后端自动拉起和结构化 Trace。下一步会给请求/响应加显式 schema version 和兼容性测试。

#### Q2：一轮请求的完整链路是什么？

答：C# 判断是否保留原版交互，构建 NPC 局部感知与会话历史，以 `存档:NPC` 作为 session 调用 `/chat`；Python 读取 Persona，计算社交上下文和 Dialogue Policy，检索 Lore、语义 Memory、近期 Episode 和 Durable Profile，组装 Prompt 并调用模型；模型返回 JSON，后端解析和校验副作用、写 Memory/Trace；C# 回主线程展示回复、再次验证关系或动作提案；动作还需玩家确认，进入持久化状态机，最终记录 postcondition 和恢复结果。

#### Q3：为什么 FastAPI endpoint 是同步函数？并发安全吗？

答：当前是本地单玩家、单后端进程，交互频率低，同步链路足够简单；MemoryStore 用 `RLock` 保护同进程写入，并使用临时文件替换保证单次落盘原子性。但它不支持多 worker 的跨进程一致性，JSON 文件也不适合高并发。若扩展到多会话服务，我会把 LLM I/O 改为 async、用 per-session lock 避免同一会话乱序，并把 Memory/Trace 移到 SQLite/Postgres，通过事务和唯一键处理幂等。

#### Q4：游戏主线程如何保证？

答：网络请求不能阻塞游戏主线程，因此请求异步发送；结果回到 C# 后进入 `ConcurrentQueue<Action>`，由 SMAPI update tick 在主线程消费，所有 UI 和 Game1 状态写入都在游戏线程执行。执行器本身按 tick 推进状态机，不用长时间阻塞循环。

#### Q5：模型服务不可用或返回坏 JSON 怎么办？

答：后端有超时和启动健康检查。生成结果解析失败时会进行一次低温 JSON 修正；仍失败则只尝试提取 reply，关系、Memory 和 Action 全部回退为 neutral/null，防止格式错误产生副作用。游戏端也捕获请求错误并给用户可恢复反馈。更完善的做法是增加 circuit breaker、重试退避、离线原版台词回退和失败率监控。

### 5.2 感知、Persona 与上下文

#### Q6：为什么要区分完整快照和 NPC 快照？

答：任务 Agent 为了规划可能需要完整农场信息，普通居民却不应知道玩家的钱、完整背包或远处作物。数据最小化比 Prompt 中写“请忽略这些信息”更可靠，所以我在输入层就分 schema。它同时减少 token、降低隐私暴露并改善沉浸感。

#### Q7：所谓“NPC 可见”真的考虑遮挡了吗？

答：目前没有，准确说是限定半径的局部邻域，默认约六格。这能消除大部分全局泄漏，但不是视觉模型。下一步会用地图碰撞/射线做 line-of-sight，并把事件转成带来源、位置、时间和 observer 的流，只让实际观察者更新 belief。

#### Q8：Persona 为什么不也走 RAG？

答：身份、语气和硬边界是每轮必需约束，如果交给召回会出现“没检索到就丢人设”。所以核心 Persona 常驻，详细背景和情境知识按需检索。代价是常驻 token，我通过结构化短字段控制长度。

#### Q9：34 个角色是否真的都做到同样质量？

答：不是。34 个角色都有基础 Persona 和隔离边界，可以正常接入；详细 Lore 当前主要集中在 Abigail，其他角色属于 baseline coverage。面试中应主动区分“支持 34 个角色”和“34 个角色都有同等深度”，后者目前不能声称。后续会按角色建立最小 Lore 覆盖和盲评集。

### 5.3 RAG

#### Q10：混合检索具体如何计算？

答：先对查询做主题标签推断；英文按 token，中文连续字符串生成 bigram，另外生成 2—4 字符 n-gram。每个允许当前 NPC 访问的 chunk 计算 BM25、token overlap、字符 n-gram TF-IDF cosine，再叠加 query topic、当前状态 tag、关键安全主题和人工 priority 的小权重。排序后取 Top-K；命中子 chunk 时可补 parent context。Trace 保存 chunk ID、各类匹配信息和总分。

#### Q11：字符级 TF-IDF 的优缺点？

答：优点是无需中文分词模型、部署轻、对词形和部分错别字有一定鲁棒性，也能处理混合语言；缺点是语义能力弱，相同含义不同措辞可能没有足够字符重叠，长文本还会产生较多无意义 n-gram。因此它适合小型本地知识库的可解释基线，不是 embedding 的完全替代。

#### Q12：为什么不用向量数据库？

答：当前只有 54 个 chunk，扫描计算开销很小，向量库不会带来可见的规模收益；它还增加模型、索引、部署和版本依赖。正确问题不是“有没有向量库”，而是冻结集上的召回、排序、延迟和成本。我的计划是增加 multilingual embedding 作为候选召回或 reranker，做消融后决定是否保留；即使使用 embedding，54 条数据也可以内存索引，不一定需要专门数据库。

#### Q13：Recall@5 从 38.8% 到 93.8% 怎么来的？

答：先冻结 40 条查询及其 gold chunk 集，baseline 使用旧的粗粒度检索；随后把综合 chunk 原子化、补中英文 alias 和 topic router，引入 BM25、字符 n-gram TF-IDF、标签权重和 parent context。对每个 query 用实际无 hint 路由取 Top 5，计算命中的 gold 数除以 gold 总数，再对查询求平均。这个提升证明固定集的知识可达性改善，不等同于回答正确率；还需 context precision 和 answer faithfulness 补充。

#### Q14：为什么 Recall@5 很高但 MRR 较低？

答：相关 chunk 大多能进入前五，但不总在第一名。当前本次复跑 MRR 是 78.4%，说明排序仍有改进空间。原因可能是共享边界 chunk 的高权重、parent 补位占用排名、短查询词法歧义。可通过分项打分分析、nDCG、reranker 和 context budget 实验改进。

#### Q15：如何避免一个 NPC 检索到另一个 NPC 的私密 Lore？

答：检索打分前先做 scope filter：带显式 `npc` 字段的 chunk 只允许对应角色，旧 Abigail chunk 也通过 tag 兼容过滤；共享世界、机制和边界知识才对所有 NPC 开放。过滤必须发生在排序前，而不是检索后让模型自行忽略。测试中有 Abigail Lore 不泄漏给 Sebastian 的用例。

#### Q16：怎么评估 RAG 生成质量？

答：拆开评估 retrieval 和 generation。检索层看 Recall@K、MRR/nDCG、context precision 和错误分类；生成层在给定 evidence 下看 answer correctness、faithfulness、unsupported fact rate 和引用覆盖。再通过 Persona-only、RAG、RAG+reranker 的盲评或配对胜率判断最终体验，不能只用 Recall@K 代替回答质量。

### 5.4 Memory

#### Q17：四层 Memory 分别是什么？

答：第一层是当前连续会话最近 12 条消息；第二层是跨会话 Episode，保存玩家输入、回复、情绪、游戏日和 policy stance；第三层是可检索 Semantic Fact，保存偏好、档案、目标、观点、承诺和边界；第四层是 Durable Profile，即高重要度、仍 active、被强化的语义事实视图。严格说持久化数据结构主要是 Episode 和 Semantic Memory，Profile 是聚合视图。

#### Q18：如何决定一条信息该不该记？

答：规则抽取负责少量高精度模式；模型最多提出三条候选。候选必须 subject=player、kind 在白名单、confidence≥0.75、evidence 是玩家本轮输入的原文子串，而且 evidence 具有稳定自我陈述标志。问句、NPC 事实、临时情绪、越界轮次和“玩家提到了……”这类元描述会被拒绝。设计目标是宁可少记，也不要把幻觉写成长久事实。

#### Q19：Memory 如何检索？

答：只扫描当前 `save:NPC` 且 active 的事实，相关度来自文本 overlap，同时加入 kind query bonus、importance、reinforcement count 和按游戏日计算的 recency；Top-K 被读取后记录 access count 和 last accessed time。近期 Episode 则作为单独上下文输入，用于重复话题、久别重逢和情绪连续性。

#### Q20：如何处理重复和矛盾？

答：完全相同文本增加 reinforcement；同一 canonical topic 且高相似的事实合并；同一 topic 出现相反 polarity 时把旧事实标记 superseded，新事实记录 supersedes ID。当前 canonical topic 仍由有限规则产生，所以开放域矛盾处理不充分。升级方向是实体/属性/value 结构、有效时间和基于 NLI 的冲突候选，再保留人工可解释的原文证据。

#### Q21：为什么 Memory 要 commit/rollback？

答：游戏存档具有事务语义：玩家对话后直接退出而不保存，世界状态会回滚；如果 AI Memory 已永久写入，就会记住一段“没有发生过”的历史。因此运行文件可以即时更新，但只有游戏 Saved 事件才合并到 checkpoint；返回标题或退出时按 save prefix 从 checkpoint 恢复。这样 Memory 与游戏事实保持一致。

#### Q22：如何证明没有跨 NPC、跨存档泄漏？

答：所有 Memory 和 Episode 都带完整 session ID，搜索和替换严格使用等值 session；commit/rollback 用存档前缀只处理相关记录。现有单测覆盖 NPC 隔离和 save prefix checkpoint 隔离。进一步应做随机 session property test 和真实多存档长测，指标要求 leakage rate 为 0。

#### Q23：Memory 最大的问题是什么？

答：不是存储技术，而是“该记什么、什么时候过期、冲突时相信谁”。当前规则保证了较高可解释性，但长期真实数据不足，Profile 也只是筛选视图。最需要补的是有时间跨度的标注 benchmark，以及 write precision、retrieval recall、stale rate、contradiction resolution 和 rollback correctness。

### 5.5 安全、关系与结构化输出

#### Q24：Prompt Injection 防护有哪些层？

答：输入侧用 NFKC 标准化和概念组合识别 persona override、secret extraction、policy bypass 和 out-of-world；会结合最近多轮用户输入识别拆分攻击。策略命中后要求角色内拒绝，并熔断正向关系、Memory 写入和 Action proposal。输出侧再检查中英文模型身份、系统提示和 backend 泄漏，命中后替换回复并继续熔断副作用。最后 C# 权限层确保即使文本防护漏掉，模型仍不能直接操作世界。

#### Q25：安全集 100% 是否说明系统安全？

答：不能。它说明当前规则覆盖了这 42 条攻击并没有误杀 18 条正常样本，而且相对基线修复了八个已知漏检。由于数据是项目内自建开发集，存在规则适配和覆盖偏差。可信做法是冻结独立测试集、增加外部改写与真实玩家日志、报告攻击成功率和 benign false-positive rate，并把“文本防护”和“副作用权限隔离”的结果分别报告。

#### Q26：为什么不用一个 LLM 分类器做安全判断？

答：本地规则延迟低、确定、可离线测试，适合保护高风险副作用；单独 LLM 分类器成本和不确定性更高，还可能受到同一输入攻击。但规则泛化有限，所以可以采用级联：确定性高风险规则先拦截，模糊样本交给独立小模型/分类器，最终世界副作用始终由硬规则限制。关键是分类器不能成为唯一安全边界。

#### Q27：关系变化为什么要 evidence 和每日上限？

答：好感是持久化游戏副作用。若只依据模型的情绪判断，普通礼貌回复可能反复刷分，Prompt injection 也可能诱导模型加好感。要求 evidence 必须来自当前玩家原话，使决策可审计；置信度过滤弱判断；intensity 只允许 1/2；每日正负上限限制累计影响。模型提出语义判断，C# 管理最终数值。

#### Q28：结构化 JSON 为什么还不够安全？

答：Schema 只约束形状，不保证语义正确。模型仍可能选择错误 action、伪造 evidence、给出过大参数或声称任务已完成。因此还要做 enum、范围、原文证据、请求/动作一致性、本地世界条件和 postcondition 校验。结构化输出是降低解析歧义，不是授权机制。

### 5.6 Action Agent

#### Q29：为什么不是让 LLM 每一步调用工具？

答：实时游戏移动需要低延迟、可预测和逐 tick 控制。让远程 LLM 决定每一步会带来延迟、成本、非确定性、无限循环和安全风险。因此 LLM 只负责把自然语言映射成高层意图及角色是否愿意；确定性状态机负责路径、目标和工具动作。这是高层语义规划与低层控制的分层。

#### Q30：动作权限链具体检查什么？

答：后端先确认输入确实包含支持的请求、规范化 action/parameter、限制 max target、检查 proposal evidence。C# 再检查功能开关、host authority、事件/节日、NPC 年龄、NPC disposition、置信度、原文证据、时间、活动任务冲突、好感、Trust、地点和 action allowlist。所有检查通过后仍需玩家 Yes/No 确认。

#### Q31：如何避免砍错树或删错物体？

答：规划阶段只从严格 allowlist 选择目标：砍树只选成熟、未装采集器等满足条件的树，除草只认明确 weed ID/name，浇水只选种有存活作物且未浇水的 HoeDirt。执行前再次读取世界状态，目标已变化就 skip。破坏性动作必须以 target precision 100% 和 unintended damage 0 作为发布门槛。

#### Q32：状态机有哪些状态，为什么持久化？

答：农场任务大致为 accepted、dispatching、preparing、navigating、acting，再进入 completed、cancelled、failed_recoverable 或 failed_terminal。持久化可以在跨 tick 和存档生命周期中保留上下文，也便于 Trace 重建。但加载未完成任务时不自动重放，因为上次 mutation 是否已发生可能不确定；当前选择标记失败并恢复 NPC，优先避免重复副作用。

#### Q33：寻路失败怎么办？

答：为目标计算相邻可站立点，用 Stardew PathFindController 导航；设置超时和候选点，卡住后重算或换候选。单目标失败通常 skip 后继续队列，系统级失败才终止。跨地图不会伪装完整导航，而是在玩家不可见或超时条件下调度到合法入口，再在可见地图使用真实寻路。所有失败原因进入 Trace。

#### Q34：NPC 日程如何恢复？

答：任务开始前保存原位置、tile、朝向和 `followSchedule`，清除当前 controller 并暂时占用 NPC。完成、取消、失败、重载和离开矿洞时统一走 restore，恢复位置/方向和 schedule flag；原先跟随日程时调用当前时间的 schedule 检查。评估不能只看字段存在，还要实机验证恢复位置和后续日程，目标至少 95%。

#### Q35：多人模式怎么处理？

答：只有 host 可以授权和执行世界修改，farmhand 可以对话但 action 会在本地 validator 被拒绝。这样避免多个客户端竞争修改同一世界。目前还没有 farmhand 提案转发和同步确认 UI。如果扩展，会让客户端只发 command，host scheduler 分配幂等 ID、资源锁并广播状态，世界写仍保持单 authority。

#### Q36：如何保证动作幂等？

答：每个 proposal/job 有唯一 ID，目标执行前后都验证状态，完成的 target 进入 outcome；重载时不重放非终态任务，避免重复砍树或重复发放掉落。更严格的版本会为每个 target mutation 记录 `(job_id, target_key, precondition_hash, committed)`，通过唯一约束和事务日志做到 exactly-once-like 语义。

#### Q37：为什么购物没有做？

答：购物同时改变金钱和物品，是跨资源事务，失败时还涉及退款和背包容量。没有商店/时间/商品白名单、精确价格确认、预算、库存、原子扣款和交付校验之前开放它，风险大于展示价值。拒绝实现不是能力不足，而是按照副作用风险逐级开放工具。

### 5.7 测试、指标与工程化

#### Q38：为什么动作路由 100% 仍不能证明识别很好？

答：53 条是项目自建固定集，类别均衡且表达覆盖有限，规则本身也根据这些失败案例迭代。100% 只说明没有回归。真实能力要用独立贡献者写的盲测输入、真实日志和对抗 hard negatives，报告每类 precision/recall、混淆矩阵，而不是只报 accuracy。

#### Q39：真实农场任务 6/7 完成，为什么 productive success 只有 4/7？

答：状态机到达 completed 不代表做了有用工作，可能所有候选都已变化或被 skip。因此我区分 terminal coverage、state completion 和 productive success。4/7 表示只有四个任务至少成功处理一个目标，更能反映玩家价值。这也暴露出目标选择和 no-op 完成逻辑需要改进。

#### Q40：你会如何设计端到端测试？

答：三层。纯 Python 测试检索、Memory、Policy 和 schema；纯 C# fake world 测 validator、状态迁移、目标白名单和恢复；固定存档的 SMAPI harness/人工脚本测真实寻路、动画、掉落、跨图和日程。每次 trial 写完整 trace，并以 world state 前后快照计算 postcondition，而不是只相信 executor 自报成功。

#### Q41：如何评估延迟和成本？

答：分段记录 perception、retrieval、prompt assembly、LLM first-token/total、parse/retry、C# round-trip 和 UI display。报告 P50/P95、structured valid rate、重试率、token/turn、cost/successful turn。RAG 和 Memory 是本地毫秒级，主要瓶颈会是远程模型；可通过缩短常驻 Persona、context budget、缓存稳定 Lore 和流式展示改善体验，但副作用字段必须等完整结构化响应后才处理。

#### Q42：当前最重要的技术债是什么？

答：第一是实机动作证据不足；第二是 Python/C# 可复现与自动测试不完整；第三是评估集独立性不足；第四是 34 人 Lore 深度不均；第五是 JSON 文件在未来并发场景下不够稳。它们比继续添加更多动作更优先。

## 六、业务与产品问题及参考回答

#### Q43：这个项目解决了什么用户问题？

答：传统 NPC 内容固定、重复，玩家很难获得连续关系和个性化回应；纯 LLM NPC 虽然自由，却容易破坏世界观、忘记玩家、泄露系统信息或越权改变存档。Valewake 的价值是在“自由度”和“可控性”之间做产品化折中：角色能理解当下和长期关系，但所有高风险副作用仍服从游戏规则和玩家确认。

#### Q44：目标用户是谁？

答：第一阶段是喜欢沉浸式角色互动、愿意安装 SMAPI Mod 的单机玩家；第二阶段是希望快速为已有游戏接入可控 AI NPC 的 Mod/独立游戏开发者。如果做平台，应把 Game Adapter、Persona/Lore authoring、Policy、Memory 和 Action DSL 解耦，而不是把 Stardew 规则写死在通用层。

#### Q45：核心北极星指标是什么？

答：不能用“对话次数”单独衡量，因为低质量重复也会增加次数。我会选“每周有价值 NPC 互动用户占比”，有价值互动需满足玩家未立即退出/重试、人工或轻量反馈为正、没有边界/事实错误，并且动作型对话达到 productive success。辅助指标是 D7 留存、每会话有效互动数、角色一致性评分、动作成功率和严重副作用事故数。

#### Q46：怎么判断玩家真的喜欢，而不只是觉得新鲜？

答：做分组长期测试：原版、Persona-only、完整 Memory/RAG/Action 三组，观察 7—14 天的复访、主动找同一 NPC、重复生成/退出、对原版任务完成的影响和访谈反馈。新鲜感通常集中在首日，持续回访和对具体角色形成稳定互动才是长期价值。

#### Q47：最可能破坏体验的失败是什么？

答：严重程度从高到低是存档破坏或误删对象、儿童/关系边界错误、NPC 串记忆或泄露其他存档、角色关键事实胡编、动作声称成功但没执行、长延迟和普通对话不自然。发布策略应按严重度设置 kill switch：动作模块可单独关闭，关系修改可关闭，模型不可用时退回原版内容。

#### Q48：为什么让玩家再次确认，不觉得打断沉浸吗？

答：确认只用于会修改世界的动作，不用于普通对话。它牺牲一次点击，换来明确授权和避免误识别。可以按风险分级：只跟随这类可逆动作允许会话内一次授权；砍树、购物等不可逆或经济动作每次确认，并在确认框展示范围、数量和预算，而不是笼统问“是否同意”。

#### Q49：如果商业化，成本怎么控制？

答：主要成本是 LLM token 和请求次数。手段包括短 Persona、Top-K/context budget、按需注入 Episode、稳定 Lore 缓存、闲聊用小模型、复杂关系/动作才升级模型、相同系统前缀利用 provider cache。需要以每个“有效互动”的成本衡量，而不是只看每 token 单价。玩家数据默认本地存储，模型请求应明确告知并提供关闭云模型选项。

#### Q50：隐私风险有哪些？

答：玩家可能在自由对话中输入真实个人信息，Memory 和 Trace 会长期保存，云模型请求也会离开本机。因此需要最小化收集、默认不记录敏感字段、提供查看/删除单 NPC 或整个存档记忆的 UI、导出前匿名化、Trace 保留期限、API key 不入日志，并在产品层清楚说明第三方模型数据流。

#### Q51：为什么选择《星露谷物语》而不是自建 Demo？

答：它提供持续时间、地图、关系、日程、任务和真实玩家行为，使状态感知、长期记忆和动作恢复都有实际约束；自建小场景更容易做出漂亮演示，却不容易暴露兼容性和持久化问题。代价是受 SMAPI 和原游戏对象模型限制，但这正好能证明系统集成能力。

#### Q52：项目的差异化是什么？

答：差异化不是“使用了 RAG/Memory”，这些是通用方法；而是把有限感知、存档事务语义、原版关系 authority、受控动作状态机和全链路 Trace 放进同一个可运行游戏，并对检索、安全和执行分别评估。面试中应强调组合设计和工程权衡，不声称发明基础算法。

#### Q53：如果只有两周，做什么最能提升求职含金量？

答：不加新功能。第一周完成固定存档实机 benchmark，至少覆盖三个农场动作并收集 60 个 terminal jobs；第二周做 100 个跨 NPC 对话盲评、冻结独立测试集、补 CI 和统一报告。最终展示一张架构图、一段 90 秒视频、一张分层指标表和三个失败案例。这比再接一个向量库或增加购物功能更有说服力。

## 七、面试表达中的红线与加分点

### 不要这样说

- “我的系统越狱识别率就是 100%。”
- “34 个 NPC 都拥有完整知识库。”
- “四层记忆都是独立长期数据库。”
- “动作成功率已经很高。”
- “用了 RAG 所以不会幻觉。”
- “模型可以自主控制游戏。”

### 建议这样说

- “在 42 条攻击、18 条正常输入的项目内固定开发集上达到 100% 攻击召回和 0% 正常误杀，下一步需要独立盲测验证泛化。”
- “34 个角色都有基础 Persona；详细 Lore 当前以 Abigail 为主。”
- “四层上下文，其中 Episode 和 Semantic Fact 是主要持久化层，Profile 是长期聚合视图。”
- “动作权限和状态机已完成，但真实执行证据不足，农场只有 7 个历史任务，矿洞 benchmark 尚未采集。”
- “RAG 改善知识可达性，faithfulness 仍要单独评估。”
- “模型只生成高层提案，确定性游戏端拥有最终写权限。”

### 最容易让面试官记住的三个点

1. **有限感知而非全量状态后提示忽略。**
2. **LLM proposal 与 game authority 分离。**
3. **Memory commit/rollback 与游戏存档事务一致。**

这三个点比单纯罗列 BM25、TF-IDF、FastAPI 更能体现你真正做过系统设计。
