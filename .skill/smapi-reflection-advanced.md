# SMAPI 反射与高级技巧

针对 LivingTown 模组的高级编程技巧和反射API使用

## 反射API基础

### 访问私有字段

```csharp
// 获取私有字段值
var field = helper.Reflection.GetField<List<string>>(npc, "dialogue");
List<string> privateDialogue = field.GetValue();

// 设置私有字段值
field.SetValue(newDialogueList);

// 检查字段是否存在
if (helper.Reflection.GetField<string>(npc, "someField") != null)
{
    // 字段存在
}
```

### 调用私有方法

```csharp
// 获取私有方法
var method = helper.Reflection.GetMethod(npc, "loadSchedule");

// 调用无参方法
method.Invoke();

// 调用带参数方法
method.Invoke(dayOfWeek, schedulePath);

// 获取返回值
var result = method.Invoke<int>(arg1, arg2);
```

### 获取私有属性

```csharp
// 获取属性值
var property = helper.Reflection.GetProperty<bool>(npc, "IsWalking");
bool isWalking = property.GetValue();

// 设置属性值
property.SetValue(true);
```

## LivingTown 常用反射模式

### NPC 日程操作

```csharp
public class NpcScheduleHelper
{
    private readonly IModHelper _helper;
    
    // 强制刷新NPC日程
    public void ForceScheduleRefresh(NPC npc)
    {
        var method = _helper.Reflection.GetMethod(npc, "checkSchedule");
        method.Invoke();
    }
    
    // 直接设置NPC路径点
    public void SetNpcPath(NPC npc, int time, string scheduleKey)
    {
        // 访问私有字段
        var scheduleField = _helper.Reflection.GetField<Dictionary<int, SchedulePathDescription>>(npc, "schedule");
        var schedule = scheduleField.GetValue();
        
        // 修改日程
        // ...
        
        scheduleField.SetValue(schedule);
    }
    
    // 解析主日程
    public Dictionary<int, string> ParseMasterSchedule(NPC npc, string rawSchedule)
    {
        var method = _helper.Reflection.GetMethod(npc, "parseMasterSchedule");
        return method.Invoke<Dictionary<int, string>>(rawSchedule);
    }
}
```

### 对话系统反射

```csharp
public class DialogueHelper
{
    private readonly IModHelper _helper;
    
    // 获取当前对话的所有页面
    public List<string> GetDialoguePages(Dialogue dialogue)
    {
        var field = _helper.Reflection.GetField<List<string>>(dialogue, "dialogues");
        return field.GetValue();
    }
    
    // 设置对话完成后执行的动作
    public void SetAfterDialogueAction(Dialogue dialogue, Action action)
    {
        var field = _helper.Reflection.GetField<GameLocation.afterQuestionBehavior>(dialogue, "answerChoiceBehavior");
        field.SetValue(new GameLocation.afterQuestionBehavior((who, answer) => action()));
    }
}
```

### 游戏状态修改

```csharp
public class GameStateHelper
{
    // 修改每日运气（反射方式）
    public void SetDailyLuck(double luck)
    {
        var field = helper.Reflection.GetField<float>(typeof(Game1), "dailyLuck");
        field.SetValue((float)luck);
    }
    
    // 修改天气
    public void ForceWeather(string weatherType)
    {
        // weatherForTomorrow 是公共字段，但isRaining等需要通过反射
        switch (weatherType)
        {
            case "Rain":
                Game1.isRaining = true;
                helper.Reflection.GetField<bool>(typeof(Game1), "isLightning").SetValue(false);
                helper.Reflection.GetField<bool>(typeof(Game1), "isSnowing").SetValue(false);
                break;
            case "Storm":
                Game1.isRaining = true;
                helper.Reflection.GetField<bool>(typeof(Game1), "isLightning").SetValue(true);
                break;
            case "Snow":
                Game1.isRaining = false;
                helper.Reflection.GetField<bool>(typeof(Game1), "isLightning").SetValue(false);
                helper.Reflection.GetField<bool>(typeof(Game1), "isSnowing").SetValue(true);
                break;
        }
    }
}
```

## 高级事件处理

### 自定义事件优先级

```csharp
// 使用 EventPriority 属性控制处理顺序
[EventPriority(EventPriority.High)]
private void OnButtonPressedEarly(object? sender, ButtonPressedEventArgs e)
{
    // 在其他模组之前处理
    // 可以抢先抑制输入
    if (ShouldSuppress(e.Button))
    {
        Helper.Input.Suppress(e.Button);
    }
}

[EventPriority(EventPriority.Low)]
private void OnButtonPressedLate(object? sender, ButtonPressedEventArgs e)
{
    // 在其他模组之后处理
    // 检查是否已被抑制
    if (Helper.Input.IsSuppressed(e.Button)) return;
}
```

### 事件快照处理

```csharp
private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
{
    // 注意：事件参数是快照
    // e.NewMenu 是事件触发时的菜单
    // 如果其他模组处理了此事件，状态可能已改变
    
    // 如果需要当前真实状态
    var currentMenu = Game1.activeClickableMenu;
    var snapshotMenu = e.NewMenu;
    
    if (currentMenu != snapshotMenu)
    {
        // 状态已被其他模组修改
    }
}
```

## 线程安全模式

### 主线程调度器

```csharp
public class MainThreadDispatcher
{
    private readonly ConcurrentQueue<Action> _actions = new();
    
    // 从后台线程调用
    public void Enqueue(Action action)
    {
        _actions.Enqueue(action);
    }
    
    // 在 UpdateTicked 中调用
    public void ProcessQueue()
    {
        while (_actions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Monitor.Log($"主线程任务失败: {ex.Message}", LogLevel.Error);
            }
        }
    }
}

// 使用示例
public class AsyncNpcManager
{
    private readonly MainThreadDispatcher _dispatcher;
    
    public async Task GenerateNpcResponse(string npcName)
    {
        // 在后台线程执行LLM调用
        var response = await CallLLMAsync(npcName);
        
        // 回到主线程更新游戏状态
        _dispatcher.Enqueue(() => {
            var npc = Game1.getCharacterFromName(npcName);
            npc?.showTextAboveHead(response);
        });
    }
}
```

### 并发字典使用

```csharp
public class ThreadSafeNpcData
{
    // 存储NPC数据（线程安全）
    private readonly ConcurrentDictionary<string, NpcSession> _sessions = new();
    
    public void StartConversation(string npcName)
    {
        _sessions.AddOrUpdate(
            npcName,
            new NpcSession { StartTime = DateTime.Now },
            (key, old) => {
                old.CancellationToken?.Cancel();
                return new NpcSession { StartTime = DateTime.Now };
            }
        );
    }
    
    public bool TryGetSession(string npcName, out NpcSession session)
    {
        return _sessions.TryGetValue(npcName, out session);
    }
    
    public void EndConversation(string npcName)
    {
        if (_sessions.TryRemove(npcName, out var session))
        {
            session.CancellationToken?.Cancel();
        }
    }
}
```

## 资源动态加载

### 运行时资源注入

```csharp
public class DynamicAssetLoader
{
    // 在 AssetRequested 中动态提供资源
    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        // 动态生成纹理
        if (e.NameWithoutLocale.IsEquivalentTo("LivingTown/GeneratedPortrait"))
        {
            e.LoadFrom(() => GeneratePortraitTexture(), AssetLoadPriority.Medium);
        }
        
        // 编辑现有资源
        if (e.NameWithoutLocale.IsEquivalentTo("Data/NPCDispositions"))
        {
            e.Edit(asset => {
                var data = asset.AsDictionary<string, string>();
                // 修改数据...
            });
        }
    }
    
    private Texture2D GeneratePortraitTexture()
    {
        // 动态生成纹理
        var texture = new Texture2D(Game1.graphics.GraphicsDevice, 64, 64);
        // ...
        return texture;
    }
}
```

### 资源缓存失效

```csharp
// 强制重新加载资源
helper.GameContent.InvalidateCache("LivingTown/GeneratedPortrait");

// 批量失效
helper.GameContent.InvalidateCache(assetName => 
    assetName.StartsWith("LivingTown/"));
```

## 性能优化技巧

### 对象池模式

```csharp
public class MessagePool
{
    private readonly ConcurrentBag<LLMMessage> _pool = new();
    private int _created = 0;
    private const int MaxPoolSize = 100;
    
    public LLMMessage Rent()
    {
        if (_pool.TryTake(out var message))
        {
            message.Reset();
            return message;
        }
        
        Interlocked.Increment(ref _created);
        return new LLMMessage();
    }
    
    public void Return(LLMMessage message)
    {
        if (_pool.Count < MaxPoolSize)
        {
            _pool.Add(message);
        }
    }
}
```

### 延迟初始化

```csharp
public class LazyNpcCache
{
    private Dictionary<string, NPC>? _cache;
    
    private Dictionary<string, NPC> Cache => _cache ??= LoadAllNpcs();
    
    public NPC? GetNpc(string name)
    {
        Cache.TryGetValue(name, out var npc);
        return npc;
    }
    
    private Dictionary<string, NPC> LoadAllNpcs()
    {
        // 加载所有NPC
        return Game1.locations
            .SelectMany(l => l.characters)
            .ToDictionary(n => n.Name, n => n);
    }
}
```

## 调试和监控

### 性能计时器

```csharp
public class PerformanceMonitor
{
    private readonly Dictionary<string, long> _timings = new();
    private readonly Dictionary<string, int> _callCounts = new();
    
    public IDisposable Measure(string operation)
    {
        var stopwatch = Stopwatch.StartNew();
        return new DisposableAction(() => {
            stopwatch.Stop();
            RecordTiming(operation, stopwatch.ElapsedMilliseconds);
        });
    }
    
    private void RecordTiming(string operation, long ms)
    {
        _timings[operation] = _timings.GetValueOrDefault(operation) + ms;
        _callCounts[operation] = _callCounts.GetValueOrDefault(operation) + 1;
    }
    
    public void LogStats()
    {
        foreach (var op in _timings.Keys)
        {
            var avg = _timings[op] / _callCounts[op];
            Monitor.Log($"{op}: 总计 {_timings[op]}ms, 平均 {avg}ms, 调用 {_callCounts[op]} 次", LogLevel.Debug);
        }
    }
}

// 使用
using (monitor.Measure("LLM_Generate"))
{
    var response = await GenerateResponse();
}
```

### 状态快照

```csharp
public class GameStateSnapshot
{
    public int Time { get; set; }
    public string Location { get; set; }
    public Dictionary<string, Vector2> NpcPositions { get; set; }
    
    public static GameStateSnapshot Capture()
    {
        return new GameStateSnapshot
        {
            Time = Game1.timeOfDay,
            Location = Game1.currentLocation?.Name ?? "Unknown",
            NpcPositions = Game1.currentLocation?.characters
                .ToDictionary(n => n.Name, n => n.Position) ?? new()
        };
    }
}
```

## 最佳实践

### 1. 防御性编程

```csharp
public void SafeReflectionOperation(NPC npc)
{
    try
    {
        var field = helper.Reflection.GetField<string>(npc, "privateField");
        if (field == null)
        {
            Monitor.Log("字段不存在，可能游戏版本不兼容", LogLevel.Warn);
            return;
        }
        
        var value = field.GetValue();
        // 处理值...
    }
    catch (Exception ex)
    {
        Monitor.Log($"反射操作失败: {ex.Message}", LogLevel.Error);
        // 优雅降级
    }
}
```

### 2. 版本检查

```csharp
public override void Entry(IModHelper helper)
{
    // 检查SMAPI版本
    if (helper.ModRegistry.GetApi<ISmaPIApi>("Pathoschild.SMAPI")?.ApiVersion.IsOlderThan("4.0.0") == true)
    {
        Monitor.Log("需要 SMAPI 4.0.0 或更高版本", LogLevel.Error);
        return;
    }
    
    // 检查游戏版本
    if (Game1.version < new Version("1.6.0"))
    {
        Monitor.Log("需要游戏 1.6.0 或更高版本", LogLevel.Error);
        return;
    }
}
```

### 3. 内存管理

```csharp
// 及时释放资源
public void Cleanup()
{
    // 取消所有任务
    foreach (var session in _activeSessions.Values)
    {
        session.CancellationToken?.Cancel();
    }
    
    // 清空集合
    _activeSessions.Clear();
    _messagePool.Clear();
    
    // 取消事件订阅
    helper.Events.GameLoop.UpdateTicked -= OnUpdateTicked;
}
```

## 参考

- [反射API文档](https://stardewvalleywiki.com/Modding:Modder_Guide/APIs/Reflection)
- [内容API文档](https://stardewvalleywiki.com/Modding:Modder_Guide/APIs/Content)
- [SMAPI源代码](https://github.com/Pathoschild/SMAPI)
