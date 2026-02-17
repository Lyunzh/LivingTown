# SMAPI Mod 开发技巧指南

基于官方 Stardew Valley Wiki (https://stardewvalleywiki.com/Modding:Index) 整理

## 快速开始

### 1. 项目设置要点

```csharp
// 必须引用 Pathoschild.Stardew.ModBuildConfig NuGet 包
// 目标框架必须是 .NET 6 (与游戏版本匹配)
// 使用 Class Library 项目类型，不是 .NET Framework!
```

### 2. Mod 入口类结构

```csharp
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace YourModName;

public class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        // 在这里订阅事件
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
    }
    
    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        Monitor.Log("Mod loaded!", LogLevel.Info);
    }
}
```

### 3. manifest.json 必需字段

```json
{
  "Name": "Your Mod Name",
  "Author": "Your Name",
  "Version": "1.0.0",
  "Description": "One or two sentences about the mod",
  "UniqueID": "YourName.YourModName",
  "EntryDll": "YourModName.dll",
  "MinimumApiVersion": "4.0.0",
  "UpdateKeys": []
}
```

## 关键检查点

### 检查世界是否就绪

```csharp
if (!Context.IsWorldReady) return;
```

### 检查玩家是否可操作

```csharp
if (!Context.IsPlayerFree) return;
```

### 抑制默认游戏行为

```csharp
// 在 ButtonPressed 事件中使用
Helper.Input.Suppress(e.Button);
```

## 常用游戏状态访问

```csharp
// 当前玩家
Farmer player = Game1.player;

// 当前位置
GameLocation location = Game1.currentLocation;

// 游戏时间 (2600 = 凌晨2点)
int time = Game1.timeOfDay;

// 获取NPC
NPC npc = Game1.getCharacterFromName("Abigail");

// 获取农场
Farm farm = Game1.getFarm();
```

## 日志记录

```csharp
Monitor.Log("Debug message", LogLevel.Debug);      // 调试用
Monitor.Log("Info message", LogLevel.Info);        // 普通信息
Monitor.Log("Warning message", LogLevel.Warn);     // 警告
Monitor.Log("Error message", LogLevel.Error);      // 错误
```

## 跨平台兼容性

### 文件路径处理

```csharp
// ✗ 不要这样做！在 Linux/Mac 上会失败
string path = this.Helper.DirectoryPath + "\\assets\\image.png";

// ✓ 正确的方法
string path = Path.Combine(this.Helper.DirectoryPath, "assets", "image.png");
```

### Asset 名称处理

```csharp
// ✗ 不要这样做
bool isAbigail = (asset.Name == Path.Combine("Characters", "Abigail"));

// ✓ 正确的方法
bool isAbigail = (asset.Name == PathUtilities.NormalizeAssetName("Characters/Abigail"));
```

## 资源加载

### 从模组文件夹加载图片

```csharp
Texture2D texture = helper.Content.Load<Texture2D>("assets/my-image.png");
```

### 从游戏内容加载

```csharp
Texture2D portrait = Game1.content.Load<Texture2D>("Portraits/Abigail");
```

## 输入处理

### 检测按键

```csharp
private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
{
    // 检查特定按键
    if (e.Button == SButton.F5)
    {
        Monitor.Log("F5 pressed!", LogLevel.Info);
    }
    
    // 检查是否是动作按钮 (鼠标右键/控制器 A)
    if (e.Button.IsActionButton())
    {
        // 处理动作
    }
}
```

### 获取光标位置

```csharp
Vector2 cursorPosition = e.Cursor.ScreenPixels;  // 屏幕像素坐标
Vector2 tilePosition = e.Cursor.Tile;            // 瓦片坐标
Vector2 grabTile = e.Cursor.GrabTile;            // 抓取瓦片坐标
```

## 多人游戏注意事项

### 检查是否是主机

```csharp
if (Context.IsMainPlayer)
{
    // 只有主机执行的代码
}
```

### 检查是否是多人游戏

```csharp
if (Context.IsMultiplayer)
{
    // 多人游戏特定逻辑
}
```

### 发送多人消息

```csharp
helper.Multiplayer.SendMessage(
    message: myData,
    messageType: "MyMessageType",
    modIDs: new[] { this.ModManifest.UniqueID },
    playerIDs: new[] { playerId }  // null 表示发送给所有玩家
);
```

## 配置支持

```csharp
// 定义配置类
public class ModConfig
{
    public bool EnableFeature { get; set; } = true;
    public int SomeValue { get; set; } = 10;
}

// 在 Entry 中读取配置
public override void Entry(IModHelper helper)
{
    var config = helper.ReadConfig<ModConfig>();
}
```

## 数据持久化

### 存储模组数据

```csharp
// 保存数据
helper.Data.WriteSaveData("MyKey", myData);

// 读取数据
var data = helper.Data.ReadSaveData<MyDataType>("MyKey");
```

### 使用 ModData (保存在存档中)

```csharp
// 给 NPC 添加自定义数据
npc.modData["MyMod_CustomValue"] = "some value";

// 读取
if (npc.modData.TryGetValue("MyMod_CustomValue", out string value))
{
    // 使用 value
}
```

## 调试技巧

### 使用 SMAPI 控制台

1. 启动游戏时保持 SMAPI 控制台窗口可见
2. 按 `~` 键可以在游戏中打开控制台
3. 使用 `help` 命令查看所有可用命令

### 常用控制台命令

```
help              - 显示帮助
mods              - 列出已加载的模组
debug             - 切换调试模式
warp [location]   - 传送到指定位置
time set [time]   - 设置时间
```

### 开发时日志级别

在 `smapi-internal/config.json` 中设置:

```json
{
  "ConsoleColors": {
    "Trace": "Gray",
    "Debug": "DarkGray"
  }
}
```

## 常见陷阱

### 1. 不要在非主线程调用游戏 API

```csharp
// ✗ 错误：在后台线程中调用
Task.Run(() => {
    Game1.player.Money += 100;  // 可能崩溃！
});

// ✓ 正确：在主线程执行
helper.Events.GameLoop.UpdateTicked += (s, e) => {
    Game1.player.Money += 100;
};
```

### 2. 检查 null 值

```csharp
var npc = Game1.getCharacterFromName("InvalidName");
if (npc == null) return;  // 总是检查！
```

### 3. 时间格式

```csharp
// 游戏时间使用 24 小时格式，以 10 分钟为单位
// 600 = 6:00 AM, 1300 = 1:00 PM, 2600 = 2:00 AM (次日)
int time = Game1.timeOfDay;
```

### 4. 瓦片坐标 vs 像素坐标

```csharp
// 瓦片坐标 (16x16 像素)
Vector2 tile = new Vector2(10, 15);

// 转换为像素坐标
Vector2 pixels = tile * Game1.tileSize;  // 160, 240

// 转换回瓦片坐标
Vector2 tileAgain = pixels / Game1.tileSize;
```

## 参考链接

- [SMAPI 官方文档](https://stardewvalleywiki.com/Modding:Modder_Guide/Get_Started)
- [SMAPI API 参考](https://stardewvalleywiki.com/Modding:Modder_Guide/APIs)
- [事件参考](https://stardewvalleywiki.com/Modding:Modder_Guide/APIs/Events)
- [游戏基础](https://stardewvalleywiki.com/Modding:Modder_Guide/Game_Fundamentals)

## 社区资源

- [Stardew Valley Discord](https://discord.gg/stardewvalley) - #making-mods 频道
- [SMAPI Mod 兼容性列表](https://smapi.io/mods)
- [SMAPI 日志解析器](https://smapi.io/log)
- [JSON 验证器](https://smapi.io/json)
