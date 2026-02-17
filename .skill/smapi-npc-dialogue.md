# SMAPI NPC 与对话系统技巧

针对 LivingTown AI NPC 对话模组的关键技巧

## NPC 基础操作

### 获取 NPC

```csharp
// 通过名称获取 NPC
NPC npc = Game1.getCharacterFromName("Abigail");
if (npc == null) return;  // 总是检查 null！

// 检查 NPC 是否在当前位置
if (npc.currentLocation == Game1.currentLocation)
{
    // NPC 和玩家在同一位置
}

// 获取 NPC 位置
Vector2 npcTile = npc.Tile;
Vector2 npcPixelPosition = npc.Position;
```

### NPC 状态检查

```csharp
// 获取友谊等级（0-14 心）
int friendshipLevel = Game1.player.getFriendshipLevelForNPC("Abigail");
int hearts = friendshipLevel / 250;  // 每颗心 250 点

// 检查是否可以送礼
bool canReceiveGift = Game1.player.friendshipData["Abigail"].GiftsToday == 0;

// 检查是否是生日
bool isBirthday = npc.isBirthday(Game1.currentSeason, Game1.dayOfMonth);

// 获取 NPC 当前对话（如果有）
string? currentDialogue = npc.CurrentDialogue?.Count > 0 
    ? npc.CurrentDialogue.Peek().getCurrentDialogue() 
    : null;
```

## 显示对话

### 气泡对话（头顶显示）

```csharp
// 在 NPC 头顶显示文字气泡
npc.showTextAboveHead("Hello farmer!", duration: 4000);

// 带表情的气泡
npc.showTextAboveHead("Great!");
npc.doEmote(32);  // 32 = 开心表情
```

### 标准对话窗口

```csharp
// 创建对话
var dialogue = new Dialogue("Hello farmer! How are you today?", npc);

// 添加到 NPC 对话队列
npc.CurrentDialogue.Push(dialogue);

// 如果玩家正在和 NPC 对话，显示对话窗口
Game1.drawDialogue(npc);
```

### 聊天框消息

```csharp
// 发送到游戏聊天框
Game1.chatBox?.addMessage(
    $"{npc.Name}: Hello farmer!",
    new Color(220, 220, 100)  // 黄色
);

// 玩家消息（蓝色）
Game1.chatBox?.addMessage(
    "You: Hello!",
    new Color(150, 220, 255)  // 浅蓝色
);
```

## 创建自定义对话菜单

### 基础聊天输入菜单

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

public class ChatInputMenu : IClickableMenu
{
    private readonly string _npcName;
    private readonly Action<string, string> _onSubmit;
    private readonly TextBox _textBox;
    private readonly ClickableTextureComponent _submitButton;

    private const int MenuWidth = 600;
    private const int MenuHeight = 200;

    public ChatInputMenu(string npcName, Action<string, string> onSubmit)
        : base(
            (Game1.uiViewport.Width - MenuWidth) / 2,
            (Game1.uiViewport.Height - MenuHeight) / 2,
            MenuWidth,
            MenuHeight,
            showUpperRightCloseButton: true
        )
    {
        _npcName = npcName;
        _onSubmit = onSubmit;

        // 创建文本输入框
        _textBox = new TextBox(
            textBoxTexture: Game1.content.Load<Texture2D>("LooseSprites\\textBox"),
            null,
            Game1.smallFont,
            Game1.textColor
        )
        {
            X = xPositionOnScreen + 32,
            Y = yPositionOnScreen + 96,
            Width = MenuWidth - 128,
            Text = ""
        };
        _textBox.Selected = true;

        // 提交按钮
        _submitButton = new ClickableTextureComponent(
            new Rectangle(
                xPositionOnScreen + MenuWidth - 96,
                yPositionOnScreen + 80,
                64, 64
            ),
            Game1.mouseCursors,
            new Rectangle(128, 256, 64, 64),  // OK 按钮精灵
            1f
        );

        // 捕获键盘输入
        Game1.keyboardDispatcher.Subscriber = _textBox;
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);

        if (_submitButton.containsPoint(x, y))
        {
            Submit();
            return;
        }

        _textBox.Update();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Enter)
        {
            Submit();
            return;
        }

        if (key == Keys.Escape)
        {
            exitThisMenu();
            return;
        }
    }

    private void Submit()
    {
        var text = _textBox.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            _onSubmit?.Invoke(_npcName, text);
        }
        exitThisMenu();
    }

    public override void draw(SpriteBatch b)
    {
        // 半透明背景
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

        // 菜单背景
        drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            xPositionOnScreen, yPositionOnScreen,
            width, height,
            Color.White
        );

        // 标题
        var title = $"Talk to {_npcName}";
        var titleSize = Game1.dialogueFont.MeasureString(title);
        b.DrawString(
            Game1.dialogueFont,
            title,
            new Vector2(xPositionOnScreen + (width - titleSize.X) / 2, yPositionOnScreen + 20),
            Game1.textColor
        );

        // 绘制输入框和按钮
        _textBox.Draw(b);
        _submitButton.draw(b);

        base.draw(b);
        drawMouse(b);
    }
}
```

### 打开菜单

```csharp
private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
{
    if (!Context.IsWorldReady || !Context.IsPlayerFree) return;
    if (!e.Button.IsActionButton()) return;

    // 找到光标处的 NPC
    var cursorTile = e.Cursor.GrabTile;
    var clickedNpc = Game1.currentLocation?.isCharacterAtTile(cursorTile);
    if (clickedNpc == null) return;

    // 抑制默认对话
    Helper.Input.Suppress(e.Button);

    // 打开自定义菜单
    Game1.activeClickableMenu = new ChatInputMenu(
        clickedNpc.Name,
        OnPlayerChatSubmit
    );
}

private void OnPlayerChatSubmit(string npcName, string message)
{
    // 显示玩家消息
    Game1.chatBox?.addMessage($"You: {message}", new Color(150, 220, 255));
    
    // 处理 NPC 回应...
}
```

## NPC 动作

### 表情

```csharp
// 表情 ID 列表
// 8 = 爱心, 12 = 生气, 16 = 音符, 20 = 问号
// 24 = 困倦, 28 = 沮丧, 32 = 开心, 36 = 惊讶

npc.doEmote(32);  // 开心
```

### 移动

```csharp
// 让 NPC 看向特定方向
// 0 = 上, 1 = 右, 2 = 下, 3 = 左
npc.faceDirection(2);  // 面向下方

// NPC 路径（需要完整的日程系统）
// 参考 Schedule_data 文档
```

### 动画

```csharp
// 开始特定动画（需要在 animationDescriptions 中定义）
npc.StartAnimation("abigail_videogames");
```

## 自定义对话系统（AI 集成）

### 对话上下文管理

```csharp
public class ConversationContext
{
    public string NpcName { get; set; }
    public List<DialogueEntry> History { get; } = new();
    public int DialogueRound { get; set; }
    
    public void AddPlayerMessage(string message)
    {
        History.Add(new DialogueEntry("Player", message, DateTime.Now));
    }
    
    public void AddNpcMessage(string message)
    {
        History.Add(new DialogueEntry(NpcName, message, DateTime.Now));
    }
    
    public string BuildPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are {NpcName} from Stardew Valley.");
        sb.AppendLine("Stay in character. Keep responses short and natural.");
        sb.AppendLine();
        
        foreach (var entry in History.TakeLast(10))  // 最近10条
        {
            sb.AppendLine($"{entry.Speaker}: {entry.Message}");
        }
        
        sb.AppendLine($"{NpcName}:");
        return sb.ToString();
    }
}

public record DialogueEntry(string Speaker, string Message, DateTime Timestamp);
```

### AI 响应集成

```csharp
public class AIConversationManager
{
    private readonly IMonitor _monitor;
    private readonly Dictionary<string, ConversationContext> _contexts = new();

    public async Task GenerateNpcResponse(string npcName, string playerMessage)
    {
        // 获取或创建上下文
        if (!_contexts.TryGetValue(npcName, out var context))
        {
            context = new ConversationContext { NpcName = npcName };
            _contexts[npcName] = context;
        }
        
        // 添加玩家消息
        context.AddPlayerMessage(playerMessage);
        
        try
        {
            // 调用 AI API（示例使用 DeepSeek）
            var response = await CallAIAPI(context.BuildPrompt());
            
            // 添加 NPC 回应到历史
            context.AddNpcMessage(response);
            context.DialogueRound++;
            
            // 显示回应
            DisplayNpcResponse(npcName, response);
        }
        catch (Exception ex)
        {
            _monitor.Log($"AI 生成失败: {ex.Message}", LogLevel.Error);
            DisplayNpcResponse(npcName, "...");
        }
    }
    
    private void DisplayNpcResponse(string npcName, string response)
    {
        var npc = Game1.getCharacterFromName(npcName);
        if (npc == null) return;
        
        // 截断气泡显示（太长会被截断）
        var bubbleText = response.Length > 60 
            ? response[..57] + "..." 
            : response;
        
        npc.showTextAboveHead(bubbleText, duration: 5000);
        
        // 完整消息在聊天框
        Game1.chatBox?.addMessage(
            $"{npcName}: {response}",
            new Color(220, 220, 100)
        );
        
        // 随机表情
        npc.doEmote(Game1.random.Next(32, 37));  // 32-36 是正面表情
    }
}
```

## NPC 数据访问

### 读取 NPC 数据

```csharp
// 获取 NPC 的基本信息
string npcName = npc.Name;
int age = npc.Age;  // 0 = 儿童, 1 = 少年, 2 = 青年, 3 = 成年
int manners = npc.Manners;  // 礼貌程度
int socialAnxiety = npc.SocialAnxiety;  // 社交焦虑
int optimism = npc.Optimism;  // 乐观程度
string gender = npc.Gender.ToString();  // Male/Female/Undefined

// 获取礼物喜好
string[] lovedGifts = npc.GetData()?.LovedGifts ?? Array.Empty<string>();
string[] likedGifts = npc.GetData()?.LikedGifts ?? Array.Empty<string>();
string[] hatedGifts = npc.GetData()?.HatedGifts ?? Array.Empty<string>();
```

### 修改 NPC 数据（临时）

```csharp
// 使用 ModData 存储自定义数据
npc.modData["LivingTown_LastConversation"] = DateTime.Now.ToString();
npc.modData["LivingTown_ConversationCount"] = "5";

// 读取
if (npc.modData.TryGetValue("LivingTown_LastConversation", out string value))
{
    DateTime lastConv = DateTime.Parse(value);
}
```

## 监听 NPC 互动

### 检测玩家与 NPC 对话

```csharp
// 方法1：使用 MenuChanged 检测对话菜单
helper.Events.Display.MenuChanged += (s, e) => {
    if (e.NewMenu is DialogueBox dialogue)
    {
        // 检查当前说话的 NPC
        if (Game1.currentSpeaker != null)
        {
            Monitor.Log($"开始与 {Game1.currentSpeaker.Name} 对话", LogLevel.Debug);
        }
    }
};
```

### 检测礼物赠送

```csharp
// 使用 ItemReceivedEvent（需要反射或 Harmony patch）
// 或者监听 InventoryChanged 和 Friendship 数据变化
```

## 最佳实践

### 1. 异步操作注意

```csharp
// ✗ 不要直接在 async 方法中修改游戏状态
private async void BadMethod()
{
    var response = await GetAIResponse();
    Game1.player.Money += 100;  // 可能崩溃！
}

// ✓ 正确做法：回到主线程
private async void GoodMethod()
{
    var response = await GetAIResponse();
    
    // 使用 UpdateTicked 或在下一个 tick 执行
    helper.Events.GameLoop.UpdateTicked += ExecuteOnMainThread;
}

private void ExecuteOnMainThread(object? sender, UpdateTickedEventArgs e)
{
    helper.Events.GameLoop.UpdateTicked -= ExecuteOnMainThread;
    Game1.player.Money += 100;  // 安全
}
```

### 2. 会话管理

```csharp
public class NpcSession
{
    public string NpcName { get; set; }
    public DateTime StartTime { get; set; }
    public CancellationTokenSource CancellationToken { get; } = new();
    
    public void EndSession()
    {
        CancellationToken.Cancel();
    }
}

// 管理活跃会话
private readonly Dictionary<string, NpcSession> _activeSessions = new();

public void StartConversation(string npcName)
{
    // 结束之前的会话
    if (_activeSessions.TryGetValue(npcName, out var oldSession))
    {
        oldSession.EndSession();
    }
    
    // 创建新会话
    var session = new NpcSession { NpcName = npcName, StartTime = DateTime.Now };
    _activeSessions[npcName] = session;
}
```

### 3. 错误恢复

```csharp
public async Task SafeNPCResponse(string npcName, string message)
{
    try
    {
        await GenerateResponse(npcName, message);
    }
    catch (OperationCanceledException)
    {
        // 正常取消，无需处理
    }
    catch (Exception ex)
    {
        Monitor.Log($"生成回应失败: {ex}", LogLevel.Error);
        
        // 显示默认回应
        var npc = Game1.getCharacterFromName(npcName);
        npc?.showTextAboveHead("...", duration: 2000);
    }
}
```

### 4. 性能优化

```csharp
// 缓存 NPC 引用
private readonly Dictionary<string, NPC> _npcCache = new();

private NPC? GetCachedNpc(string name)
{
    if (!_npcCache.TryGetValue(name, out var npc) || npc == null)
    {
        npc = Game1.getCharacterFromName(name);
        if (npc != null)
        {
            _npcCache[name] = npc;
        }
    }
    return npc;
}
```

## 参考

- [NPC 数据文档](https://stardewvalleywiki.com/Modding:NPC_data)
- [对话系统文档](https://stardewvalleywiki.com/Modding:Dialogue)
- [Schedule 数据](https://stardewvalleywiki.com/Modding:Schedule_data)
- [StardewValley.NPC 类](https://stardewvalleywiki.com/Modding:Modder_Guide/Game_Fundamentals)
