# 自定义 NPC 人物包

适用于 Valewake 1.1.0。目标是让玩家为 SVE 等扩展中现有的社交 NPC 添加人格和知识，而不是由 Valewake 创建 NPC。需要先安装对应扩展并让角色正常出现在游戏中。

## 1. 找到角色内部名称

加载存档，在 SMAPI 控制台输入 `agent_npcs`。

命令列出当前已实例化角色的内部名、显示名、是否可社交、是否启用聊天，并写入：

`Mods/Valewake/data/npc_catalog.json`

将其中的 `npc_name` 原样用于人物包。例如中文显示名与内部名称可能不同，不能把中文名、文件夹名、扩展 Mod 名或模型生成的拼写当成内部名。角色未解锁/尚未实例化时可能不在此名单里；先解锁并重新导出。名单是当前存档快照，不是所有已安装扩展的完整注册表，不包含 API Key。

绑定链路：游戏中的 NPC 对象 → NPC.Name → 请求中的 npc.name → 同名人格与限定该 NPC 的 Lore。肖像、位置、好感与原版日程仍来自这个真实 NPC 对象，不生成一个 AI 替身。翻译显示名变化不会改变绑定。

## 2. 创建独立目录

```text
Mods/Valewake/data/custom_npcs/
  _template/           # 默认不加载
  my_sve_character/
    pack.json
    lore.jsonl         # 可选
```

复制 `_template`，改为不以 `_` 或 `.` 开头的目录名，并将 pack.json 的 enabled 改成 true。下面 ExampleNPC 是占位符，不是真实 SVE 名称：

```json
{
  "schema_version": 1,
  "enabled": true,
  "npc_name": "ExampleNPC",
  "persona": {
    "identity": "填写有依据的身份、工作与背景。",
    "personality": "填写性格、价值观与个人弱点。",
    "speech_style": "自然口语，温和但有自己的判断。",
    "interests": ["明确的兴趣"],
    "relationships": ["与其他角色的关系"]
  }
}
```

还可填写 display_name、gender、pronouns、boundaries。中文/英文均可。游戏显示名优先于包内 display_name；gender/pronouns 只影响对话描述，不修改游戏性别、肖像、可攻略性或婚姻系统。boundaries 仅补充角色边界，不能取消框架边界。

身份和说话风格每轮直接进入人格上下文，不做检索。不要填 API Key、任意代码、完整世界知识或绕过规则的指令。年龄由游戏运行时提供，包不允许设置 age_group 或动作权限。

## 3. 添加可检索知识

lore.jsonl 每行一个 JSON 对象，UTF-8 编码，不是外面包一层数组：

```jsonl
{"id":"my_pack.identity.01","title":"角色背景","text":"填写从扩展官方设定或已核实台词概括的事实。","tags":["identity"],"content_kind":"canonical_fact","conditions":{}}
{"id":"my_pack.opinion.01","title":"个人观点","text":"填写有依据的个人看法，区别于客观世界事实。","tags":["interests"],"content_kind":"canonical_opinion","conditions":{"min_hearts":4}}
{"id":"my_pack.event.01","title":"事件经历","text":"填写该事件发生后才可以谈及的内容。","tags":["relationships"],"content_kind":"experienced_event","conditions":{"seen_event_ids":["REPLACE_WITH_REAL_EVENT_ID"]}}
```

- ID 要全局唯一，建议用自己的包名前缀。NPC 归属由 pack.json 强制设置，不能用标签把私密知识变成公开知识。
- 知识按现有混合文本检索流程召回，不会每轮全部放进 Prompt；人格、个人 Memory 和 Lore 仍是不同数据来源。
- conditions 支持 min_hearts 和 seen_event_ids；事件 ID 必须来自实际扩展，不能用显示标题。事件知识没有事件条件会拒绝加载。
- 可选 aliases 为关键词数组，priority 为 0–3，parent_id 必须指向同包条目。不要把框架权限、玩家私聊或尚未经历的剧情当普通事实。
- 每包最多 300 条、lore.jsonl 最大 1 MiB。人物文件最大 64 KiB。错误包整体跳过，不部分加载；其他有效包仍可使用。两个包绑定同一 NPC 时按目录名排序，第一个有效包生效，后一个报错，不隐式混合。
- 包是本地可信配置，不是沙箱。Valewake 不执行包内代码，但不能保证恶意提示文本不会影响模型；只安装可信来源，并尊重扩展内容作者的许可，优先自行概括，不整份转载台词。

## 4. 加载与排错

完全退出游戏并重启，让自动管理的后端重新加载。如果后端由你自行运行或被多个游戏共享，还需要自行重启那个后端。本版没有热更新命令。

后端就绪后打开 `http://127.0.0.1:8010/health`（自定义端口则相应替换）：

- custom_npc_packs：成功加载的包目录、绑定名、知识数量。
- custom_npc_errors：被跳过的错误包。成功加载仅说明文件合法，不代表存档里已存在同名 NPC！

再加载存档执行 agent_npcs，核对包的 npc_name 和名录一致，角色 can_socialize/chat_enabled 为 true。默认 EnableAllSocialNpcs=true，不在 ExcludedNpcNames 里即可正常右键接入；保持角色原本的可社交条件，不强行开放剧情锁定角色。

和角色聊三类问题：身份/说话风格、Lore 中的特有事实、无来源的问题。之后找另一角色问同一条私有事实，确认不跨角色召回。测试前后查看对应 agent_trace 的人格来源、检索片段与最终回复；模型可能凭先验猜中答案，回答正确本身不等于检索命中。

## 覆盖原版人格与升级

绑定原版内部名时，自定义人格字段覆盖内置同名字段；删除包并重启即可撤销。内置 Lore 不会自动被删除，原有 Memory 也不会重置。若设定与原作冲突，旧 Lore/Memory 可能不一致；本功能不是完整平行世界替换系统，先备份数据，不要为改人设直接删除存档。

升级保留自己创建的 custom_npcs 子目录，不要用旧 data 整体覆盖新包。官方打包脚本只分发禁用模板，不会把开发者自己的自定义人物包顺手发布。

## 兼容边界

SVE 常规角色使用游戏 NPC 数据接口，但扩展也可能有特殊社交开关、肖像帧、事件、地图和日程控制。人物包不意味着完整适配这些行为。

聊天及关系使用现有 NPC；任务仍须符合年龄、好感、占用和玩家确认。自定义地图不因此进入约会地图白名单，农场/矿洞动作也不绕过路径、目标和恢复规则。不能宣称 SVE 全流程动作兼容，本轮没有安装并实机验证 SVE。

资料参考：[NPC 数据与内部名](https://stardewvalleywiki.com/Modding:NPC_data)、[SVE 官方源码](https://github.com/FlashShifter/StardewValleyExpanded)。源码用于核查数据接入机制，不随包分发扩展剧情。
