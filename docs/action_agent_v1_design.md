# Valewake Action Agent v1 设计

## 1. 目标与边界

第一版让玩家通过自然对话委托 NPC 完成少量、可撤销或损失受控的工作：

- 浇灌指定范围内未浇水的耕地。
- 清理指定范围内的杂草。
- 使用明确来源的种子在指定耕地种植。
- 在明确商店购买明确数量的物品。
- 执行一段有时间成本和产出上限的钓鱼任务。

LLM 只负责理解请求、生成角色态度和提出结构化 `action_proposal`。它不能直接调用
SMAPI、修改地图、扣钱、移动 NPC 或生成物品。

## 2. 不能直接“同意后执行”的原因

一句自然语言通常缺少执行所需参数。例如“帮我浇水”没有说明：

- 哪块田、最多多少格、什么时候开始。
- NPC 是否应该中断原日程。
- 是否允许跨地图移动或瞬移。
- 下雨、节日、剧情事件、NPC 睡眠或地图变化时怎么办。
- 多人模式由谁授权。
- 中途失败后是否恢复原位置和日程。

因此需要把对话和游戏修改分成两个权限域。

```text
玩家自然语言
  -> LLM Action Proposal
  -> Proposal Validator
  -> NPC 是否愿意
  -> Action Policy
  -> 玩家确认
  -> Persistent Job Queue
  -> Deterministic Planner
  -> NPC Movement Controller
  -> Typed Executor
  -> Postcondition Verifier
  -> Trace + 对话反馈
```

## 3. 核心数据结构

```json
{
  "proposal_id": "proposal_001",
  "npc_name": "Abigail",
  "intent": "request_help",
  "action": "water_crops",
  "parameters": {
    "location": "Farm",
    "area": "near_player",
    "max_tiles": 20
  },
  "requires_confirmation": true,
  "reason": "The player directly requested help."
}
```

确认后转换成不再由 LLM 修改的 Job：

```json
{
  "job_id": "job_001",
  "save_id": "MyFarm_123",
  "npc_name": "Abigail",
  "action": "water_crops",
  "parameters": {
    "location": "Farm",
    "tiles": [[42, 18], [43, 18]]
  },
  "resource_budget": {},
  "created_day": 36,
  "expires_day": 36,
  "state": "accepted",
  "progress": {
    "completed": 0,
    "failed": 0
  },
  "return_context": {
    "location": "Mountain",
    "tile": [15, 22],
    "schedule_resume_time": 1340
  }
}
```

Job 状态机：

```text
proposed -> confirmed -> accepted -> travelling -> executing
         -> completed
         -> refused
         -> cancelled
         -> failed_recoverable
         -> failed_terminal
```

## 4. 权限与关系门槛

不建议只使用好感度。是否执行应同时考虑：

1. 原版心数：关系基础。
2. Valewake Trust：玩家是否有可靠的历史。
3. NPC affinity：该角色是否愿意或擅长这类工作。
4. 当前日程：是否上班、睡觉、参加节日或事件。
5. 明确同意：NPC 可以基于人格拒绝。
6. 资源和风险：是否消耗种子、金钱或稀有物品。
7. 玩家确认：高风险动作必须逐次确认。

建议默认门槛：

| 操作 | 心数 | Trust | 确认 | 默认上限 |
|---|---:|---:|---|---:|
| 浇附近作物 | 2 | 0 | 首次/范围变化时 | 20 格/天 |
| 清理杂草 | 2 | 0 | 每次确认范围 | 15 个/天 |
| 指定区域种植 | 4 | 5 | 每次确认种子、数量、区域 | 20 格/天 |
| 钓鱼任务 | 4 | 5 | 确认时长和产物归属 | 2 游戏小时 |
| 代买物品 | 6 | 15 | 每笔确认商品、数量、总价 | 2000g/天 |
| 重复/每日委托 | 8 | 25 | 创建规则时确认 | 可配置 |

这些是默认策略，不是写死人格。Willy 对钓鱼、Marnie 对动物相关工作可以降低
affinity 门槛；George、Jas 或正在上班的 NPC 可以自然拒绝不合适的任务。

## 5. 各动作实现难度

### 5.1 浇水：中等，适合作为第一个动作

Planner 只选择农场中未浇水、存在 `HoeDirt`、未死亡且在授权区域内的 tile。
NPC 使用局部寻路走到相邻可站立 tile，播放面向和工具动画，Executor 再把该
`HoeDirt` 标记为已浇水。每格执行后验证状态，不做“一次循环改全图”。

第一版可给 NPC 使用虚拟水壶，但必须通过每日格数和时间成本平衡。后续可以增加水量、
水源补充和不同 NPC 效率。

### 5.2 除草：中等

只允许目标分类为 weeds 的可破坏对象。禁止把作物、树苗、围栏、箱子、装饰和玩家放置
物品纳入目标。优先调用正常 tool-action 路径以保留掉落物；若使用直接删除，需要自行
生成正确掉落并记录。

### 5.3 种植：中高

必须明确：

- 种子物品 ID、数量和所有者。
- 允许种植的 tile 集合。
- 当前季节和地点是否合法。
- 失败时如何退还未使用种子。

第一版建议使用农场上的“工作箱”作为资源来源，不直接读取或扣除玩家随身背包。
Executor 对种子采用事务式处理：预留、逐格消耗、失败退还。

### 5.4 购物：高风险

购物不是简单生成物品。需要验证商店、营业时间、商品库存、单价、数量、玩家余额和交付
位置。确认窗口必须显示总价。只有主机可以扣钱；扣款与交付要形成一个事务，任一步失败
都回滚。第一版仅支持固定白名单商店和普通商品，不购买限量、任务或高价值物品。

### 5.5 钓鱼：高

原版 `FishingRod` 和小游戏围绕 `Farmer` 实现，普通 NPC 不能直接复用完整玩家钓鱼流程。
第一版适合做“可观察的异步工作”：

- NPC 走到合法水边并播放钓鱼动画。
- 根据地点、季节、时间、天气和任务时长调用受控产出规则。
- 产物放入指定工作箱，并记录来源。
- 设置每日次数、品质和价值上限。

这比让 NPC 隐式操纵玩家的钓鱼小游戏稳定，也更符合 NPC 自己在工作的表现。

## 6. 移动、日程与失败恢复

同地图移动可以优先使用游戏的 `PathFindController`，并在到达后触发 typed executor。
跨地图移动应由独立 Route Planner 处理 warp 图。第一版不要承诺完全真实的全程跨图行走：
可以在屏幕外从合法出口切换地图，但玩家看得见时必须实际行走。

接受任务时保存原日程上下文。以下情况立即拒绝或暂停：

- 节日、事件、婚礼或特殊剧情。
- NPC 睡眠、工作关键时段或不可社交状态。
- 目标地图未加载、tile 不可达或被玩家新放置物阻挡。
- 玩家离开存档、当天结束或多人主机断开。

Job 完成、取消或失败后恢复 NPC 日程；不能恢复时将 NPC 放到安全的合法入口 tile，并记录
失败原因。

## 7. 多人模式与存档安全

- 只有主机创建和执行世界修改 Job。
- 农场主拥有金钱、物品和永久地形修改的最终确认权。
- Job 队列写入 SMAPI save data，保存中断状态。
- 每个执行步骤必须幂等，读档后不能重复扣钱、消耗种子或发放产物。
- Trace 记录 proposal、policy、confirmation、targets、每步结果、资源变化和最终状态。
- 配置提供全局 Action Agent 开关和每种动作独立开关。

## 8. 推荐实施顺序

1. 完成 typed proposal、规则验证、确认菜单、Job Queue 和 Trace，不执行动作。
2. 实现“NPC 已在农场时，浇附近最多 10 格”，验证局部寻路和逐格执行。
3. 增加跨地图派工、日程占用和恢复。
4. 增加除草及严格目标白名单。
5. 增加工作箱和事务式种植。
6. 增加白名单购物及金额确认。
7. 最后实现模拟钓鱼任务。

可参考 SMAPI 生命周期和游戏 `PathFindController` 的使用方式。Custom Companions 可用于研究
跟随实体的更新模式；本地 `stardew-mcp` 可用于研究状态序列化、A* 和 typed command 的模块
边界。Valewake 应自行实现代码，尤其本地 `stardew-mcp` 未声明许可证，不直接复制其实现。
