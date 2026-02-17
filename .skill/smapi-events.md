# SMAPI 事件系统详解

基于官方文档 (https://stardewvalleywiki.com/Modding:Modder_Guide/APIs/Events) 整理

## 事件基础

事件是 SMAPI 的核心机制，让你的模组能够响应游戏中的各种变化。事件在每次游戏 tick（约60次/秒）时触发。

### 基本用法

```csharp
public override void Entry(IModHelper helper)
{
    // 订阅事件 - 使用 += 操作符
    helper.Events.GameLoop.DayStarted += this.OnDayStarted;
}

private void OnDayStarted(object? sender, DayStartedEventArgs e)
{
    this.Monitor.Log("新的一天开始了！", LogLevel.Info);
}
```

### 取消订阅

```csharp
// 如果需要取消订阅
helper.Events.GameLoop.DayStarted -= this.OnDayStarted;
```

## 游戏循环事件 (GameLoop)

### 生命周期事件

```csharp
// 游戏启动后触发（每个游戏会话一次）
helper.Events.GameLoop.GameLaunched += (s, e) => {
    Monitor.Log("游戏启动完成！", LogLevel.Info);
};

// 存档加载完成后触发
helper.Events.GameLoop.SaveLoaded += (s, e) => {
    Monitor.Log("存档已加载！", LogLevel.Info);
    // 此时 Context.IsWorldReady == true
};

// 返回标题画面后触发
helper.Events.GameLoop.ReturnedToTitle += (s, e) => {
    Monitor.Log("返回标题画面", LogLevel.Info);
};
```

### 每日事件

```csharp
// 新的一天开始（6:00 AM）
helper.Events.GameLoop.DayStarted += (s, e) => {
    // 初始化每日数据
    // 注入 NPC 日程
    // 重置每日状态
};

// 一天结束（玩家睡觉或晕倒）
helper.Events.GameLoop.DayEnding += (s, e) => {
    // 保存每日摘要
    // 执行夜间逻辑
    // 发送次日邮件
};

// 时间变化（每10分钟）
helper.Events.GameLoop.TimeChanged += (s, e) => {
    Monitor.Log($"时间从 {e.OldTime} 变为 {e.NewTime}", LogLevel.Debug);
    // e.OldTime: 之前的时间
    // e.NewTime: 当前时间（2600 = 凌晨2点）
};
```

### 存档事件

```csharp
// 创建新存档时
helper.Events.GameLoop.SaveCreating += (s, e) => {
    // 初始化新存档数据
};

// 存档创建完成
helper.Events.GameLoop.SaveCreated += (s, e) => {
    // 新存档已创建
};

// 保存前触发
helper.Events.GameLoop.Saving += (s, e) => {
    // 确保所有数据已准备好保存
};

// 保存完成后
helper.Events.GameLoop.Saved += (s, e) => {
    // 执行保存后逻辑
};
```

### 更新 Tick 事件

```csharp
// 每帧更新前（约60次/秒）
helper.Events.GameLoop.UpdateTicking += (s, e) => {
    // e.Ticks: 游戏启动以来的 tick 数
    // e.IsOneSecond: 是否是一秒的开始
    // e.IsMultipleOf(30): 每0.5秒为 true
};

// 每帧更新后
helper.Events.GameLoop.UpdateTicked += (s, e) => {
    // 适合轮询游戏状态变化
};

// 每秒更新（比 UpdateTicked 更高效）
helper.Events.GameLoop.OneSecondUpdateTicking += (s, e) => {
    // 适合不需要每帧执行的逻辑
};
```

## 输入事件 (Input)

### 按钮事件

```csharp
// 按钮按下
helper.Events.Input.ButtonPressed += (s, e) => {
    // e.Button: 被按下的按钮 (SButton 枚举)
    // e.Cursor: 光标位置信息
    // e.IsDown(SButton.F): 检查 F 键是否正被按住
    
    if (e.Button == SButton.F5)
    {
        Monitor.Log("F5 被按下！", LogLevel.Info);
        // 抑制游戏接收此按键
        Helper.Input.Suppress(e.Button);
    }
};

// 按钮释放
helper.Events.Input.ButtonReleased += (s, e) => {
    // 按钮释放时的处理
};

// 多个按钮变化（更高效）
helper.Events.Input.ButtonsChanged += (s, e) => {
    // e.Pressed: 本次 tick 新按下的按钮
    // e.Held: 正在持续按住的按钮
    // e.Released: 本次 tick 释放的按钮
    
    foreach (SButton button in e.Pressed)
    {
        Monitor.Log($"按下: {button}", LogLevel.Debug);
    }
};
```

### 光标事件

```csharp
// 光标移动
helper.Events.Input.CursorMoved += (s, e) => {
    // e.OldPosition: 之前的位置
    // e.NewPosition: 当前位置
    
    Vector2 oldTile = e.OldPosition.GrabTile;
    Vector2 newTile = e.NewPosition.GrabTile;
};

// 鼠标滚轮
helper.Events.Input.MouseWheelScrolled += (s, e) => {
    // e.Delta: 滚动量（正数向上，负数向下）
    // e.OldValue, e.NewValue: 累计滚动值
};
```

## 显示事件 (Display)

### 菜单事件

```csharp
// 菜单变化
helper.Events.Display.MenuChanged += (s, e) => {
    // e.OldMenu: 之前的菜单（null 如果没有）
    // e.NewMenu: 新的菜单（null 如果关闭）
    
    if (e.NewMenu is DialogueBox dialogue)
    {
        Monitor.Log("对话菜单已打开", LogLevel.Debug);
    }
};
```

### 渲染事件（用于自定义 UI）

```csharp
// 渲染世界后（在菜单和 HUD 之下）
helper.Events.Display.RenderedWorld += (s, e) => {
    // e.SpriteBatch: 用于绘制的精灵批次
    // 在这里绘制的内容会显示在世界之上，但在菜单之下
};

// 渲染 HUD 后
helper.Events.Display.RenderedHud += (s, e) => {
    // 在这里绘制的内容会显示在 HUD 之上
};

// 渲染活动菜单后
helper.Events.Display.RenderedActiveMenu += (s, e) => {
    // 在这里绘制的内容会显示在当前菜单之上
};

// 窗口大小改变
helper.Events.Display.WindowResized += (s, e) => {
    Monitor.Log($"窗口大小: {e.OldSize} -> {e.NewSize}", LogLevel.Debug);
};
```

## 内容事件 (Content)

### 资源请求

```csharp
// 资源被请求时（用于修改或替换资源）
helper.Events.Content.AssetRequested += (s, e) => {
    // e.Name: 资源名称（包含本地化代码）
    // e.NameWithoutLocale: 不含本地化代码的资源名
    
    // 检查特定资源
    if (e.NameWithoutLocale.IsEquivalentTo("Portraits/Abigail"))
    {
        // 加载自定义资源
        e.LoadFromModFile<Texture2D>("assets/abigail.png", AssetLoadPriority.Medium);
    }
    
    // 编辑已有资源
    if (e.NameWithoutLocale.IsEquivalentTo("Data/NPCDispositions"))
    {
        e.Edit(asset => {
            var data = asset.AsDictionary<string, string>();
            // 修改数据
        });
    }
};

// 资源加载完成
helper.Events.Content.AssetReady += (s, e) => {
    // 资源已加载并可使用
};

// 资源被标记为无效（需要重新加载）
helper.Events.Content.AssetsInvalidated += (s, e) => {
    // e.Names: 被无效化的资源名称集合
};
```

## 世界事件 (World)

### NPC 和对象变化

```csharp
// NPC 列表变化
helper.Events.World.NpcListChanged += (s, e) => {
    // e.Location: 发生变化的位置
    // e.Added: 新增的 NPC
    // e.Removed: 移除的 NPC
    // e.IsCurrentLocation: 是否是当前玩家所在位置
    
    foreach (NPC npc in e.Added)
    {
        Monitor.Log($"{npc.Name} 进入 {e.Location.Name}", LogLevel.Debug);
    }
};

// 对象列表变化
helper.Events.World.ObjectListChanged += (s, e) => {
    // e.Added: 新增的对象（瓦片坐标 -> 对象）
    // e.Removed: 移除的对象
    
    foreach (var pair in e.Added)
    {
        Vector2 tile = pair.Key;
        StardewValley.Object obj = pair.Value;
        Monitor.Log($"在 {tile} 添加了 {obj.Name}", LogLevel.Debug);
    }
};

// 地形特征变化
helper.Events.World.TerrainFeatureListChanged += (s, e) => {
    // 树木、作物、地板等
};

// 家具变化
helper.Events.World.FurnitureListChanged += (s, e) => {
    // 家具添加/移除
};
```

### 位置变化

```csharp
// 位置列表变化
helper.Events.World.LocationListChanged += (s, e) => {
    // e.Added: 新增的位置
    // e.Removed: 移除的位置
};

// 建筑列表变化
helper.Events.World.BuildingListChanged += (s, e) => {
    // e.Location: 发生变化的地点
    // e.Added: 新增的建筑
    // e.Removed: 移除的建筑
};
```

## 玩家事件 (Player)

```csharp
// 背包变化
helper.Events.Player.InventoryChanged += (s, e) => {
    // e.Player: 发生变化的玩家
    // e.Added: 添加的物品
    // e.Removed: 移除的物品
    // e.QuantityChanged: 数量变化的物品栈
    // e.IsLocalPlayer: 是否是本地玩家
};

// 技能等级变化
helper.Events.Player.LevelChanged += (s, e) => {
    // e.Skill: 技能类型（SkillType.Farming 等）
    // e.OldLevel: 之前的等级
    // e.NewLevel: 新等级
};

// 玩家传送
helper.Events.Player.Warped += (s, e) => {
    // e.OldLocation: 之前的位置
    // e.NewLocation: 新位置
    Monitor.Log($"玩家从 {e.OldLocation.Name} 传送到 {e.NewLocation.Name}", LogLevel.Debug);
};
```

## 多人游戏事件 (Multiplayer)

```csharp
// 玩家连接
helper.Events.Multiplayer.PeerConnected += (s, e) => {
    // e.Peer: 连接的玩家信息
    Monitor.Log($"玩家 {e.Peer.PlayerID} 已连接", LogLevel.Info);
};

// 玩家断开
helper.Events.Multiplayer.PeerDisconnected += (s, e) => {
    Monitor.Log($"玩家 {e.Peer.PlayerID} 已断开", LogLevel.Info);
};

// 收到模组消息
helper.Events.Multiplayer.ModMessageReceived += (s, e) => {
    // e.FromPlayerID: 发送者 ID
    // e.FromModID: 发送模组 ID
    // e.Type: 消息类型
    // e.ReadAs<T>(): 读取消息内容
    
    if (e.FromModID == this.ModManifest.UniqueID && e.Type == "MyMessage")
    {
        var data = e.ReadAs<MyMessageClass>();
        // 处理消息
    }
};
```

## 高级技巧

### 1. 变化监测模式

当某个游戏状态没有专门的事件时，使用 UpdateTicked 轮询：

```csharp
private Event? LastEvent;

private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
{
    // 检测事件是否刚结束
    if (this.LastEvent != null && Game1.CurrentEvent == null)
    {
        this.Monitor.Log($"事件 {this.LastEvent.id} 已结束！");
        OnEventEnded();
    }
    
    this.LastEvent = Game1.CurrentEvent;
}
```

### 2. 事件优先级

使用 `[EventPriority]` 属性控制处理顺序：

```csharp
[EventPriority(EventPriority.High)]  // 在其他模组之前处理
private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
{
    // 高优先级处理
}

[EventPriority(EventPriority.Low)]   // 在其他模组之后处理
private void OnDayStarted(object? sender, DayStartedEventArgs e)
{
    // 低优先级处理
}
```

优先级：Low < Default < High

### 3. 条件事件订阅

```csharp
public override void Entry(IModHelper helper)
{
    // 只在满足条件时订阅
    if (helper.ModRegistry.IsLoaded("SomeMod.UniqueID"))
    {
        helper.Events.GameLoop.DayStarted += OnDayStartedForIntegration;
    }
}
```

### 4. 防抖处理

对于高频事件（如 UpdateTicked），避免重复执行：

```csharp
private int LastExecutionTick = -1;

private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
{
    // 每秒只执行一次
    if (e.Ticks - LastExecutionTick < 60) return;
    LastExecutionTick = e.Ticks;
    
    // 执行逻辑
}
```

### 5. 事件快照行为

事件基于游戏状态的快照触发。如果模组 A 处理事件时改变了状态，模组 B 仍会收到原始状态的事件。

```csharp
private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
{
    // e.NewMenu 是快照时的菜单
    // 如果其他模组关闭了菜单，这里仍会得到打开时的菜单
    // 如果需要当前状态，直接检查 Game1.activeClickableMenu
    
    var currentMenu = Game1.activeClickableMenu;  // 当前真实状态
    var snapshotMenu = e.NewMenu;                  // 事件触发时的状态
}
```

## 最佳实践

### 1. 选择合适的事件

- **使用语义化事件**：优先使用 DayStarted 而不是 UpdateTicked + 时间检查
- **避免过度轮询**：如果不需要每帧更新，使用 OneSecondUpdateTicked
- **按需订阅**：只订阅你真正需要的事件

### 2. 性能考虑

```csharp
// ✗ 低效：在 UpdateTicked 中执行昂贵操作
helper.Events.GameLoop.UpdateTicked += (s, e) => {
    var allData = LoadHugeDataFile();  // 每帧都加载！
};

// ✓ 高效：缓存数据
private HugeData? CachedData;

helper.Events.GameLoop.UpdateTicked += (s, e) => {
    CachedData ??= LoadHugeDataFile();  // 只加载一次
    // 使用 CachedData
};
```

### 3. 错误处理

```csharp
private void OnDayStarted(object? sender, DayStartedEventArgs e)
{
    try
    {
        RiskyOperation();
    }
    catch (Exception ex)
    {
        Monitor.Log($"OnDayStarted 出错: {ex}", LogLevel.Error);
        // 继续执行，不要让一个错误破坏整个事件链
    }
}
```

### 4. 状态检查

```csharp
private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
{
    // 总是检查世界是否就绪
    if (!Context.IsWorldReady) return;
    
    // 检查玩家是否可以自由操作
    if (!Context.IsPlayerFree) return;
    
    // 处理输入
}
```

## 完整示例：监听多个事件

```csharp
public class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        // 游戏生命周期
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.DayEnding += OnDayEnding;
        
        // 输入
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        
        // 世界变化
        helper.Events.World.NpcListChanged += OnNpcListChanged;
        helper.Events.World.ObjectListChanged += OnObjectListChanged;
        
        // 玩家
        helper.Events.Player.Warped += OnPlayerWarped;
        
        // 显示
        helper.Events.Display.MenuChanged += OnMenuChanged;
    }
    
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        Monitor.Log("模组已加载！", LogLevel.Info);
    }
    
    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        Monitor.Log($"存档已加载：{Game1.player.Name} 的农场", LogLevel.Info);
    }
    
    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        Monitor.Log($"第 {Game1.year} 年 {Game1.currentSeason} 第 {Game1.dayOfMonth} 天", LogLevel.Info);
    }
    
    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        Monitor.Log("一天结束了，保存数据中...", LogLevel.Debug);
    }
    
    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady) return;
        
        if (e.Button == SButton.F5)
        {
            Monitor.Log("F5 快捷键被触发！", LogLevel.Info);
            Helper.Input.Suppress(e.Button);
        }
    }
    
    private void OnNpcListChanged(object? sender, NpcListChangedEventArgs e)
    {
        foreach (var npc in e.Added)
        {
            Monitor.Log($"{npc.Name} 进入了 {e.Location.Name}", LogLevel.Trace);
        }
    }
    
    private void OnObjectListChanged(object? sender, ObjectListChangedEventArgs e)
    {
        foreach (var pair in e.Added)
        {
            Monitor.Log($"在 {e.Location.Name} 的 {pair.Key} 添加了 {pair.Value.Name}", LogLevel.Trace);
        }
    }
    
    private void OnPlayerWarped(object? sender, WarpedEventArgs e)
    {
        Monitor.Log($"玩家从 {e.OldLocation.Name} 传送到 {e.NewLocation.Name}", LogLevel.Debug);
    }
    
    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        if (e.NewMenu is DialogueBox)
        {
            Monitor.Log("对话开始", LogLevel.Trace);
        }
        else if (e.OldMenu is DialogueBox && e.NewMenu == null)
        {
            Monitor.Log("对话结束", LogLevel.Trace);
        }
    }
}
```

## 参考

- [官方事件文档](https://stardewvalleywiki.com/Modding:Modder_Guide/APIs/Events)
- [SMAPI API 参考](https://stardewvalleywiki.com/Modding:Modder_Guide/APIs)
