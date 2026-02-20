# SMAPI 技巧索引

本目录包含基于官方 SMAPI 文档整理的使用技巧指南。

## 文档列表

### 1. [smapi-basics.md](./smapi-basics.md) - SMAPI 基础技巧
- 项目设置要点
- Mod 入口类结构
- manifest.json 配置
- 游戏状态检查
- 日志记录
- 跨平台兼容性
- 常用陷阱和注意事项

### 2. [smapi-events.md](./smapi-events.md) - 事件系统详解
- 事件基础用法
- 游戏循环事件（生命周期、每日、存档）
- 输入事件（按钮、光标、滚轮）
- 显示事件（菜单、渲染）
- 内容事件（资源加载）
- 世界事件（NPC、对象变化）
- 玩家事件
- 多人游戏事件
- 高级技巧（变化监测、优先级、防抖）

### 3. [smapi-npc-dialogue.md](./smapi-npc-dialogue.md) - NPC 与对话系统
- NPC 基础操作
- 显示对话（气泡、聊天框）
- 创建自定义对话菜单
- NPC 动作（表情、移动、动画）
- AI 集成对话系统
- NPC 数据访问
- 会话管理和错误恢复

### 4. [smapi-network-multiplayer.md](./smapi-network-multiplayer.md) - 多人游戏与网络通信
- 多人游戏状态检查
- 模组消息传递
- 分屏支持 (PerScreen)
- NPC 状态同步
- 社交图谱传播
- 网络优化和调试

### 5. [smapi-phone-mail-system.md](./smapi-phone-mail-system.md) - 手机与邮件系统
- 程序化邮件发送
- 动态邮件内容
- 来电系统
- 自定义手机UI框架
- TownFeed应用实现
- 条件性内容触发

### 6. [smapi-reflection-advanced.md](./smapi-reflection-advanced.md) - 反射与高级技巧
- 反射API基础
- NPC日程操作
- 高级事件处理
- 线程安全模式
- 性能优化
- 调试和监控

### 7. [openclaw-memory-system.md](./openclaw-memory-system.md) - OpenClaw 记忆系统架构
- 双层记忆架构（Daily Notes + MEMORY.md）
- SOUL.md 人格定义系统
- 预压缩刷新机制
- BM25 + Vector 混合检索
- 压缩与提取算法
- 心跳与记忆新鲜度管理

## 快速参考

### 最常用的事件
```csharp
helper.Events.GameLoop.GameLaunched      // 游戏启动
helper.Events.GameLoop.SaveLoaded        // 存档加载
helper.Events.GameLoop.DayStarted        // 新的一天
helper.Events.GameLoop.DayEnding         // 一天结束
helper.Events.Input.ButtonPressed        // 按键按下
helper.Events.Display.MenuChanged        // 菜单变化
helper.Events.World.NpcListChanged       // NPC变化
```

### 最重要的检查
```csharp
if (!Context.IsWorldReady) return;      // 世界是否就绪
if (!Context.IsPlayerFree) return;      // 玩家是否可操作
if (npc == null) return;                 // NPC是否存在
```

### 官方资源
- [SMAPI 官方网站](https://smapi.io/)
- [Stardew Valley Wiki - Modding](https://stardewvalleywiki.com/Modding:Index)
- [SMAPI API 参考](https://stardewvalleywiki.com/Modding:Modder_Guide/APIs)
- [事件系统参考](https://stardewvalleywiki.com/Modding:Modder_Guide/APIs/Events)
- [游戏基础](https://stardewvalleywiki.com/Modding:Modder_Guide/Game_Fundamentals)

### 社区资源
- [Stardew Valley Discord](https://discord.gg/stardewvalley) - #making-mods
- [SMAPI Mod 兼容性列表](https://smapi.io/mods)
- [SMAPI 日志解析器](https://smapi.io/log)
- [JSON 验证器](https://smapi.io/json)

## 使用建议

1. **新手入门**：从 `smapi-basics.md` 开始，了解基础概念
2. **功能开发**：查看 `smapi-events.md` 选择合适的事件
3. **NPC 集成**：参考 `smapi-npc-dialogue.md` 实现对话系统
4. **多人游戏**：使用 `smapi-network-multiplayer.md` 实现联机功能
5. **手机邮件**：参考 `smapi-phone-mail-system.md` 创建UI系统
6. **高级功能**：查阅 `smapi-reflection-advanced.md` 了解反射和优化技巧
7. **记忆系统**：参考 `openclaw-memory-system.md` 设计NPC长期记忆架构
8. **遇到问题**：检查各文档的"常见陷阱"和"最佳实践"部分

---

*最后更新：2026年2月*
*基于 SMAPI 4.0+ 和 Stardew Valley 1.6 版本*
