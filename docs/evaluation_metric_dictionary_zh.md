# Valewake 评估指标字典

## 1. 先区分四个对象

- **Trial/Job**：一次被玩家确认并接受的任务。
- **Selected target**：执行器实际尝试处理的目标。
- **Eligible opportunity**：任务开始时范围内满足规则的目标；受 `max_targets` 限制。
- **Observed mutation**：任务前后 world snapshot 中实际发生变化的对象。

“任务完成”“至少做了一件有用的事”“选中的目标成功”“没有改错对象”是四件不同的事，不能共用一个 success rate。

## 2. 动作指标

| 指标 | 回答的问题 | 公式 | 必需数据 | 注意事项 |
|---|---|---|---|---|
| Terminal coverage | 接受的任务都有结论吗 | terminal jobs / accepted jobs | accepted、completed/failed/cancelled/reload recovery、job ID | 小于 100% 说明有悬挂任务或日志缺失 |
| State completion rate | 状态机报告成功的比例 | completed / terminal | terminal event/state | 可能 completed 但零产出 |
| Productive task success | 玩家是否至少得到一次有效结果 | completed 且 completed_targets>0 / terminal | terminal、completed_targets | 最适合作为动作主成功指标之一 |
| No-op rate | 报告完成却没干成任何事的比例 | completed 且 completed_targets=0 / completed | terminal、completed_targets、no-op reason | 目标本来为 0 的场景应单独分层 |
| Target execution success | 已选目标的执行可靠性 | completed targets / (completed+failed/skipped targets) | 每目标 outcome 或 terminal count | 不是 recall；没被选中的 eligible target 不在分母中 |
| Opportunity recall | 在任务额度内抓住了多少可完成机会 | completed baseline-eligible / min(baseline eligible, max_targets) | job baseline 全量 eligible、max_targets、target outcomes | 目标中途被玩家改变时应额外报告 invalidated 数 |
| Target precision | 实际变化中有多少属于合法目标 | intended observed mutations / all observed mutations | world snapshot before/after、selected target keys | 只看 executor 的 `target_allowed` 不够，需要独立 world diff |
| Unintended mutations | 是否改坏不该碰的对象 | count(unexpected changed keys) | protected/world before/after hash | 破坏性动作发布门槛应为 0 |
| Allowlist invariant | 执行器尝试的目标是否通过白名单 | allowed target attempts / verified attempts | 每目标 type/ID/allowlist result | 是内部安全不变量，不等价于 precision |
| Mutation verification rate | executor 报成功时是否观察到状态变化 | successful outcomes with pre!=post / successful outcomes | target pre/post state、outcome | 对纯跟随等无 mutation 动作不适用 |
| Job path retry rate | 有多少任务遇到过寻路重试 | jobs with retry / terminal jobs | job ID、retry count | 反映玩家体验和场景难度 |
| Path attempt retry rate | 每次寻路尝试的稳定性 | retries / path attempts | path attempt ID/start/result/reason | 应区分 target path 和 return path |
| P50/P95 active duration | 一般/尾部任务需要多久 | accepted-to-terminal active ticks 的分位数 | accepted tick、terminal tick、paused ticks | 优先用 game ticks；wall time 会包含暂停和菜单 |
| Schedule restore rate | 任务后 NPC 是否恢复日程开关 | schedule flag restored / restorable jobs | expected/actual followSchedule、delayed postcheck | 仅即时检查可能漏掉随后被破坏的问题 |
| Location restore rate | NPC 是否回到正确地图 | correct location / restorable jobs | expected/actual location | 与 exact tile 分开报告 |
| Exact tile/facing restore | 是否恢复精确姿态 | exact tile/facing pass / applicable jobs | expected/actual tile/facing | 如果恢复日程后 NPC 已继续走动，exact tile 不应作为主失败条件 |
| Trace linkage rate | 能否重建因果链 | accepted rows with turn+proposal ID / accepted | turn_id、proposal_id、job/expedition_id | 影响失败定位，不直接代表体验质量 |

### 推荐主面板

对每个 action 单独报告：

1. 样本数和 strata；
2. productive success；
3. target precision 与 unintended mutations；
4. opportunity recall；
5. P95 active duration；
6. job/path retry rate；
7. combined restore rate；
8. no-op rate。

不要把这些平均成一个不透明的“Agent 总准确率”。

## 3. Memory 指标

| 指标 | 回答的问题 | 公式 | 必需数据 |
|---|---|---|---|
| Write precision | 写下来的内容有多少真的该长期记 | 正确写入事实 / 全部写入事实 | 每轮输入、实际 write、人工 gold：should_write + normalized fact |
| Write recall | 应该记住的事实有多少被写入 | 正确写入的 gold facts / 应写 gold facts | 带 expected facts 的标注输入集 |
| Retrieval Recall@K | 需要时能否找回 gold memory | Top-K 命中的 gold / gold 数 | query、active memory corpus、gold memory IDs、Top-K IDs |
| Context precision@K | 注入的 Memory 有多少相关 | Top-K 中相关 memory / K | 每条 retrieved memory 的相关性标注 |
| MRR | 第一条相关记忆排得多靠前 | mean(1 / first relevant rank) | 每个 query 的排序结果和 gold IDs |
| Contradiction resolution | 新事实能否替代旧事实 | 正确 active/superseded 场景 / 冲突场景 | old/new fact、canonical topic、polarity、valid time、最终状态 |
| Rollback correctness | 未保存退出是否恢复 checkpoint | rollback 后状态完全匹配 committed snapshot 的场景 / rollback 场景 | commit snapshot、unsaved mutations、rollback snapshot |
| Cross-save/NPC leakage | 是否把别处记忆取回来 | 错误跨 session 命中 / isolation queries | save ID、NPC ID、query、retrieved memory session IDs |
| Stale memory rate | 过期信息是否仍被当成当前事实 | 错误使用 stale facts / stale queries | valid_from/to、supersedes、query day、retrieved IDs |
| Reinforcement correctness | 重复陈述是否合并而非复制 | 正确 dedup/reinforce 场景 / 重复场景 | canonical key、active count、reinforcement_count |
| Episode retention | 容量策略是否正确 | 符合保留策略的场景 / capacity 场景 | episode order/day、capacity、retained IDs |

## 4. 当前 Memory Simulator 能证明什么

当前 simulator 是 deterministic contract suite，包含 8 个场景、24 个断言，覆盖 1—30 游戏日：

- 30 日后检索；
- contradiction commit/rollback；
- save/NPC 隔离；
- 问句和瞬时天气不写入；
- 20 条 Episode 容量；
- 未保存事实回滚；
- 重复事实去重。

它当前输出的是 scenario/assertion pass rate。这个结果证明代码契约没有回归，但不能直接称为 Memory write precision/recall；后两者必须增加一批逐条人工标注的 should-write、expected-fact 和 relevant-memory gold 数据。

## 5. 最少要采集的数据

### 动作 Trial

```text
run/scenario/trial/action/version/config
accepted/terminal event + game tick
baseline eligible targets + max_targets
selected targets
每次 path attempt/retry/reason
每个 target pre/post/outcome/allowlist
world before/after diff
NPC expected/actual restore state（延迟检查）
```

### Memory Case

```text
save_id + npc + game_day + turn_id
player input
gold should_write + normalized expected facts
actual memory writes
query + gold relevant memory IDs
retrieved ranked IDs
commit/rollback boundary
active/superseded/version state
```

没有这些分母和 gold label 时，可以报告事件数量或规则不变量，但不要把它命名为 precision、recall 或真实准确率。
