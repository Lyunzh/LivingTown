# PelicanNet 记忆体系：脱离 Vector DB 的本地化防渗漏架构

结合 OpenClaw (原 ClawdBot) 的架构理念，我们将为《星露谷物语》模组建立一套**极度轻量化、避免使用任何本地向量数据库 (Vector DB)** 的反记忆遗忘机制。

OpenClaw 的核心理念是**“系统工程而非单纯的 Prompt 工程”**。在不依赖外部重量级检索组件的前提下，以下是将这套逻辑应用在您的 Mod 中的具体实践方案：

## 1. 核心架构映射：轻量化的双层结构

| 逻辑组件 | 功能描述 | 星露谷 C# / SMAPI 实现 |
| :--- | :--- | :--- |
| **短期缓存 (Event Buffer)** | **交易日志：** 记录当天的临时动作，防止 LLM 在同一天内装失忆。 | **内存队列：** 定义一个 `List<GameEvent>`。玩家每天睡觉时清空，纯 RAM 存储。 |
| **长期记忆 (ModData Storage)** | **语义持久化：** 经过提炼的核心事实。**绝对不允许无脑塞原文本**。 | **字典存储：** 利用 SMAPI 的 `NPC.modData` 存储 JSON 格式的 KV 键值对（如 `"Memory_TrashGift": "Hates player for gifting trash on Spring 12"`）。 |
| **基础人设 (Persona Prompt)** | **灵魂/宪法：** 定义 Agent 的绝对性格与底线。 | **系统提示词：** 每次发给大模型的静态头部 (System Prompt)，享受大模型的 Context Caching 优惠。 |

---

## 2. 核心痛点解决：防内存溢出的滑动窗口

如果放任 NPC 每天睡觉时都在积累新记忆，到了游戏第三年，LLM 的上下文窗口必定会被撑爆。因此，必须引入**记忆衰减（Memory Decay）**与**截断策略**。

### 步骤 A：带有权重分数的记忆单元
每次通过 `DayEnding` 夜间总结让 LLM 凝练记忆时，要求 LLM 输出一个**重要度评分 (1-10)**，并带上时间戳：
```json
{
  "Timestamp": "Y1_Spring_15",
  "Fact": "The player missed my birthday.",
  "Importance": 7
}
```

### 步骤 B：组装查询时的“滑动窗口 (Sliding Window)”
当拼装发给 LLM 的 Prompt 时，执行以下防溢出排序：
1. **绝对核心 (Importance >= 9)**：如“玩家与我结婚”、“玩家与我离婚”。这一类打上 `[PERMANENT]` 标签，**永远不被清除，永远强制注入 Prompt**。
2. **常规记忆 (Importance 4-8)**：根据时间错配算法 $ Score = Importance - (CurrentDay - MemoryDay) \times 0.1 $ 重新计算分数。如果分数跌破阈值，视为自然遗忘，从 `ModData` 中默默擦除。
3. **容量截断**：即使按上述公式排序后，只取 Top N 条（例如最多取 10 条事件）注入当前 Prompt，从物理层面掐断 Token 溢出的可能。

---

## 3. 具体应用实践步骤

### 第一步：短期记忆流的触发器采集
不要每一帧都记录，利用 C# 的 `EventWatcher` 只记录**关键触发器 (Triggers)**。
- 玩家送礼。
- 玩家触发了特定的过场动画。
- 玩家闯入了卧室。

### 第二步：“预压缩刷新” (The Pre-Compaction Flush)
游戏触发 `DayEnding` (玩家睡觉黑屏) 时，开始**内存结账**。
- **提取**：将当天的 `List<GameEvent>` 序列化后打包发给 LLM。
- **归纳**：Prompt：“总结今天发生的事，如果无事发生就返回空数组；如果有值得记住的，以 JSON 格式返回事实和重要度(1-10)”。
- **写入**：反序列化 LLM 的 JSON，追加到 `NPC.modData["AgentMemories"]` 字典中。
- **清空**：清空 C# 内存里的 `List<GameEvent>`，迎接新的一天。

### 第三步：基于标签与上下文的纯文本检索 (Lexical Retrieval)
抛弃复杂的 Vector Embedding 和语义检索。NPC 的大脑不应该是一座广袤的图书馆，而应该是一个贴满便签的记事本。
- **场景触发与标签匹配**：
  当玩家进入“皮埃尔商店”时，系统读取玩家身上的隐性状态（例如 `Inventory` 里携带了大量蓝莓），C# 代码使用 LINQ 扫一遍皮埃尔的近期记忆库，通过纯文本匹配（`.Contains("Blueberry")` 或 `.Contains("PriceDrop")`）直接提取这几条特定记忆发给提示词组装器。
- **优势**：没有大模型调用延迟，不需要第三方数据库进程。检索成本为零。

**一句话总结**：不要妄图通过 Vector DB 全盘模拟人的记忆宇宙；用“权重衰减 + 每日结算 + C#字符串匹配”的**便签簿模型**，才是算力与智力的最优解。