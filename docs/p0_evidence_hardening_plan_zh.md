# Valewake P0 证据强化实施方案

> 更新基线：`develop@ba66c59`，2026-08-27。目标不是继续增加能力，而是把已实现能力转化为可复现、可审计、能经得住面试追问的证据。

## 1. 当前已经自动记录什么

### 1.1 对话 Trace：`backend/data/traces/agent_trace.jsonl`

每轮自动记录：

- `turn_id / created_at / session_id / npc`；
- 玩家输入、最近会话历史、NPC Persona；
- NPC 局部感知、社交上下文、Dialogue Policy；
- Lore 检索结果（chunk ID、文本、标签、来源、parent、分数）；
- 检索到的 Memory、Durable Profile、近期 Episode；
- 最终回复、情绪、关系变化提议、Memory 写入、Action Proposal；
- Action eligibility/decision context；
- JSON 是否解析成功、是否重试；
- 每次 LLM attempt 的 provider、model、thinking mode、latency、prompt/completion/total tokens，以及总 attempt 数和总时延。

### 1.2 数据集事件与人工标注

- `dataset_events.jsonl`：Memory commit、rollback、save-loaded rollback，以及关系实际应用结果；
- `dataset_annotations.jsonl`：通过 `agent_mark` 写入 `keep/reject/boundary/memory_good/memory_bad/action_good/action_bad` 和备注；
- `export_dataset.py`：按 `turn_id/proposal_id` 关联对话、保存边界、动作事件，支持匿名化和过滤回滚样本。

### 1.3 农场动作 Trace：`Mods/Valewake/data/traces/action_trace.jsonl`

自动记录：

- proposal validation、玩家确认、accepted；
- dispatch/preparing/navigating/acting/background/returning 状态；
- `target_path_retry / target_path_failed / return_path_retry`；
- `target_strike / target_completed / target_skipped / background_target_completed`；
- completed、failed、cancelled、reload recovery；
- `turn_id / proposal_id / job_id / save_id / NPC / action`；
- completed/failed/total target 数、当前 target 和 NPC tile；
- target type、qualified ID、是否通过 allowlist；
- 当前/返回 location，以及 terminal 时 `location_restored / schedule_restored`。

### 1.4 矿洞动作 Trace：`Mods/Valewake/data/traces/expedition_trace.jsonl`

除公共关联字段外，还记录：

- capability upgrade、玩家换图、跨层跟随；
- follow stall、offscreen recovery、navigation recovery/failure；
- priority target、node mined、strike-limit skip；
- monster defeated；
- defense/mining mode、resource priority、目标 ID/name/allowlist、威胁类型/距离；
- terminal 时 location/schedule restoration。

### 1.5 当前 scorer 已经输出的指标

`backend/eval/score_action_traces.py` 已实现：

- terminal coverage；
- state task success；
- productive task success；
- no-op completed 数；
- target success rate；
- mean completed targets；
- trace linkage rate；
- schedule + location restore rate（当前合并为一项）；
- allowlist invariant rate；
- path retry event 数；
- 矿洞换图、击败怪物、开采节点数；
- terminal sample 少于 20 时提示样本不足。

## 2. 指标实现状态（Trace v2 更新后）

| 指标 | 当前状态 | 缺口 |
|---|---|---|
| Productive success | 已实现 | 分母需固定为所有 accepted terminal trials，并按 action/version 分层 |
| No-op rate | 可从 scorer 直接算 | 当前报告给 no-op 数，建议同时输出 `no_op_completed / completed` 和原因分类 |
| Target success rate | 已实现 | 它是“执行目标成功率”，不是 target recall |
| Target precision | Trace v2 已实现 world-diff 口径 | 仍需在 Windows 实机验证 snapshot 是否覆盖所有可能受损对象 |
| Target recall | Trace v2 实现为 opportunity recall | 使用 baseline eligible 与 `max_targets` 形成分母，更适合有任务上限的系统 |
| P95 completion time | scorer 已实现 active tick 与 wall-clock P50/P95 | active tick 尚未扣除显式 pause ticks，需实机校准 |
| Path retry rate | Trace v2 已记录 attempt/retry 分母并计算 rate | reason taxonomy 仍可继续细化 |
| Schedule restore rate | 已增加 90 tick 延迟 postcheck | 需在 Windows 验证 `checkSchedule` 后 exact tile/facing 的解释方式 |
| Unintended damage | Trace v2 已实现受控 world snapshot/diff | 当前覆盖 Farm Objects、HoeDirt、Tree 和其他 TerrainFeature，仍需实机检查 debris/特殊对象 |

结论：代码现在已经具备计算上述 v2 指标所需的主要字段；在 Windows 实机产生新版 Trace 前，只能说“指标管线已实现并通过合成数据测试”，不能提前声称真实指标已经达标。

## 3. Trace v2 最小补充字段

不要重写整个日志系统，只为每个 trial 增加以下事件和字段。

### 3.1 公共运行元数据

```json
{
  "trace_schema": "valewake-action-trace-2",
  "run_id": "run_...",
  "scenario_id": "water_blocked_03",
  "trial_id": "trial_...",
  "code_commit": "ba66c59",
  "config_hash": "...",
  "save_fixture": "farm-benchmark-v1",
  "game_day": 12,
  "game_time": 900,
  "game_tick": 123456
}
```

### 3.2 `job_baseline` 事件

在选择目标前记录：

- scope/anchor/radius/max targets；
- scope 内全部 `eligible_targets_before`；
- `protected_objects_before`；
- 每个目标的 tile、type、qualified ID、关键 precondition 和状态 hash；
- NPC 原 location/tile/facing/followSchedule/controller 摘要；
- 玩家/地图最小必要状态。

### 3.3 路径事件

每次路径尝试记录：

- `path_attempt_id / phase / target_key / candidate_index`；
- `started_tick / ended_tick / outcome`；
- `reason_code`：no_path、timeout、controller_stopped、target_changed、map_changed 等。

### 3.4 `target_outcome` 事件

记录：

- `target_key / selected_from_baseline / eligible_at_execution`；
- pre/post state hash；
- `mutation_observed`，而不是只使用执行器返回值；
- 周边 protected objects 的 pre/post hash；
- completed/skipped/failed 及 reason code。

### 3.5 `job_postcheck` 事件

在 restore 后等待 60—120 ticks 再记录：

- active ticks、wall-clock ms；
- NPC location/tile/facing/followSchedule/controller；
- 是否重新进入应有 schedule；
- world diff、unintended mutations；
- no-op reason。

## 4. 指标的统一定义

以每个 action、代码版本和 scenario stratum 分开报告。

```text
productive_success
= terminal trials with completed_targets > 0
  / all accepted terminal trials

state_completion_rate
= completed terminal trials / all terminal trials

no_op_rate
= completed trials with completed_targets = 0
  / all completed trials

target_precision
= intended eligible targets actually mutated
  / all world objects mutated by the job

opportunity_recall
= successfully completed baseline-eligible targets
  / min(number of baseline-eligible targets, max_targets)

target_execution_success
= completed selected targets
  / all attempted selected targets

job_path_retry_rate
= jobs with at least one retry / terminal jobs

path_attempt_retry_rate
= retry attempts / all path attempts

schedule_restore_rate
= jobs passing delayed full restore postcheck / restorable terminal jobs

P95_active_duration
= P95(terminal_tick - accepted_tick - explicitly paused ticks)
```

`opportunity_recall` 的名称比笼统的 target recall 更准确，因为任务有 `max_targets` 上限，不应该要求一次任务处理范围内所有对象。

## 5. 农场动作实机 Benchmark

### 5.1 最小规模

- `water_crops`：20 trials；
- `clear_weeds`：20 trials；
- `chop_trees`：20 trials；
- 共 60 个 terminal trials；
- 每个 trial 从同一个只读 fixture 的副本或确定性重建状态开始。

### 5.2 每类动作的分层矩阵

每类至少覆盖：

| 维度 | 取值示例 |
|---|---|
| NPC 起点 | Farm / FarmHouse / Town 或其他地图 |
| 玩家位置 | Farm / off-farm |
| 目标规模 | 0 / 1 / 3 / 达到 cap / 超过 cap |
| 路径 | 正常 / 单候选阻塞 / 多候选阻塞 / 中途目标变化 |
| 时间 | 上午 / 接近截止时间 |
| 可见性 | 玩家观察 / off-screen background |
| 恢复 | 完成 / 取消 / path failure / reload recovery |

破坏性动作额外加入 protected objects：幼树、树桩、装有采集器的树、箱子、机器、装饰、非 weed 对象和作物。

### 5.3 Release gate

- 每个 action 至少 20 个 terminal trials；
- productive success ≥ 90%；
- target precision = 100%；
- unintended mutation = 0；
- delayed schedule restore ≥ 95%；
- no-op rate ≤ 5%；
- P95 active duration 与 retry rate 先报告基线，再根据错误分布定门槛。

## 6. 独立测试集和开放对话盲评

### 6.1 数据集三分

- `dev`：可查看、用于修改规则；
- `validation`：阶段性选择阈值；
- `blind_test`：冻结，最终版本才运行；
- 按“来源人/真实会话/语义模板”分组切分，不能把同一模板的轻微改写分到 dev 和 test；
- 保存 dataset version、SHA-256、代码 commit、Prompt/model 版本。

动作/安全 blind test 优先收集：错别字、口语、省略、否定、过去式、偏好陈述、多意图、多轮拆分、含技术词但无攻击意图的 hard negatives。

### 6.2 对话盲评

- 至少 100 个 context，覆盖 ≥10 NPC、不同关系阶段/时间/天气/地点；
- 比较 Persona-only、Persona+RAG、完整系统三个版本；
- 每个 context 的三个输出随机化和匿名化；
- 两名评价者按 0—2 分标角色一致性、事实正确、相关性、自然度、Memory 使用、边界遵循；
- 输出均分、pairwise win rate、95% bootstrap CI、评价者一致率/Cohen's kappa；
- 单列严重失败，不能用平均分掩盖儿童边界、串 Memory 和错误副作用。

## 7. 连续 7—30 游戏日自动化方案

建议同时建设两条轨道，不让真实游戏进程承担所有测试。

### 7.1 Track A：Backend Memory Simulator（完全 headless）

用途：快速、确定性地验证 Memory 写入、检索、冲突、衰减、隔离和 commit/rollback。

Scenario DSL 示例：

```yaml
scenario_id: preference_changes_over_14_days
save_id: memory_fixture_01
npc: Abigail
days:
  - day: 1
    turns:
      - input: "我喜欢下矿。"
        expect_write: ["mining"]
    save: commit
  - day: 7
    turns:
      - input: "你还记得我喜欢做什么吗？"
        expect_retrieval: ["mining"]
    save: commit
  - day: 14
    turns:
      - input: "我现在不喜欢下矿了。"
        expect_supersede: ["mining"]
    save: rollback
  - day: 15
    turns:
      - input: "我喜欢下矿吗？"
        expect_retrieval: ["mining_positive"]
```

Runner 行为：

1. 每个 scenario 使用临时 Memory/Trace 目录；
2. 构造带 year/season/day 的 `game_state`；
3. 默认使用 mock/frozen LLM 输出，避免网络和随机性；
4. 调用 `StardewAgent.chat` 或 `/chat`；
5. 按脚本调用 `/memory/commit`、`/memory/rollback`；
6. 每轮读取 Memory 状态并断言 write/retrieve/supersede/leakage；
7. 输出 scenario-level JSON 报告。

建议用例：

- 7/14/30 日后的记忆召回；
- 同一事实多次强化；
- 正负偏好冲突与 rollback；
- 问句、临时状态、NPC 事实不写入；
- save A/B 和 NPC A/B 四象限隔离；
- 多轮越狱期间不写 Memory；
- Episode 达到上限后的淘汰；
- `days_since_last_interaction` 与重复话题跨日重置。

这条轨道可以从命令行开始到结束完全无人操作，也适合 CI。它证明 Memory 内核，但不证明 SMAPI 的真实保存事件。

### 7.2 Track B：SMAPI Scenario Runner（真实游戏集成）

新增仅在 `TestMode=true` 且专用测试存档中启用的 runner：

```text
SaveLoaded
 -> 读取 scenario JSON
 -> 校验 save fixture / commit / config hash
 -> DayStarted 设置场景
 -> 定位玩家和 NPC、布置目标/阻塞物
 -> 直接调用与真实 UI 共用的 Chat/Proposal service
 -> TestMode 自动确认（仍走相同 validator 和 Accept）
 -> 按 tick 等待 terminal/postcheck
 -> 写 assertion + world diff
 -> 触发保存/睡眠进入下一天
 -> 运行下一 scenario step
 -> 结束时输出 report 并停止 runner
```

实现要点：

- 把当前 `SendChatToBackendAsync` 后的 proposal 处理抽成可复用 service；runner 不应复制业务规则；
- 自动确认只能在 `TestMode + dedicated save ID allowlist` 下生效，正式发布包强制关闭；
- 用 `SaveLoaded / DayStarted / DayEnding / Saved / UpdateTicked` 驱动状态机；
- 使用 game tick 推进和超时，不使用阻塞 sleep；
- 每一步具有 `step_id` 和幂等状态，崩溃后不得重复 mutation；
- 测试 fixture 必须是专用副本，绝不对真实游玩存档运行；
- 真实模型测试与确定性回归分开，前者至少重复 3 次并记录模型版本和 token/latency。

SMAPI/游戏自带 debug 命令支持 `sleep/newday`、时间、天气、warp、minelevel 和宏，可用于准备原型；长期应让 Scenario Runner 调用稳定的游戏/domain 接口，而不是依赖从外部向控制台逐条输入字符串。

### 7.3 “完全零人工”能做到哪一步

| 范围 | 可行性 | 建议 |
|---|---|---|
| 7—30 日 Memory 内核脚本 | 完全无人、稳定 | 立即做，进入 CI |
| 已加载专用存档后的 7—30 日 SMAPI 场景 | 完全无人、可稳定实现 | 推荐的真实集成路径 |
| 从启动游戏、选择存档、跑完、退出全流程 | 技术上可做 | 自动加载存档或 UI 自动化较脆弱，只做 nightly harness |
| 无窗口、无图形环境的真正 headless Stardew | 不应假设可用 | 游戏仍依赖图形/update loop；CI 中用 backend/C# fake world 替代 |

若目标是求职证据，“一次加载专用存档后自动跑完 30 天”已经足够可信；没有必要为了零点击启动而引入脆弱的标题菜单自动化。

## 8. 1—2 周实施排期

### 第 1—2 天：指标与 Schema

- 固定指标定义和分母；
- 增加 run/scenario/trial 元数据；
- 增加 baseline、path attempt、target outcome、delayed postcheck；
- scorer 增加分 action/stratum、P50/P95、两种 retry rate、precision/opportunity recall/no-op rate。

### 第 3—4 天：确定性场景与 Memory Simulator

- 编写 Scenario DSL 和 runner；
- 建 20—30 个 7/14/30 日 Memory 场景；
- mock/frozen 模型进入 CI；
- 输出 HTML/Markdown + JSON 报告。

### 第 5—7 天：SMAPI Runner 最小闭环

- TestMode/专用存档保护；
- 自动布置 farm state、提交 proposal、确认、等待 terminal；
- world pre/post snapshot 和 NPC delayed restore postcheck；
- 先自动完成 watering 的 5 个 smoke trials。

### 第 8—10 天：60 个实机 Trial

- 扩展 watering/weeding/chopping 分层矩阵；
- 每类至少 20 个 terminal trials；
- 修复 benchmark 暴露的问题，但禁止改 blind test；
- 生成含样本数、失败分类和典型 Trace 的报告。

### 第 11—12 天：独立集和盲评

- 收集外部表达并按来源分组切分；
- 生成 100 context × 3 system variants 的匿名评审包；
- 至少两名评价者完成标注。

### 第 13—14 天：可复现交付

- 固定 Python/.NET/SMAPI/游戏版本；
- CI 跑 54+ Python tests、golden eval、Memory simulator、schema validation；
- 统一从报告生成 README/简历数字；
- 发布架构图、90 秒演示、指标表和失败分析。

## 9. 最终面试交付物

1. 一张 authority pipeline 架构图；
2. 一个 60-trial 农场 benchmark 报告；
3. 一个 7—30 日 Memory timeline 报告；
4. 一个 100-context 对话盲评报告；
5. 三个失败案例及修复前后 Trace；
6. 一条命令复跑确定性评估，报告自动包含 commit、dataset hash 和环境版本。

完成这些以后，可以诚实地把项目描述从“实现完整的个人 Agent Demo”升级为“具备离线回归、长期状态测试和真实游戏执行 benchmark 的受控 Agent 系统”。
