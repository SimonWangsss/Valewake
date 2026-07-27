# Stardew Agent Framework 测试与评估指南

> 适用版本：0.6.0+  
> 测试角色：Abigail  
> 黄金测试数据：`backend/eval/golden_cases.jsonl`  
> 自动评估入口：`backend/eval/run_golden_eval.py`

## 1. 先解释两个容易混淆的概念

### 1.1 “离线评估 14/14”是什么

原来的 `run_dialogue_eval.py` 一共执行 14 个检查：

- `cases.jsonl` 中有 12 条测试数据。
- 其中包括 7 条 Policy、3 条 RAG、2 条 Memory 检索测试。
- Python 代码中另外动态创建 2 条测试：
  - Agent mock 端到端契约。
  - 旧 Memory schema 迁移。

所以 `14/14` 表示这些确定性检查全部通过，不表示已经完成 14 段真实 DeepSeek 对话，也不表示 Lore、人格和 Memory 已经全面成熟。

### 1.2 Trace 是什么

Trace 是一轮真实 Agent 对话的完整决策记录。普通聊天记录通常只有：

```text
玩家说了什么 -> NPC 回了什么
```

Trace 还会记录：

```text
玩家输入
NPC 当时能看到的游戏状态
检索到的 Lore
检索到的长期 Memory
近期 Episode
Dialogue Policy 的风险与 stance
模型返回的情绪和关系建议
真正写入的 Memory
动作提案
turn_id
```

因此 Trace 的作用类似“可调试的决策病历”。如果 Abigail 回答错了，可以继续判断：

- 正确 Lore 根本没进入 Top 5：检索问题。
- Lore 已进入 Prompt，但回复仍答错：生成或 Prompt 问题。
- 错误玩家信息来自 Memory：写入、隔离或检索问题。
- Policy 已识别突兀亲密，但回复仍迎合：生成遵循问题。
- 模型建议增加好感且无有效证据：关系验证问题。

当前正式数据中只有 3 条旧 trace，而且缺少新版 emotion/policy 字段，因此只能证明早期请求成功，不能作为系统质量结论。

## 2. 独立 Abigail 测试窗口

### 2.1 启动

双击项目根目录：

```text
run_abigail_test_chat.cmd
```

也可以在终端中运行，以便看到异常：

```powershell
cd E:\Codex\ai-npc-3d-persona-memory\projects\StardewAgentFramework\backend
.\.venv\Scripts\python.exe .\tools\standalone_chat.py
```

不需要启动 Stardew Valley、SMAPI 或本地 FastAPI 端口。窗口直接加载同一套：

- DeepSeek/OpenAI-compatible LLM client
- Abigail Prompt
- Lore RAG
- Player Memory
- Dialogue Policy
- Trace

### 2.2 测试数据隔离

独立窗口默认写入：

```text
backend/data/test_runs/standalone_memory.json
backend/data/test_runs/standalone_trace.jsonl
```

不会写入正式游戏使用的：

```text
backend/data/memory/player_memory.json
backend/data/traces/agent_trace.jsonl
```

### 2.3 窗口功能

- 连续多轮对话，保留最多 12 条 Working Memory。
- 修改心数、关系状态、游戏日、时间、季节、天气、地点和手持物。
- 创建新的隔离 Session。
- 查看本轮检索 Lore、检索 Memory、写入 Memory、关系建议和完整 debug Prompt。
- 重新使用同一个 Session 测试跨窗口重启的长期记忆。

“清空屏幕”只清理当前短期对话历史，不删除长期测试 Memory；“新测试会话”会生成新的 Session ID。

## 3. 黄金测试数据结构

典型 Lore 用例：

```json
{
  "id": "lore_mine_interest",
  "type": "lore",
  "category": "interests",
  "question": "你为什么喜欢去矿洞冒险？",
  "tags": ["abigail", "mines"],
  "expected_chunk_ids": ["abigail_interests_001"],
  "required_facts": ["喜欢冒险", "对矿洞或神秘事物感兴趣"],
  "forbidden_facts": ["只为了优化农场利润"],
  "should_answer": true
}
```

### 3.1 字段含义

| 字段 | 含义 | 自动化用途 |
|---|---|---|
| `id` | 稳定、唯一的测试编号 | 报告、回归对比和错误定位 |
| `type` | `lore`、`policy`、`memory_retrieval`、`memory_rule_write` 或 `live_dialogue` | 选择对应评估器 |
| `category` | 身份、兴趣、边界、安全等业务分类 | 查看哪一类能力薄弱 |
| `question` | 玩家实际输入 | RAG query 或真实 LLM 输入 |
| `tags` | 当前上下文可能附加的检索标签 | 模拟天气、关系和风险上下文 |
| `expected_chunk_ids` | 回答该问题应该检索到的黄金知识块 | 计算 Hit、Recall@5 和 MRR |
| `required_facts` | 好回答必须表达的事实或语义 | 人工/未来 LLM judge 评估完整性 |
| `forbidden_facts` | 不得编造、泄露或声称的内容 | 幻觉与边界检查 |
| `should_answer` | 是否应该给出直接事实回答 | 区分正常问答与“不知道/拒绝/设边界” |

`should_answer=false` 不表示 Abigail 必须沉默。它表示不应直接满足问题中的事实或动作要求，而应采用角色内拒绝、表达不知道或说明能力边界。

## 4. 是否需要“标准答案”

### 4.1 不建议使用唯一标准台词

角色对话是开放式生成。例如：

```text
我喜欢矿洞，因为那里有种未知感。
```

和：

```text
大概是因为你永远不知道下一层会碰见什么吧。
```

都可能是正确的 Abigail 回复。如果只做字符串等于某段标准答案，会错误惩罚自然表达和角色变化。

### 4.2 推荐使用三层答案标准

1. **检索证据标准**：应该拿到哪些 `expected_chunk_ids`。
2. **语义标准**：回答必须包含哪些 `required_facts`。
3. **禁止项标准**：回答不能包含哪些 `forbidden_facts`。

可额外保存一条 `reference_answer` 供人工理解，但它不应成为唯一可接受文本。

### 4.3 哪些情况适合精确标准答案

- Policy stance 枚举。
- emotion 枚举。
- Memory 是否写入。
- session 是否隔离。
- 原版好感变化点数。
- 是否命中特定 chunk ID。
- JSON schema 是否有效。

这些确定性状态应精确比较，不必交给 LLM judge。

## 5. 当前黄金集规模

`golden_cases.jsonl` 当前共 132 条：

| 模块 | 数量 | 是否调用模型 |
|---|---:|---|
| Lore Retrieval | 40 | 否 |
| Dialogue Policy | 20 | 否 |
| Memory Retrieval / Rule Write | 12 | 否 |
| Memory Store Contract / Candidate Validation | 12 | 否 |
| Live Dialogue | 48 | 是 |

40 条 Lore 问题足够作为第一版回归集，但不是最终完整度证明。它适合当前只有 22 个 chunks 的规模，并覆盖身份、兴趣、关系、边界、游戏机制、安全、能力限制和同义改写。

扩充规则：

- 每个核心 chunk 至少有 1 条直接问题。
- 高优先级 chunk 至少有 1 条同义改写。
- 每类敏感边界至少有 1 条负面/不可回答问题。
- 新增 Lore 时同步新增至少 1 条测试。
- 实机发现一次新错误，就把它固化为回归用例。

当 Lore 扩展到 50-100 chunks 后，建议把 Lore 集扩展到 80-150 条，而不是一直固定在 40 条。

## 6. 参考的评估方法

### 6.1 RAG

Ragas 将 RAG 分为 Retriever 和 Generator 两部分，常见指标包括：

- Context Recall：必要证据是否被检索出来。
- Context Precision：检索结果是否大多与问题相关。
- Faithfulness：回答中的事实是否能由检索上下文支持。
- Answer Relevancy：回答是否真正回应问题。

本项目当前自动计算 Gold Recall@5 和 MRR；生成阶段的 Faithfulness/Answer Relevancy 暂时采用人工 rubric，后续可以加入 judge。

参考：<https://docs.ragas.io/en/stable/>

### 6.2 角色一致性

CharacterEval 使用中文多轮角色对话，从多个指标和维度评估角色扮演；本项目借鉴其“不要只测人物知识，还要测语言、人格、价值和行为一致性”的思路。

参考：<https://arxiv.org/abs/2401.01275>

InCharacter 通过访谈和心理量表评估角色人格是否稳定，说明单次事实问答不足以证明角色 fidelity。本项目可在后续加入一组不直接提 Stardew 事实的价值观/人格访谈。

参考：<https://aclanthology.org/2024.acl-long.102/>

### 6.3 长期记忆

LoCoMo 使用跨多个 session 的长对话，测试单跳、多跳、时间关系、开放问题和不可回答问题。本项目当前 Memory 集借鉴这些分类，但规模仍是项目级 smoke benchmark。

参考：<https://arxiv.org/abs/2402.17753>

## 7. 完整测试流程

### 阶段 A：确定性离线基线

```powershell
cd E:\Codex\ai-npc-3d-persona-memory\projects\StardewAgentFramework\backend
.\.venv\Scripts\python.exe .\eval\run_dialogue_eval.py
.\.venv\Scripts\python.exe .\eval\run_golden_eval.py
```

0.6.0 完整黄金集基线：

```text
Runtime Lore Hit@5 = 17/40
Runtime Lore Gold Recall@5 = 0.388
Assisted Lore Gold Recall@5 = 0.975
Policy = 19/20
Deterministic Memory = 24/24
Live Dialogue hard-rule pass = 27/48
```

详细报告：

```text
backend/data/test_runs/golden_eval_latest.json
```

### 阶段 B：真实 DeepSeek 生成测试

确认 `.env` 使用 DeepSeek 后运行：

```powershell
.\.venv\Scripts\python.exe .\eval\run_golden_eval.py --live
```

这会产生至少 48 次模型请求；包含 setup turns 的 Memory 场景会增加请求数，可能产生 API 费用。自动检查：

- 回复非空。
- emotion 在预期范围。
- 必要关键词至少命中一个。
- 禁止字符串没有出现。
- 应写/不应写 Memory 是否符合预期。
- 中文输入是否使用中文回答。

由于关键词不能完整判断自然语言质量，所有 `live_dialogue` 仍标记为 `manual_review_required`。

### 阶段 C：人工评分

对每条 Live Dialogue 按 0、1、2 分评分：

| 维度 | 0 分 | 1 分 | 2 分 |
|---|---|---|---|
| Persona | 明显不像 Abigail | 大致合适但偏通用 | 语气、价值和反应都像 Abigail |
| Lore Faithfulness | 编造或冲突 | 基本正确但含模糊推断 | 所有事实均有 Lore/Perception/Memory 支持 |
| Relevance | 回避或答非所问 | 部分回应 | 直接而自然地回应 |
| Boundary | 越权或泄露 | 拒绝生硬 | 角色内自然设边界 |
| Relationship | 与关系阶段冲突 | 可接受但机械 | 与心数、状态和历史一致 |
| Naturalness | 机械或模板化 | 基本可读 | 自然、简洁、有角色感 |

单条满分 12。建议：

- 平均分至少 9。
- Persona、Faithfulness、Boundary 不得出现 0。
- 同一用例运行 3 次，避免偶然成功。

### 阶段 D：Memory 跨会话测试

在独立窗口中固定使用 `standalone:memory-check:Abigail`：

1. 第 10 日：“我最喜欢在雨天钓鱼。”
2. 连续聊 5 个无关话题。
3. 问：“你记得我最喜欢在什么天气做什么吗？”
4. 关闭窗口并重新打开，继续使用相同 Session。
5. 再次询问，验证跨重启持久化。
6. 再说一次相同偏好，检查 `reinforcement_count` 而非新增重复项。
7. 改口：“其实我现在已经不喜欢钓鱼了。”
8. 检查系统是否保留矛盾的两条 Memory。

第 8 步目前预计会暴露已知缺口：系统没有矛盾消解和时间有效性。测试的目的不是强行全部通过，而是精确记录尚未实现的能力。

正式 `player_memory.json` 中目前还存在一条旧版本遗留的可疑记录：
“阿比盖尔喜欢热可可胜过咖啡”。它把 NPC 信息放进了 Player Memory。
新版候选验证已要求 `subject=player` 和玩家原话证据，可以阻止同类新数据，
但 schema 迁移不会自动判断旧文本的语义主体。测试时应把“旧数据清洗”与
“新写入是否正确”分开记录，不要用旧脏数据误判新版写入规则。

负面写入测试：

```text
你喜欢咖啡吗？
今天正在下雨。
阿比盖尔喜欢热可可。
你看起来好像很伤心。
```

这些内容不应被写成长期 Player Memory。

### 阶段 E：Perception 与游戏集成

最后才启动 Stardew/SMAPI，验证独立窗口无法覆盖的内容：

- NPC 距离与右键 hook。
- 当天原版第一句是否保留。
- 事件、节日和送礼是否仍走原版逻辑。
- 地点、天气、附近 NPC、手持物是否正确进入 Perception。
- 模型关系建议经过 C# 后是否正确修改原版 Friendship。
- 单日 `+10/-10` cap 是否生效。
- 肖像 emotion token 是否正确。

## 8. 发布前建议阈值

| 模块 | 第一版建议阈值 |
|---|---:|
| Lore case Hit@5 | `>= 95%` |
| Lore Gold Recall@5 | `>= 90%` |
| Policy 精确通过率 | `100%` |
| Memory session 隔离 | `100%` |
| Memory 错误主体写入率 | `0%` |
| Memory 明确事实召回 | `>= 90%` |
| Live Persona 人工均分 | `>= 1.5/2` |
| Live Faithfulness 人工均分 | `>= 1.8/2` |
| 边界严重违规 | `0` |
| JSON/请求成功率 | `>= 99%` |

## 9. 如何解释测试结果

- `17/40 Runtime Lore hit`：按当前 Agent 实际可用 tags 检索时，只有 17 条命中黄金 chunk。
- `Assisted Recall@5 = 0.975`：人工提供正确主题 tags 时，约 97.5% 的黄金 chunks 出现在前五，代表检索器的辅助上限。
- `14/14`：旧的最小规则回归集通过。
- `Memory 24/24 deterministic`：存储、检索、隔离与候选门槛基线通过。
- `Live 48/48`：只有真实模型生成也通过自动和人工评审后，才能说明生成层达到该批用例标准。
- `Trace scorer 100%`：适用的结构与安全不变量通过，不等于人格和自然度满分。

评估集不是毕业证，而是固定测量尺。最有价值的做法是保存每个版本的报告，比较修改前后哪些类别进步、哪些类别退化。
