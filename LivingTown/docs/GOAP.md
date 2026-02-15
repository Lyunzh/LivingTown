星露谷物语（Stardew Valley）动态逻辑注入与高级模组架构研究报告：从SMAPI调度到生成式AI代理的深度解析
1. 引言：星露谷模组生态的技术演进
《星露谷物语》（Stardew Valley）的模组开发生态，在过去数年间经历了从静态资源替换（XNB Modding）到基于即时编译（JIT）的动态逻辑注入的深刻技术变革。作为构建在Microsoft XNA/MonoGame框架之上的开放世界模拟游戏，其底层的单线程游戏循环（Game Loop）机制为开发者提供了独特的挑战与机遇。随着Stardew Modding API (SMAPI) 的迭代，特别是针对游戏1.6版本的重大架构更新，开发者现在拥有了前所未有的能力来操控游戏状态（Game State）、重写非玩家角色（NPC）行为树、以及通过C#反射机制（Reflection）深度介入游戏的核心逻辑。
本报告旨在提供一份详尽的技术分析，重点探讨如何利用C#和SMAPI实现NPC日程的动态注入（Dynamic Schedule Injection）、构建复杂的社交与邮件系统、操控环境状态（如天气与运气），并进一步分析如何将外部的高级框架——如自定义移动电话UI系统及基于大语言模型（LLM）的生成式AI代理（Generative Agents）——集成到现有的游戏架构中。分析将基于现有的开源模组代码库、官方技术文档及社区研究成果，深入剖析其底层实现逻辑与架构模式。
2. NPC动态调度系统架构与C#实现路径
NPC的调度系统（Scheduling System）是驱动《星露谷物语》“生活感”的核心引擎。它不仅决定了角色的位置移动，还通过路径点（Waypoints）、朝向（Facing Direction）和动画状态（Animation State）共同构建了角色的行为叙事。
2.1 日程数据的底层结构与解析逻辑
在游戏内存中，NPC的日程并未以简单的坐标列表形式存在，而是被封装在一个更为复杂的数据结构中。NPC.Schedule 属性本质上是一个 Dictionary<int, SchedulePathDescription> 类型的数据集合 1。
键（Key）： int 类型，代表以24小时制表示的游戏内时间。例如，610 代表早上6:10，1300 代表下午1:00。这种非线性的整数表示法要求开发者在进行时间计算时必须使用专门的工具类（如 Utility.ModifyTime）而非简单的算术运算。
值（Value）： SchedulePathDescription 对象，它包含了从当前位置移动到目标位置的完整路径描述。
原始的日程数据通常存储在 Content/Characters/schedules/Name.xnb 文件中，以斜杠分隔的字符串形式存在。例如，Abigail的一个典型日程字符串可能如下所示： "900 SeedShop 39 5 0/1030 ScienceHouse 5 19 1/1430 SeedShop 3 6 0 abigail_videogames" 1。
该字符串被游戏的解析器（NPC.parseMasterSchedule 和 NPC.TryLoadSchedule）处理后，转化为具体的行为指令：
时间戳（Time）： 900（上午9:00）。
目标地图（Target Location）： SeedShop（皮埃尔杂货店）。
目标坐标（Target Tile）： 39 5（X, Y）。
最终朝向（Facing）： 0（向上）。
结束行为（End Behavior）： 可选字段，如 abigail_videogames，这对应着 Data/animationDescriptions 中的具体动画帧序列 3。
在1.6版本更新中，路径查找算法（Pathfinding）和地图加载逻辑并未发生根本性改变，但数据加载接口（Asset Loader）经历了重构，从 IAssetLoader 转向了基于事件的 AssetRequested 模式，这对动态注入提出了新的适配要求 4。
2.2 动态日程注入的三种架构模式
为了在运行时改变NPC的行为（例如，让NPC在特定触发器下偏离预定路线），开发者通常采用以下三种架构模式，每种模式在持久性与灵活性上各有优劣。
2.2.1 模式一：基于 AssetRequested 的即时补丁（JIT Patching）
这是SMAPI推荐的标准做法，具有最高的兼容性。通过监听 helper.Events.Content.AssetRequested 事件，模组可以在游戏引擎请求加载NPC日程数据的那一刻，拦截并修改数据流。这种方法的优势在于它不修改磁盘上的物理文件，且能与其他模组的修改（通过Content Patcher）进行合并。
C# 实现逻辑：

C#


public override void Entry(IModHelper helper)
{
    helper.Events.Content.AssetRequested += OnAssetRequested;
}

private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
{
    // 检查请求的资源是否为目标NPC的日程
    if (e.NameWithoutLocale.IsEquivalentTo("Characters/schedules/Abigail"))
    {
        e.Edit(asset =>
        {
            var editor = asset.AsDictionary<string, string>();
            
            // 根据当前游戏状态（如天气、季节、自定义事件）决定注入的键值
            string targetKey = "spring";
            if (Game1.isRaining) targetKey = "rain";
            
            // 动态构建日程字符串
            // 示例：6:10前往公交车站，12:00前往镇中心
            editor.Data[targetKey] = "610 BusStop 19 4 2/1200 Town 47 87 2/2000 BusStop 19 4 2";
        });
    }
}


在此模式下，数据的修改是“会话级”的。如果游戏内的条件（如天气）在一天中发生变化，或者模组需要立即应用新的日程，开发者必须调用 helper.GameContent.InvalidateCache("Characters/schedules/Abigail") 来强制游戏重新加载资源，并随后调用 NPC.checkSchedule() 来刷新NPC的当前路径 4。
2.2.2 模式二：基于 DayStarted 的易失性注入（Volatile Injection）
对于那些不需要持久化、仅针对当天的临时行为变更（例如，AI生成的随机漫步），直接操作 NPC 实例的 Schedule 属性更为高效。这种操作通常挂载在 GameLoop.DayStarted 事件上。
C# 实现逻辑：

C#


private void OnDayStarted(object sender, DayStartedEventArgs e)
{
    NPC npc = Game1.getCharacterFromName("Abigail");
    if (npc == null) return;

    try 
    {
        // 定义原始日程字符串
        string dynamicSchedule = "610 BusStop 19 4 2/700 Town 47 87 2";
        
        // 使用反射调用私有的解析方法，或者手动构建 SchedulePathDescription
        // 注意：parseMasterSchedule 通常是私有的，需通过 Helper.Reflection 访问
        var newSchedule = this.Helper.Reflection.GetMethod(npc, "parseMasterSchedule")
           .Invoke<Dictionary<int, SchedulePathDescription>>(dynamicSchedule);
            
        // 直接覆盖当前实例的日程
        npc.Schedule = newSchedule;
        npc.checkSchedule(Game1.timeOfDay);
    }
    catch (Exception ex)
    {
        this.Monitor.Log($"日程注入失败: {ex.Message}", LogLevel.Error);
    }
}


这种方法的风险在于它绕过了游戏的资源加载管道。如果其他模组（如Content Patcher包）试图在当天晚些时候更新日程，可能会引发冲突或覆盖。此外，直接操作内存对象在保存/加载周期中不会自动持久化，因此必须在每次 DayStarted 时重新执行 2。
2.2.3 模式三：GOTO 逻辑与条件分支模拟
原版游戏支持在日程中使用 GOTO 关键字来实现逻辑跳转（例如 "rain": "GOTO spring"）。在C#中模拟这一行为，需要开发者构建一个简单的状态机。如果模组逻辑检测到特定条件不满足，应动态将日程键值重定向到备用键。
例如，当检测到自定义天气“雾天”（Fog）时，模组可以先检查日程字典中是否存在 fog 键；如果不存在，则通过代码逻辑回退到 spring 键，模仿引擎内部的 GOTO 行为 7。
2.3 路径查找（Pathfinding）与坐标系统的技术挑战
动态日程注入的最大技术障碍在于坐标系统的有效性。游戏世界中的地图并非全是静态的，玩家放置的建筑物（如畜棚、鸡舍）是动态生成的障碍物。
静态地图 vs. 动态层： NPC的路径查找主要依赖地图的 Paths 层和 Back 层属性。如果目标坐标（Target Tile）被玩家放置的物体占据，原版A*算法可能会导致NPC在路径计算时卡死或原地踏步 3。
农场内的特殊处理： 正如资料 8 指出的，NPC在农场地图上的寻路极其复杂，因为农场布局是高度用户自定义的。为了让NPC能够走到玩家建造的畜棚附近，模组必须遍历 Game1.getFarm().buildings 列表，获取建筑物的 tileX 和 tileY，并计算出一个邻近的、无碰撞体积的坐标作为目标点。
坐标偏移修正： 为了防止NPC穿模，注入的坐标通常需要进行偏移（Offset）。例如，如果目标是坐标 (10, 10) 的建筑物，日程目标应设为 (10, 11) 或 (10, 12)，这需要编写几何计算辅助函数来实现 8。
2.4 动画状态机与 Data/animationDescriptions
日程字符串末尾的 abigail_videogames 并非简单的文本标签，它是指向 Data/animationDescriptions 的键。该数据文件定义了动画的帧序列（Frames）和持续时间。
数据格式示例：
"abigail_videogames": "16 17 18 19/300/16"
这意味着：循环播放帧16-19，每帧间隔300毫秒，结束时停在帧16。
如果C#模组注入了一个包含自定义动画键的日程，但未同步更新 Data/animationDescriptions，NPC将因找不到对应的帧序列而无法正确渲染行为，通常表现为“滑行”或卡在默认站立姿势。因此，动态日程注入往往伴随着动态的动画数据注入 3。
3. 游戏状态持久化：邮件系统与社交图谱的深度开发
在《星露谷物语》中，邮件系统（Mail System）不仅是信息传递的媒介，更是游戏进度的核心状态管理器（State Manager）。所有的事件触发、配方解锁、以及一次性对话的过滤，很大程度上都依赖于“邮件标志”（Mail Flags）。
3.1 邮件数据的程序化构建与发送
Game1.player 对象暴露了三个关键的集合用于管理邮件状态，理解它们的区别对于模组开发至关重要：
mailbox: 类型为 List<string>，存储当前在邮箱中等待玩家阅读的邮件ID。
mailReceived: 类型为 HashSet<string>，存储玩家已经阅读过的邮件ID，以及各种非邮件形式的游戏进度标志（如 ccIsComplete）。
mailForTomorrow: 类型为 List<string>，存储将在下一个 DayStarted 处理周期加入邮箱的邮件ID 10。
3.1.1 即时投递与次日投递的逻辑差异
即时投递（Instant Delivery）：
若希望邮件在当前游戏刻（Tick）立即出现在邮箱中，模组需直接操作 mailbox 集合。这常用于调试或对即时事件的响应。

C#


public void SendMailInstant(string mailId)
{
    // 必须进行双重检查以防止重复发送
    if (!Game1.player.mailbox.Contains(mailId) &&!Game1.player.mailReceived.Contains(mailId))
    {
        Game1.player.mailbox.Add(mailId);
        // 播放新邮件提示音
        Game1.playSound("newArtifact");
    }
}


次日投递（Queued Delivery）：
这是更符合游戏叙事节奏的方式。模组将ID加入 mailForTomorrow，游戏引擎会在玩家睡觉后的结算阶段将其转移到 mailbox。

C#


public void QueueMail(string mailId)
{
    // 使用游戏内置的辅助方法检查所有三个集合
    if (!Game1.player.hasOrWillReceiveMail(mailId))
    {
        Game1.player.mailForTomorrow.Add(mailId);
    }
}


hasOrWillReceiveMail 方法是一个极具价值的API，它能同时查询 mailReceived、mailbox 和 mailForTomorrow，有效防止逻辑错误导致的重复投递 10。
3.2 邮件内容的动态生成与Token解析
邮件内容存储在 Data/Mail 字典中。C#模组不仅可以静态添加内容，还可以利用Token系统实现动态文本。
高级Token语法：
%item object (O)388 50 %%：附件，赠送50个木材。1.6版本引入了 (O) 前缀的限定ID系统，解决了旧版本ID冲突的问题 10。
%item money 500 1000 %%：随机赠送500到1000金币。
%item quest 123 true %%：阅读邮件后自动接取ID为123的任务。
%action AddMoney 500 %%：触发动作（Trigger Action）。这是1.6版本最强大的新增功能之一，允许邮件触发任意定义在 Data/TriggerActions 中的逻辑字符串，如修改好感度、播放过场动画或改变地图状态 12。
C# 注入示例：

C#


public void OnAssetRequested(object sender, AssetRequestedEventArgs e)
{
    if (e.NameWithoutLocale.IsEquivalentTo("Data/Mail"))
    {
        e.Edit(asset =>
        {
            var data = asset.AsDictionary<string, string>().Data;
            // 动态生成邮件内容，支持占位符 @ (玩家名)
            data = "亲爱的 @,^这里有一份来自星灵界的礼物。^  -法师%item object (O)PrismaticShard 1%%[#]神秘包裹";
        });
    }
}


如果在 mailbox 中添加了ID但未在 Data/Mail 中定义内容，游戏会抛出异常或显示空白信件（Error Item），因此内容注入必须先于发送逻辑执行 13。
3.3 社交图谱（Social Graph）与“八卦”传播算法
虽然原版游戏仅通过 Friendship 类维护一对一的好感度，但模组可以通过 Data/Characters 中的 FriendsAndFamily 字段构建一个复杂的社交图谱 15。该字段定义了每个NPC的亲密关系网（如Pierre与Caroline，Sebastian与Sam）。
“八卦”传播算法（Gossip Algorithm）的理论实现：
基于图论中的传播模型，我们可以利用C#在 DayEnding 事件中模拟信息的流动：
事件源（Event Source）： 假设玩家被NPC A看到在翻垃圾桶。
初始节点状态： NPC A的内部状态（通过ModData存储）标记为“知晓翻垃圾桶”。
传播阶段（Propagation）：
遍历NPC A的 FriendsAndFamily 列表。
应用概率函数 。例如，性格为“outgoing”（外向）的NPC传播概率更高。
如果传播成功，将目标NPC的状态也标记为“知晓”。
反应阶段（Reaction）： 在对话生成（Dialogue）阶段，检查NPC是否带有该标记。如果有，则通过 AssetRequested 动态替换其默认对话为嘲讽文本 16。
这种逻辑将原本静态的“全知”或“无知”状态，转化为一个有机的、基于网络拓扑的信息扩散过程，极大地增强了社区的真实感。
4. 环境与随机性的程序化控制：天气与运气
环境控制涉及对 Game1 静态类的深度操作。随着1.6版本的更新，天气系统从简单的整数枚举（Enum）转变为基于字符串ID的系统，这为自定义天气提供了可能。
4.1 天气系统的状态机操纵
游戏的每日天气是在 Game1.newDayAfterFade 方法中确定的。
关键字段：
Game1.weatherForTomorrow: 存储次日天气ID的字符串（如 "Rain", "Sun", "Storm", "GreenRain"）。
Game1.isRaining, Game1.isSnowing: 当前天气的布尔标志。
C# 操纵策略：
要强制改变明天的天气，模组必须在游戏计算完天气后、但存档写入前进行干预。最佳的钩子是 GameLoop.DayStarted（用于修正当日）或 GameLoop.DayEnding（用于设定次日）。

C#


// 强制明天为雷雨天
// 注意：1.6版本支持自定义天气ID
Game1.weatherForTomorrow = Game1.weather_lightning; // 或直接赋值字符串 "Storm"


对于自定义天气（如“迷雾”），仅修改ID是不够的。开发者必须挂钩 Display.RenderedWorld 事件，在屏幕上绘制自定义的全屏纹理覆盖层（Overlay），并可能需要通过Harmony补丁拦截 Game1.getDebrisWeather 以生成特定的粒子效果 18。
4.2 运气值的精确控制
每日运气（Daily Luck）是一个浮点数 Game1.dailyLuck，范围通常在 -0.1 到 0.1 之间。
双重修改陷阱：
需要注意的是，存在 Game1.dailyLuck（全局运气，影响电视预报）和 Game1.player.DailyLuck（玩家个人运气）。在大多数逻辑判断中，游戏使用的是 Game1.dailyLuck，但在某些特定交互（如采矿掉落）可能会参考玩家属性。为了保证一致性，模组应同时修改两者。

C#


private void OnDayStarted(object sender, DayStartedEventArgs e)
{
    double forcedLuck = 0.12; // 极好运气
    Game1.dailyLuck = forcedLuck;
    Game1.player.DailyLuck = forcedLuck;
}


这种直接的字段赋值比依赖控制台命令更稳定，是实现“幸运日”模组的标准做法 18。
5. 自定义用户界面框架：移动电话系统的架构解析
由 aedenthorn 开发并由社区维护的“Mobile Phone”模组，代表了SMAPI在UI层面的一种高级应用范式：在不修改游戏引擎核心UI代码的前提下，构建一套完全独立的交互系统。
5.1 虚拟操作系统的实现原理
该框架并未利用游戏原本的菜单系统（IClickableMenu），而是直接在 Display.RenderedHud 事件中绘制纹理。
核心组件架构：
纹理合成（Texture Composition）： 手机的外壳、屏幕背景和APP图标被分层加载。
坐标映射（Coordinate Mapping）： 框架维护了一个相对于屏幕右下角（通常位置）的局部坐标系。当 Input.ButtonPressed 事件触发时，框架检测鼠标点击是否落在手机的包围盒（Bounding Box）内。如果是，则拦截输入（e.Button.Consume()），防止触发游戏内的工具使用动作。
API 注册机制： 这是该模组成为“框架”的关键。它暴露了一个接口 IMobilePhoneApi，允许第三方模组注册自己的应用程序 20。
API 设计模式（概念）：

C#


public interface IMobilePhoneApi
{
    // 第三方模组调用此方法注册APP
    bool AddApp(string appId, string appName, Action onLaunchCallback, Texture2D iconTexture);
}


当第三方模组加载时，它通过 helper.ModRegistry.GetApi<IMobilePhoneApi> 获取接口实例，并传入自己的启动委托（Delegate）。当玩家点击手机屏幕上的图标时，框架调用该委托，从而打开第三方模组的自定义菜单（例如“日程查看器”或“阿比盖尔的即时通讯软件”）20。
5.2 Android平台的适配挑战
将此类UI框架移植到Android平台（通过SMAPI Android Installer）面临巨大挑战。Android版的SMAPI将触摸信号映射为鼠标点击，但屏幕缩放比例（Zoom Level）和UI缩放（UI Scale）的处理逻辑与PC版截然不同。
技术难点：
视口锚定（Viewport Anchoring）： 代码必须频繁调用 Game1.uiViewport 来重新计算手机在屏幕上的位置，以适应动态的分辨率变化。
输入模拟： 移动端缺少键盘热键（如按 'P' 打开手机）。因此，框架必须在屏幕上始终渲染一个可点击的切换按钮（Toggle Button），或者集成到Android特有的虚拟键盘工具栏中 23。
6. 下一代AI集成：生成式代理与“斯坦福小镇”架构的移植
随着LLM技术的发展，将“斯坦福小镇”（Stanford Smallville）实验中的生成式代理（Generative Agents）引入星露谷物语已从理论走向实践。模组如 "Inworld AI" 和 "StardewRPG" (基于RPGGO API) 正在重塑NPC的行为逻辑。
6.1 生成式代理的内存架构
根据“Generative Agents”论文，一个具备拟人行为的代理需要三个核心组件，这在C#模组中对应着复杂的数据结构设计 26：
记忆流（Memory Stream）： 一个按时间顺序排列的观察记录列表。在C#中，这通常实现为一个基于SQLite或JSON的本地数据库。
数据项： ``
示例： ``
反思（Reflection）： 定期（如每晚 DayEnding）运行的高级认知过程。模组提取当天的记忆，发送给LLM总结出高级推论。
输入： “Farmer送了水仙花”，“Farmer每天都打招呼”。
输出（反思）： “Farmer似乎想和我建立亲密关系。”
规划（Planning）： 这是生成动态日程的关键。
6.2 实时AI调度系统的实现
在星露谷中实现AI驱动的调度，需要打破原有的静态日程表模式。
技术流程：
预计算（Pre-computation）： 由于LLM API调用存在延迟，模组不能在游戏运行时实时请求下一步行动。通常在游戏内每天开始前（DayStarted之前，或在前一晚的结算画面期间），模组将NPC的状态（位置、意图、记忆）发送给云端API（如RPGGO或OpenAI）。
日程生成（Schedule Generation）： LLM返回一份自然语言描述的日程：“8点起床，9点去皮埃尔店里买种子，下午1点去墓地散步。”
坐标转译（Translation）： 模组必须包含一个“语义-坐标映射表”（Semantic-to-Coordinate Map）。
“皮埃尔店里” -> SeedShop 12 20
“墓地” -> Town 47 87
动态注入： 将转译后的坐标序列封装为 SchedulePathDescription，并在 DayStarted 事件中注入到NPC实例中。
防幻觉机制（Hallucination Guard）： AI可能会指令NPC前往不存在的地点或无法到达的区域。模组层必须包含路径验证逻辑，利用 Game1.isLocationAccessible 和 PathFinder 预先验证路径的可行性，若不可行则回退到默认日程 28。
6.3 性能优化：异步处理与多线程
在单线程的MonoGame循环中，HTTP请求会阻塞游戏帧。因此，所有与AI服务器的通信必须在 Task.Run 启动的后台线程中进行。当数据返回时，必须使用线程安全的方式（或在下一帧的 UpdateTicked 中）将数据同步回主游戏线程，以避免并发冲突。
7. 事件编排与“爷爷的幽灵”
事件系统（Events）是星露谷的脚本语言。以ID 558291（爷爷的评估）为例，这是游戏中最复杂的硬编码事件之一。
7.1 程序化触发与逻辑覆写
虽然大多数事件通过条件触发，但C#模组可以强制启动事件。

C#


public void TriggerGrandpaEvaluation()
{
    // 定义事件脚本字符串
    // 格式：音乐/视口位置/角色数据/指令序列...
    string eventScript = "none/64 15/farmer 64 16 2 grandpa 64 12 2/pause 1000/speak grandpa \"我亲爱的孩子...\"/pause 500/end";
    
    Game1.globalFadeToBlack(() => {
        Game1.currentLocation.startEvent(new Event(eventScript));
    }, 0.02f);
}


原版的评估逻辑（点亮蜡烛）是由事件命令 grandpaEvaluation 触发的，该命令在 Event.cs 中是硬编码的。若要修改评估标准（例如，要求更高分数才能获得紫色猫头鹰雕像），仅仅修改事件脚本是不够的，必须使用 Harmony 库对 Utility.getGrandpaScore() 方法进行字节码插桩（Transpilation）或前缀拦截（Prefix Patch），重新定义分数的计算逻辑 31。
8. 结论与技术展望
《星露谷物语》的模组架构已经从简单的“资源替换”进化为“逻辑重构”。通过SMAPI提供的 Content、GameLoop 和 Reflection API，开发者能够拆解并重组游戏最核心的系统——从NPC的每一步移动到天气的生成算法。
未来的技术前沿在于外部系统的深度集成。无论是通过移动电话框架引入全新的UI交互范式，还是通过LLM引入具有无限可能性的生成式叙事，模组开发正在打破游戏原本的代码边界。对于开发者而言，掌握异步编程模型、理解MonoGame的渲染管线以及熟悉图论在社交模拟中的应用，将是构建下一代沉浸式模组的关键。
附录：核心技术数据表
表 1：NPC日程注入方法的架构对比
注入方法
实现难度
持久性
兼容性
适用场景
XNB 文件替换
低
永久
极差（覆盖冲突）
传统的静态修改（已废弃）
Content Patcher
低（JSON）
会话级
高（合并机制）
基于条件的静态日程切换
C# 直接注入 (Volatile)
中（代码）
易失（当日有效）
高
实时AI决策、临时行为、调试
AssetRequested API
中（C#）
会话级
极高
框架模组、复杂的动态分支逻辑

表 2：SMAPI 关键状态管理事件
事件名称
触发时机
主要技术用途
GameLoop.SaveLoaded
存档加载后，日开始前
初始化模组数据，完整性检查
GameLoop.DayStarted
每日6:00 AM
注入日程、重置每日运气、生成物体
GameLoop.DayEnding
玩家入睡或晕倒时
社交图谱传播（八卦算法）、邮件队列处理
GameLoop.UpdateTicked
每秒60次（每帧）
实时输入拦截、UI绘制、平滑动画插值
Content.AssetRequested
资源加载时
动态修补文本、纹理、数据字典

表 3：邮件系统高级数据Token (1.6+)
Token 语法
功能描述
版本状态
%item object (O)388 50 %%
赠送50个木材（使用限定ID）
当前标准 (1.6+)
%item object 388 50 %%
赠送50个木材（旧式ID）
已废弃但向下兼容
%item money 500 1000 %%
赠送500-1000之间的随机金币
当前标准
%item quest 123 true %%
自动接取ID为123的任务
当前标准
%action AddMoney 500 %%
触发通用动作（Trigger Action）
1.6新增，推荐用于复杂逻辑

1
引用的著作
Modding:Schedule data - Stardew Valley Wiki, 访问时间为 二月 8, 2026， https://stardewvalleywiki.com/Modding:Schedule_data
Modding Help - SMAPI changing NPC schedule? - Chucklefish Forums, 访问时间为 二月 8, 2026， https://community.playstarbound.com/threads/smapi-changing-npc-schedule.124607/
NPC Creation: Schedules - LemurKat - WordPress.com, 访问时间为 二月 8, 2026， https://lemurkat.wordpress.com/2020/10/10/npc-creation-schedules/
Modding:Migrate to Stardew Valley 1.6, 访问时间为 二月 8, 2026， https://stardewvalleywiki.com/Modding:Migrate_to_Stardew_Valley_1.6
aedenthorn/README.md at master - GitHub, 访问时间为 二月 8, 2026， https://github.com/mouahrara/aedenthorn/blob/master/README.md
Modding:Modder Guide/APIs/Events - Stardew Valley Wiki, 访问时间为 二月 8, 2026， https://stardewvalleywiki.com/Modding:Modder_Guide/APIs/Events
Coding Help - Trying to change a schedule - Stardew Valley Forums, 访问时间为 二月 8, 2026， https://forums.stardewvalley.net/threads/trying-to-change-a-schedule.13602/
Need Assistance: Dynamic building locations and schedules - Stardew Valley Forums, 访问时间为 二月 8, 2026， https://forums.stardewvalley.net/threads/need-assistance-dynamic-building-locations-and-schedules.7152/
[Spoilers!] Stardew Valley Relationship Map : r/StardewValley - Reddit, 访问时间为 二月 8, 2026， https://www.reddit.com/r/StardewValley/comments/ja0445/spoilers_stardew_valley_relationship_map/
Modding:Mail data - Stardew Valley Wiki, 访问时间为 二月 8, 2026， https://stardewvalleywiki.com/Modding:Mail_data
Modding Help - (Solved) Please help me with Json Assets and Mail Framework, 访问时间为 二月 8, 2026， https://community.playstarbound.com/threads/solved-please-help-me-with-json-assets-and-mail-framework.155070/
Modding:Trigger actions - Stardew Valley Wiki, 访问时间为 二月 8, 2026， https://stardewvalleywiki.com/Modding:Trigger_actions
I have made a mail object but it does not open. - Stardew Valley Forums, 访问时间为 二月 8, 2026， https://forums.stardewvalley.net/threads/i-have-made-a-mail-object-but-it-does-not-open.13898/
Modding Help - Mail popup but no mail? - Chucklefish Forums, 访问时间为 二月 8, 2026， https://community.playstarbound.com/threads/mail-popup-but-no-mail.140829/
Modding:NPC data - Stardew Valley Wiki, 访问时间为 二月 8, 2026， https://stardewvalleywiki.com/Modding:NPC_data
Modding:Dialogue - Stardew Valley Wiki, 访问时间为 二月 8, 2026， https://stardewvalleywiki.com/Modding:Dialogue
Gossip Protocol in Social Media Networks: Instagram and Beyond - DZone, 访问时间为 二月 8, 2026， https://dzone.com/articles/gossip-protocol-in-social-media-networks-instagram
Modding:Weather data - Stardew Valley Wiki, 访问时间为 二月 8, 2026， https://stardewvalleywiki.com/Modding:Weather_data
sndcode/luckydaymod: Sets your "daily luck" to a configurable value ingame - mod for Stardew Valley - GitHub, 访问时间为 二月 8, 2026， https://github.com/sndcode/luckydaymod
[Visual Studio] How to use Content Patcher and Json Assets within a SMAPI mod? - Reddit, 访问时间为 二月 8, 2026， https://www.reddit.com/r/SMAPI/comments/143kydy/visual_studio_how_to_use_content_patcher_and_json/
Mobile phone mod error | Stardew Valley Forums, 访问时间为 二月 8, 2026， https://forums.stardewvalley.net/threads/mobile-phone-mod-error.26753/
BinaryLip/ScheduleViewer: A Stardew Valley mod that adds 2 new menus for viewing NPCs' schedules. - GitHub, 访问时间为 二月 8, 2026， https://github.com/BinaryLip/ScheduleViewer
Modding:Installing SMAPI on Android - Stardew Valley Wiki, 访问时间为 二月 8, 2026， https://stardewvalleywiki.com/Modding:Installing_SMAPI_on_Android
Is there a list anywhere of mods that are confirmed to work on mobile : r/StardewValleyMods, 访问时间为 二月 8, 2026， https://www.reddit.com/r/StardewValleyMods/comments/1ki66wf/is_there_a_list_anywhere_of_mods_that_are/
Mobile Phone mod not working : r/StardewValleyMods - Reddit, 访问时间为 二月 8, 2026， https://www.reddit.com/r/StardewValleyMods/comments/1c1gdje/mobile_phone_mod_not_working/
joonspk-research/generative_agents: Generative Agents: Interactive Simulacra of Human Behavior - GitHub, 访问时间为 二月 8, 2026， https://github.com/joonspk-research/generative_agents
[R] Generative Agents: Interactive Simulacra of Human Behavior - Joon Sung Park et al Stanford University 2023 : r/MachineLearning - Reddit, 访问时间为 二月 8, 2026， https://www.reddit.com/r/MachineLearning/comments/12hluz1/r_generative_agents_interactive_simulacra_of/
How we Combined Generative AI with Stardew Valley, 访问时间为 二月 8, 2026， https://rpggodotai.wordpress.com/2024/11/24/how-we-combined-generative-ai-with-stardew-valley/
I Added AI Villagers to Stardew Valley… - YouTube, 访问时间为 二月 8, 2026， https://www.youtube.com/watch?v=TTVEXwNN-Gc
Stanford Smallville is officially open-source! | by Riki Phukon - Medium, 访问时间为 二月 8, 2026， https://rikiphukon.medium.com/stanford-smallville-is-officially-open-source-9882e3fbc981
Modding:Event data - Stardew Valley Wiki, 访问时间为 二月 8, 2026， https://stardewvalleywiki.com/Modding:Event_data
Modding:Modder Guide/Game Fundamentals - Stardew Valley Wiki, 访问时间为 二月 8, 2026， https://stardewvalleywiki.com/Modding:Modder_Guide/Game_Fundamentals
Need help with schedule of custom npc. : r/SMAPI - Reddit, 访问时间为 二月 8, 2026， https://www.reddit.com/r/SMAPI/comments/1crc3ro/need_help_with_schedule_of_custom_npc/
RELEASED - Mail Framework Mod [v1.3.0-beta.6] - Chucklefish Forums, 访问时间为 二月 8, 2026， https://community.playstarbound.com/threads/mail-framework-mod-v1-3-0-beta-6.145900/
