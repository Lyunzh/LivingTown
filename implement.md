# 总体开发进度索引 (Development Index)

- **阶段一：初始化与基础架构** (已完成) - 探索项目结构，建立基础 ModEntry 与 API 客户端。
- **阶段二：LLM Multi-Agent 模块设计与开发** (进行中) - 设计基于 ReACT 架构的多智能体系统，支持 `mode` 模板、并行工具与 `new_task` 子智能体派生。
- **阶段三：NPC 逻辑与记忆模块接入** (待办) - 接入真·GOAP调度，结合具备滑动窗口的便签薄式记忆进行动态交互。
- **阶段四：PelicanPhone (鹈鹕通) UI 渲染与社交演化** (待办) - 利用渲染事件与本地预加载，展示如 TownFeed 等 AI 社交网络。
- **文档重构：去伪存真与脱水降本** (已完成) - 移除了高大上但不切实际的 VectorDB 幻想，植入了“看门狗打分机制”、“状态叠加器 Tracker”与真实的 GOAP 动作架构（涵盖 `base.md`, `Network.md`, `GOAP.md`, `Agent_Memory_Architecture.md`）。

***

# 问答记录与解决方案 (Q&A and Solutions)

## Q4: 从“纸上谈兵”到工程落地：全面剔除大容量中间层 (Engineering Pragmatism)
**解决的问题：**
在上一版架构文档中，系统充斥着依赖大模型测算“信息熵”的自我矛盾，并且可笑地试图在一个基于 .NET / MonoGame 的游戏客户端内去跑 Vector Embeddings（向量嵌入计算）以及加载类似 ChromaDB 的重型向量库。这不仅会导致极差的性能问题，更背离了轻量模组的设计初衷。此外，GOAP 和多日程追踪依然停留在空壳阶段。

**怎么解决的（老资格架构师的毒打纠正）：**
- **剔除信息熵算力悖论，引入“启发式看门狗” (`base.md` 更新)**：
  废弃了用 AI 去判断需不需要 AI 的智障循环。改在 C# 层手写一个极低性能开销的 `Interaction Weight Ledger` (打分簿)。常规操作加 1-5 分，极端操作加 50 分。当分值捅穿了阈值，立刻把缓存池发给大模型。这叫硬核的阈值判定。
- **枪毙 VectorDB 及客户端 Embedding 计算 (`Agent_Memory_Architecture.md` 更新)**：
  彻底把所有关于“语义相似度计算”的重构剥离客户端。转而采用：
  1. 基于便签薄（Tag Matching）和字符串 LINQ 的正则匹配实现短路缓存。
  2. 对于长线记忆，增加**带有重要度和衰减因子的滑动窗口**，利用算术减法自动剔除冗余记忆，保护 Context Window 不溢出。
- **补充填实 GOAP 核心设计 (`GOAP.md` 更新)**：
  终于像个真正的 AI 架构文档了。补齐了基于状态机的 Blackboard（黑板），定下了 `Preconditions`, `Effects` 以及基于空间距离或耗时的 `Heuristics (Cost)` 原子化框架。LLM 现在被剥夺了胡乱输出坐标的权力，仅仅以老板的身份下达“我要去消除饥饿状态”的目标，老黄牛 C# 代码会自己算出 `Action_GoToSaloon` 的路径。
- **增加数据持久化底座 (`Network.md` 更新)**：
  增设了关键的 `GameStateTracker` 中间件，死死抱住 `DayEnding` 的大腿，记录“连卖 3 天蓝莓”这种复合事件并打入专有 JSON 标签，从此那些“舔狗惩罚”才真正获得了运行基座。

---

## Q3: 基于信息熵的高级架构与逻辑审查 (`base.md` 逻辑批判记录)
（*摘要：已在 Q4 阶段彻底解决该版本中提到的“算力悖论”和“错误估计端侧环境”等逻辑硬伤。*）

---

## Q2: 文档格式优化与逻辑梳理 (Docs Formatting 初次梳理)
（*摘要：发现了由于原作者直接用 Word 复制而导致的排版崩溃与大段缺失，首次指出了 GOAP 及长期记忆管理的隐患。*）

---

## Q1: 如何设计符合项目需求的多智能体 LLM 模块？
**解决的问题：**
优先构建底层的 LLM Agent 模块。需要支持多智能体 (Multi-Agent)、`mode` 前缀系统提示词、并行工具执行，以及用 `new_task` 派生 SubAgent，整体采用 ReACT 架构。

**怎么解决的：**
- **核心组件设计**：
  1. **PromptManager (Mode 模板适配器)**
  2. **ReACT 循环器 (ReACT Executor)**
  3. **异步并发工具中心 (Tool 并发器)**：利用 .NET 的 `Task.WhenAll` 实现异步并行执行。
  4. **SubAgent 派生器 (`new_task` 工具)**：内部实例递归化。
