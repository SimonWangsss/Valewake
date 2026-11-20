# 从 v1.0 升级到 v1.1

1. 关闭游戏、SMAPI 和旧后端，备份游戏存档。
2. 将旧 Mods/Valewake 整个移到 Mods 之外，保留作备份。
3. 解压新包到 Mods/Valewake，先不启动游戏。
4. 双击新包内 Upgrade-Valewake.cmd，选择旧备份中的 Valewake 文件夹，等到 Migration completed。选择其 data 目录或只包含一份 Valewake 的外层解压目录也可自动识别。运行时先核对 OLD source / NEW destination，避免把方向弄反。
5. 通过 SMAPI 启动游戏，核对模型设置和旧存档中的记忆。

脚本复制 data/memory（同时保留当前文件和 committed 检查点）、data/traces（历史审计日志）、玩家自建 custom_npcs（不迁移 _template）。只迁移 config.json 的 LlmProvider、LlmApiKey、LlmModel、LlmApiBase。

**不会迁移 RAG、内置人格、meeting_spots、DLL、Backend 程序、旧路径、测试开关或 .env。不要把旧 data 整个覆盖到新包。** 若只在旧 Backend/.env 或 llm_config.json 配了 Key，请重新在新配置或游戏设置里填写。

旧 Backend/data 的 Memory/Trace 也可识别；若它和 data 下都有记录，脚本停止，需人工确认哪份有效。新版已经游玩并产生记录时也可能拒绝迁移，避免混合不同时间线；应重新解压一个干净新包后迁移。

只备份了单独的 data 文件夹时，也支持迁移其中的 memory/traces/custom_npcs，但不能恢复 API 配置。外层目录包含多份 Valewake 时会要求精确选择，不自动挑选。找不到安装时会给出目录提示，不再直接报缺失 data/manifest.json。新版目标仍必须是完整解压的 Mod，不能只有数据文件。

旧目录不修改、不删除。新版 config 的迁移前备份和文件清单保存在 Mods 外的 ValewakeUpgradeBackups。备份及旧目录可能包含私聊和密钥，不要随发布包上传。如果中途失败，重新解压新包再试；本工具不提供完整事务回滚，也不会自动删除部分已复制文件。

喜欢手动操作的话，按相同范围复制目录并填写四个模型字段即可。不要只复制 player_memory.json 而丢掉 player_memory.committed.json；保存检查点用于未保存游戏的记忆回滚。Trace 不驱动 NPC 记忆，不会因为未保存退出而删除审计历史。

Windows 命令预览，不写入文件：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Upgrade-Valewake.ps1 -OldModPath "D:\Backups\Valewake-v1.0" -WhatIf
```

换电脑仍需单独转移原游戏存档；好感、部分日记和约见信息在游戏存档中。不要把重命名后的旧 Mod 留在 Mods 内，否则 SMAPI 仍可能加载两份。自定义过旧内置人格/RAG 的玩家请单独备份改动，再审核适配新版，而不是直接覆盖。

## English Quick Guide

Close the game and backend. Back up your save and move the old Valewake folder OUTSIDE Mods. Extract the new package into Mods/Valewake without starting the game. Double-click Upgrade-Valewake.cmd in the new folder and select the old backup folder.

Only personal Memory, Traces, user NPC packs and four model configuration fields are imported. New lore, built-in personas, meeting spots, binaries and runtime paths are preserved. Existing conflicting runtime data is not merged automatically. Legacy .env-only API settings need to be entered again. The old installation is never removed; backups may contain private data and keys, so do not share them.
