# 总体开发进度索引 (Development Index)

- **阶段一：初始化与基础架构** (已完成) - 探索项目结构，建立基础 ModEntry 与 API 客户端。
- **阶段二：混合架构底层搭建 (State & Watchdog)** (已完成) - 构建 `GameStateTracker`, `HeuristicWatchdog`, `LexicalCache`, `SoulLoader`, `MemoryManager`。(`src/state/` 下 5 个文件)
- **阶段三：GOAP 执行系统与记忆管线** (待办) - 建立 `Blackboard`, `GOAPAction` 动作库。
- **阶段四：ReACT Multi-Agent 模块实现** (已完成) - (`src/llm/core/` 下 6 个文件)
- **阶段五：ModEntry 闭环重写** (已完成) - 替换旧版 Pipeline，实现 Watchdog → LexicalCache/ReActAgent → 主线程队列显示。
- **阶段六：PelicanPhone 表现层** (待办) - TownFeed 的异步离线预渲染展现。

***

# 问答记录与解决方案 (Q&A and Solutions)

## Q1: 如何设计符合项目需求的多智能体 LLM 模块？
*(详见旧版纪要，聚焦 ReACT 并发工具与 SubAgent 设计)*

## Q2: 如何重塑 LLM 模块与游戏本体的交互，避免成本与延迟失控（The Impossible Triangle）？
**解决的问题：**
全新的架构文档指出，直接让 LLM 操作寻路或全盘响应 NPC 交互会导致高昂成本与逻辑幻觉。要求将多智能体架构建立在混合层之上。需要处理看门狗鉴权、GOAP 执行、每日记忆归纳等。

**怎么解决的：**
应用混合神经符号架构 (Hybrid Neuro-Symbolic Architecture)，按照金字塔原理拆分解耦：
- **纯 C# 技术栈**：确认废弃此前使用 Python Backend 中转的杂念，将这套高扩展性的 ReACT Multi-Agent 完全原生集成在 C# Mod 逻辑内。
- **前置拦截（底层保安）**：一切交互先过 `HeuristicWatchdog`。系统硬编码不同事件的权重。当 `DailyEntropyPool` (今日熵池) 小于 30 时，系统短路，绝不调用 LLM。配合 `LexicalCache` (正则速回) 实现零延迟常规寒暄。只有当池子爆满产生“高熵态”时，才将事件交给深度的 LLM 智能体。
- **记忆管理（减负优化）**：丢弃本地 VectorDB 幻想。引入 `Event Buffer`，每天睡觉触发 `DayEnding` 后将当天行为总结出 `Importance` 评分，采用滑动窗口衰减并存入 `ModData` 进行纯字符串标签化检索。
- **业务决策层（大模型专攻方向）**：此时呼起我们的 ReACT 多智能体模块。这套模块拥有 `Planner`、`Critic` 等多个 `mode` 模板，支持 `new_task` 派生子智能体分析庞杂的经济和社交日志。
- **终端执行（GOAP苦力）**：LLM 计算完毕后，不再直接输出坐标路线。它只吐出一句诸如 `{"Goal": "IsHungry", "TargetValue": false}` 的 JSON。而我们在 C# 端构建的 GOAPPlanner 将基于 `Blackboard` 的状态，通过 A* 算法逆向拼装出一条包含 `Action` 组合的执行图谱，完美规避幻觉穿模。

由此实现了极低延迟成本下的拟真涌现性网络。

## Q3: 为什么我们需要先建立通用的 Multi-Agent 基座？NPC 初始化与 Agent 的关系是什么？
**用户构想：** 将所有 NPC 初始化为挂载 Agent 的 Client，并利用通用 Multi-Agent 的 WebSearch 工具来离线生成人设(soul.md)，同时在运行时复用于记忆压缩等功能。
**架构结论：** 这不是过度设计，而是优秀的“自我闭环 (Dogfooding)”。Agent 不应等同于具体 NPC，而是**通用的算力执行容器**。根据加载的 `mode`（如 PersonaBuilder, NpcActor, MemoryCompactor）不同，该引擎不仅能在建构期自动完成人设构建，还能在运行期作为统一的 ReACT 推理中枢，极大降低模块间的耦合度与维护成本。

## Q4: 关于 WebSearch 破壁体验与记忆压缩并发灾难的最终设计
**面对的问题：** 
1. 玩家期望使用 PersonaBuilder (带 WebSearch) 来初建 NPC 档案，甚至兼容其他外部 Mod 新人物。
2. 玩家期望通过 WebSearch 实现打破第四面墙的惊喜。
3. 30 个 NPC 同时晚间执行 Memory Compact 会触发 API 的 429 速率限制死锁。

**最终纠偏设计：**
- **PersonaBuilder 定位（解决过度设计）**：将 PersonaBuilder 作为**独立于游戏主循环外的一个辅助脚本/工具窗口**。如果有新 Mod 人物加入，玩家或作者点击一个按钮，利用通用 Agent 爬取 wiki 生成 `soul.md`。这个动作只发生一次，生成后就作为静态资产喂给游戏，绝对不与游戏的启动加载流程 (Loading Phase) 强绑定。这就保留了通用 Agent 的“闭环快感”，也砍掉了运行期的冗余风险。
- **异步 WebSearch（破壁体验）**：绝对禁止在玩家同步对话（交互）时发起 WebSearch。当白天识别到需要破壁的话题时，暂时使用“不懂但感兴趣”的话术兜底。此任务进入夜间 SubAgent 队列，第二天 NPC 获取新知识后，通过 `PelicanPhone (Whisper)` 主动给玩家发消息。既保全了延迟底线，又将破壁感推向高潮。
- **惰性压缩 + 物理截断 (Lazy Compaction & Hard Truncation)**：不强制每晚 Compact。只有当 `Watchdog` 熵池满载，或流水账积累条目超过物理阈值（如 `GameEvent.Count > 10`）时，才触发该 NPC 的压缩任务。
- **全局令牌桶限流 (SemaphoreSlim)**：触发 Compact 的 NPC 任务会被扔进全局 `Task.WhenAll` 队列中，利用 `SemaphoreSlim(initialCount: 2)` 严格控制最大并发对外 HTTP 请求数为 2。若请求超时崩溃，自动降级延后处理，绝不阻塞游戏进程。
## Q5: 纵观全局，旧版 Pipeline (Channels) 架构与新版混合架构是否存在致命冲突（过度设计）？
**深层漏洞剖析（对以前设计的反思）：**
在此前搭建阶段（参考 `AGENTS.md` 与 `Messages.cs`），我们引入了受 Go 语言启发的**Channel-based Pipeline Architecture (Channel+Task.WhenAny)**，让 Game、Npc、LLM 三个端点（Endpoint）像微服务一样通过不可变消息来回抛接球。

**当前面临的架构撕裂：**
1. **过度设计的中间商赚差价**：
   在纯对话型 Mod 中，Pipeline 模式很优雅。但现在我们引入了 **Watchdog 看门狗**、**Lexical Cache 短路缓存** 和 **GOAP 执行层**。如果在原有的由 NpcMsg/GameMsg 统治的强类型管道里硬塞 Watchdog，会导致每一条 `GameMsg.TimeChange` 都要进管道、序列化一遍发给 NpcAgent、然后 NpcAgent 在管道里算一遍熵值，这是**荒谬的内存浪费**。看门狗必须是直接贴在 SMAPI 事件树上的 C# 静态监听器，而不是管道里的一个节点。
2. **Streaming（流式）与 ReACT（多次迭代）的不可调和性**：
   你的 `Messages.cs` 里洋洋洒洒写了 `StreamingResponse`。但我们在 Q2 明确了，对于复杂业务我们要用 **ReACT Multi-Agent**（包含 Thought, Action 等不可见的中间步骤）。把一个包含多步递归思考的 ReACT 引擎直接接到一个要求 `IAsyncEnumerable<string>` 吐 Token 的 Pipeline 上，相当于把一个正在沉思解题的博士的每一声叹气都通过扩音器（UI）播给玩家听。
3. **Session 隔离的假象**：
   原有架构强调“Per-NPC conversation context (Guid)”，但星露谷的游戏状态是**全局的 (Global GameStateTracker)**。皮埃尔降价不需要他自己有一个独立的 Session 去推理，而是需要在全局 `DayEnding` 时做聚合盘点。

**重构决断（削足适履不如刮骨疗毒）：**
- **降级 Pipeline**：放弃将整个 Mod 业务塞进 Channel 管道的幻想。Pipeline 设计仅保留于【被看门狗放行后的纯文本交互阶段】。
- **确立分层边界**：
  - **L0 (SMAPI 宿主层)**：`ModEntry` 和 `GameStateTracker`，直接操作游戏内存，计算熵值。
  - **L1 (短路控制层)**：`HeuristicWatchdog` 和 `GOAP 引擎`，**它们是驻留在内存中的全局单例 (Singleton)**，纯 C# 同步代码。对于低熵值事件，看门狗只做一次 `Dictionary[NPC] += Weight` 的算术题，耗时 0.01ms，立刻 return 游戏。**绝对不会去 `new` 任何 Agent 对象！**
  - **L2 (离线异步层)**：基于 `SemaphoreSlim` 的并发队列，专跑 Nightly Memory Compact 和 WebSearch。
  - **L3 (重型对话层)**：也就是你心心念念的 `ReACT Multi-Agent`。它不作为长连接守护进程活着，而是作为一个**随时被 L1 或 L2 new 出来使用的临时算力容器 (Scoped Service)**。只有当看门狗发现熵池满了（>30），才会 `new` 一个有血有肉的多智能体去推导剧情。

保留通用多智能体底座（解算复杂任务），但必须**拔掉遍布整个游戏底层的那些花里胡哨的异步通讯管道**。游戏框架以同步回调（Event Hook）为尊，越朴素越健壮。

## Q6: 如果没有微服务式的 Pipeline 管线，如何实时响应事件并并行执行 GOAP 等操作？

**核心认知错位：游戏引擎循环 (Game Loop) vs 异步微服务 (Microservices)**

你的疑问来源于把“后端高并发服务器”的设计硬搬到了“单机游戏客户端”里。

**1. 星露谷是单主线程环境（The Main Thread Dictatorship）**
SMAPI 提供给你的所有事件（点击 NPC、时间改变、送礼）都是在**游戏的同一根主线程（Main Thread）**上按帧抛出的。任何企图修改游戏状态（比如让 NPC 转身、播放 emote、显示对话框）的操作，**绝对、不能**离开这根主线程。
如果你用 Pipeline 开后台协程去处理，最后还是要通过 `helper.Events.GameLoop.UpdateTicked` 写个并发队列（像你在 `AGENTS.md` 里写的 `_pendingActions.TryDequeue`）把结果生拉硬拽回主线程执行。这就叫“兜圈子”。

**2. 实时响应不等于“独立协程 Pipeline”**
怎么做到实时？**依靠事件驱动 (Event-Driven) 与分帧计算。**
当送礼事件发生时，主线程立刻执行 `Watchdog` 的静态方法算分。
如果分数没爆，直接返回，主线程接着渲染下一帧。
如果分数爆了，看门狗在主线程丢出一个 `Task.Run( () => { 启动大模型推理(); } )`。注意，大模型 API 等待那 5 秒钟是在**后台工作线程（ThreadPool）**发生的，绝不阻塞游戏主线程。游戏照常运行，玩家可以照常走动。

**3. GOAP 是怎么“并行”执行的？**
NPC 的行走和行为在游戏里本身就是依靠每秒 60 次的 `UpdateTicked` 帧刷新来推进的，它本来就是伪并行的。
当后台的那个大模型算完了（给了个 Goal：我要去酒馆买沙拉）。它把这个结果塞进游戏内存主线程的队列里。
下一帧到来时，主线程的 GOAP 引擎瞬间（0.01ms）算出了去酒馆的寻路路径，并把 `SchedulePathDescription` 注射给了 NPC 自己。
接下来，游戏引擎自己会在每一帧的循环里带动 NPC 往前走。你根本不需要自己写个“并发管道”去推着 NPC 走。

**总结：**
摒弃 Pipeline 绝不意味着你的程序变成“卡加载”的单线程废物。恰恰相反，是用 C# 原生的异步编程（`async/await` 和后台 `Task`）加上 SMAPI 天然的 **60FPS 主线程心跳 (Tick)**，来替代那套花里胡哨且容易引发时序竞态的“通道抛接球（Channel/WhenAny）”。

**最高级的游戏 AI 架构：平时像死人一样静默（零开销），需要时用异步 Task 在后台闷声算，算完用 Tick 队列把结果射回主线程。**

## Q7: 如何利用 PersonaBuilder 结合萌娘百科/维基获取 NPC 的 soul 定制数据？

**用户构想：** 想参考 `https://zh.moegirl.org.cn/星露谷物语:塞巴斯蒂安` 等外部 Wiki 网站，让 Multi-Agent 挂载 `PersonaBuilder` 模式自动抓取并生成 NPC 的 `soul.md`。

**架构结论（强烈赞同的工程实践）：**
这是非常实用且完全正统的 RAG（检索增强生成）数据清洗管线（Data Ingestion Pipeline）。不仅可行，而且正是发挥你在 Q3 构想的“自我闭环”威力的绝佳场景。

**具体落地流转（离线数据清洗环节）：**
1. **触发时机**：它不属于 `LivingTown` 在游戏内的运行代码，而应该是一个我们在控制台（或开发调试热键下）写好的独立脚本入口：`RunPersonaBuilder(string npcName, string wikiUrl)`。
2. **WebSearch Tool 的精确斩首**：
   挂载 `PersonaBuilder` 模式的 Agent 获取到这个 URL 后，调用你为他写的 `WebScraperTool`（底层可以使用 `HtmlAgilityPack` 抓取正文，或者直接调第三方 API 转 Markdown）。
3. **清洗与规范化输出 (JSONL/MD)**：
   Prompt 中强制规定大模型必须将抓取到的萌娘百科中的冗长描述，提取为 OpenClaw 标准格式的 `soul.json` 或 `soul.md`，例如：
   ```json
   {
     "Name": "Sebastian",
     "CoreTraits": ["Rebellious", "Introverted", "Loves Technology"],
     "Relationships": {"Robin": "Mother, feels ignored", "Maru": "Half-sister, mildly jealous"},
     "ScheduleAnchors": ["Basement", "Lake smoking"]
   }
   ```
4. **资产固化**：
   这个文件直接保存在 Mod 目录下的 `assets/souls/Sebastian.json` 里。
   等到玩家真正进游戏启动时，游戏直接把这个静态 JSON 读进内存，无须再联网。

**结论**：用你写的通用 Agent 去爬萌娘百科/星露谷 Wiki 洗数据，并且**一次生成、永久静态化使用**，这是最高效、最符合 Senior 开发者直觉的“数据喂养”方案。这套工具链一成型，未来就算别的作者做了新人物的 Mod，丢个 Wiki 链接给他，一键就能生成匹配的 AI 大脑。完美。

## Q8: Agent 的 Tool 和 GOAP 是什么关系？怎么产生联动？

**核心统一认知：**
Agent 决定 **WHAT**（做什么），GOAP 决定 **HOW**（怎么做）。它们之间的桥梁是 **Tool**。

**具体流转：**
1. **看门狗判定**：玩家连续送了 3 份讨厌的礼物给 Sebastian，熵值爆表（>30）
2. **ReACT Agent 启动**：看门狗唤起 NpcChat 模式的 Agent
3. **Agent 思考**：LLM 看到 Soul + Memories 后认为："Sebastian 应该很不高兴，他应该走开去山上独处"
4. **Agent 调用 Tool**：`set_goal(goal_name="VisitLocation=Mountain", priority="high", reason="angry at player")`
5. **Tool 将 Goal 写入 GOAP Blackboard**
6. **GOAP Planner 读取 Blackboard**：用 A* 反向搜索出 Action 序列：`StopConversation → WalkTo(Mountain) → StandBy(LakeSmokingSpot)`
7. **游戏引擎执行**：每帧 Tick 推动 NPC 走路

**已实现的 Game Tools（`src/llm/core/GameTools.cs`）：**
- `set_goal` → GOAP 桥梁（高层目标 → 黑板）
- `play_emote` → 直接执行表情动画（不经 GOAP）
- `remember` → 将重要信息写入长期记忆
- `web_search` → 仅用于 PersonaBuilder 和夜间批处理，不在对话时启用

