# SMAPI 手机与邮件系统技巧

针对 LivingTown 模组的鹈鹕通手机UI和程序化邮件系统

## 邮件系统

### 基础邮件发送

```csharp
// 立即发送邮件（玩家会立即收到通知）
Game1.player.mailbox.Add("MyMod_MailId");

// 次日发送邮件
Game1.addMailForTomorrow("MyMod_MailId");

// 检查玩家是否已收到或即将收到邮件
if (Game1.player.hasOrWillReceiveMail("MyMod_MailId"))
{
    // 避免重复发送
}

// 标记为已读（如果需要）
Game1.player.mailReceived.Add("MyMod_MailId");
```

### 动态添加邮件内容

```csharp
// 在 AssetRequested 事件中注入邮件内容
helper.Events.Content.AssetRequested += OnAssetRequested;

private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
{
    if (e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
    {
        e.Edit(asset =>
        {
            var data = asset.AsDictionary<string, string>().Data;
            
            // 添加新邮件
            data["LivingTown_Welcome"] = "欢迎来到LivingTown！^这里是一个充满活力的世界，NPC们有自己的生活和故事。^ ^祝你好运！^^   - LivingTown团队";
            
            // 带条件的邮件
            data["LivingTown_Gossip"] = "听说Abigail最近在练习剑术...^也许你可以去矿山附近找她聊聊？^^   - 一个关心你的朋友";
            
            // 带奖励的邮件（使用 %item 命令）
            data["LivingTown_Reward"] = "感谢你的帮助！^请收下这份礼物。^^   - Pierre%%[object 74 5]";
            // %[object ID 数量] - 赠送物品
            // %[money 数量] - 赠送金钱
        });
    }
}
```

### 程序化构建邮件

```csharp
public class MailBuilder
{
    private readonly StringBuilder _content = new();
    private readonly List<string> _rewards = new();
    
    public MailBuilder AddLine(string text)
    {
        _content.AppendLine(text);
        return this;
    }
    
    public MailBuilder AddBreak()
    {
        _content.AppendLine("^");  // 游戏内换行符
        return this;
    }
    
    public MailBuilder AddReward(int objectId, int count)
    {
        _rewards.Add($"[object {objectId} {count}]");
        return this;
    }
    
    public MailBuilder AddMoney(int amount)
    {
        _rewards.Add($"[money {amount}]");
        return this;
    }
    
    public string Build()
    {
        var result = _content.ToString();
        if (_rewards.Any())
        {
            result += "%" + string.Join(" ", _rewards);
        }
        return result;
    }
}

// 使用示例
var mailContent = new MailBuilder()
    .AddLine("亲爱的玩家，")
    .AddBreak()
    .AddLine("感谢你昨天和Abigail的对话。")
    .AddLine("她看起来很开心！")
    .AddBreak()
    .AddLine("送上一份小礼物。")
    .AddReward(74, 3)  // 3个普里西玛水晶
    .Build();

// 注册邮件
RegisterMail("LivingTown_Dynamic_001", mailContent);
```

### 邮件命令参考

```csharp
// %item 命令格式
"%[object <ID> <数量>]"          // 赠送物品
"%[money <数量>]"                // 赠送金钱
"%[quest <ID>]"                  // 添加任务
"%[recipe <名称>]"               // 教授食谱
"%[cooking <名称>]"              // 教授烹饪配方
"%[bigcraftable <ID>]"           // 赠送大型可制作物品

// 特殊字符
"^"  // 换行
"^^" // 段落分隔（显示署名区域）
"@"  // 玩家名字占位符
```

## 手机系统

### 来电系统

```csharp
// 在 AssetRequested 中注册来电
helper.Events.Content.AssetRequested += OnAssetRequested;

private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
{
    if (e.NameWithoutLocale.IsEquivalentTo("Data/IncomingPhoneCalls"))
    {
        e.Edit(asset =>
        {
            var data = asset.AsDictionary<string, IncomingPhoneCallData>().Data;
            
            data["LivingTown_NpcCall"] = new IncomingPhoneCallData
            {
                Dialogue = "嘿，是我！你有空来农场一趟吗？",
                FromNpc = "Abigail",
                FromPortrait = "Portraits/Abigail",  // 可选：自定义头像
                MaxCalls = 1,  // 最多响铃次数
                TriggerCondition = "PLAYER_FRIENDSHIP_LEVEL Current Abigail 4"  // 触发条件
            };
        });
    }
}
```

### 自定义电话处理器

```csharp
// 实现 IPhoneHandler 接口
public class LivingTownPhoneHandler : IPhoneHandler
{
    public bool TryHandlePhoneCall(NPC? speaker, out string? dialogueKey)
    {
        dialogueKey = null;
        
        // 检查是否是LivingTown的NPC来电
        if (speaker?.Name == "Abigail" && ShouldTriggerSpecialDialogue())
        {
            dialogueKey = "LivingTown_SpecialCall";
            return true;
        }
        
        return false;
    }
    
    private bool ShouldTriggerSpecialDialogue()
    {
        // 自定义逻辑
        return Game1.timeOfDay > 1800;  // 晚上6点后
    }
}

// 注册处理器
helper.Events.GameLoop.GameLaunched += (s, e) => {
    Phone.PhoneHandlers.Add(new LivingTownPhoneHandler());
};
```

### 鹈鹕通手机UI框架

```csharp
// 自定义手机菜单
public class PelicanPhoneMenu : IClickableMenu
{
    private readonly List<PhoneApp> _apps = new();
    private PhoneApp? _currentApp;
    private readonly ClickableTextureComponent _homeButton;
    
    public PelicanPhoneMenu() : base(
        (Game1.uiViewport.Width - 400) / 2,
        (Game1.uiViewport.Height - 700) / 2,
        400, 700,
        showUpperRightCloseButton: true
    )
    {
        // 初始化应用
        _apps.Add(new TownFeedApp());
        _apps.Add(new WhisperApp());
        _apps.Add(new MarketWatchApp());
        
        // Home按钮
        _homeButton = new ClickableTextureComponent(
            new Rectangle(xPositionOnScreen + 20, yPositionOnScreen + height - 60, 40, 40),
            Game1.mouseCursors,
            new Rectangle(576, 96, 16, 16),  // Home图标
            2.5f
        );
    }
    
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);
        
        if (_homeButton.containsPoint(x, y))
        {
            _currentApp = null;  // 返回主屏幕
            Game1.playSound("select");
            return;
        }
        
        // 检查应用图标点击
        if (_currentApp == null)
        {
            for (int i = 0; i < _apps.Count; i++)
            {
                var appBounds = GetAppIconBounds(i);
                if (appBounds.Contains(x, y))
                {
                    _currentApp = _apps[i];
                    _currentApp.OnOpen();
                    Game1.playSound("select");
                    return;
                }
            }
        }
        else
        {
            _currentApp.ReceiveLeftClick(x, y);
        }
    }
    
    public override void draw(SpriteBatch b)
    {
        // 手机外壳
        DrawPhoneFrame(b);
        
        // 屏幕内容
        if (_currentApp == null)
        {
            DrawHomeScreen(b);
        }
        else
        {
            _currentApp.Draw(b, xPositionOnScreen + 20, yPositionOnScreen + 80, width - 40, height - 140);
        }
        
        // Home按钮
        _homeButton.draw(b);
        
        base.draw(b);
        drawMouse(b);
    }
    
    private void DrawPhoneFrame(SpriteBatch b)
    {
        // 绘制手机边框和屏幕区域
        IClickableMenu.drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            xPositionOnScreen, yPositionOnScreen,
            width, height,
            Color.DarkSlateGray
        );
        
        // 屏幕背景
        b.Draw(
            Game1.fadeToBlackRect,
            new Rectangle(xPositionOnScreen + 15, yPositionOnScreen + 60, width - 30, height - 80),
            Color.White
        );
    }
    
    private void DrawHomeScreen(SpriteBatch b)
    {
        // 绘制应用图标网格
        for (int i = 0; i < _apps.Count; i++)
        {
            var bounds = GetAppIconBounds(i);
            var app = _apps[i];
            
            // 图标背景
            b.Draw(
                Game1.fadeToBlackRect,
                bounds,
                app.IconColor
            );
            
            // 图标纹理
            b.Draw(
                app.IconTexture,
                new Vector2(bounds.X + 10, bounds.Y + 10),
                app.IconSourceRect,
                Color.White,
                0f,
                Vector2.Zero,
                2f,
                SpriteEffects.None,
                0f
            );
            
            // 应用名称
            var nameSize = Game1.smallFont.MeasureString(app.Name);
            b.DrawString(
                Game1.smallFont,
                app.Name,
                new Vector2(bounds.X + (bounds.Width - nameSize.X) / 2, bounds.Y + bounds.Height + 5),
                Game1.textColor
            );
        }
    }
}

// 应用基类
public abstract class PhoneApp
{
    public abstract string Name { get; }
    public abstract Texture2D IconTexture { get; }
    public abstract Rectangle IconSourceRect { get; }
    public abstract Color IconColor { get; }
    
    public virtual void OnOpen() { }
    public virtual void ReceiveLeftClick(int x, int y) { }
    public virtual void Draw(SpriteBatch b, int x, int y, int width, int height) { }
}
```

### TownFeed 应用示例

```csharp
public class TownFeedApp : PhoneApp
{
    private readonly List<FeedEntry> _entries = new();
    private int _scrollOffset = 0;
    
    public override string Name => "TownFeed";
    public override Texture2D IconTexture => Game1.mouseCursors;
    public override Rectangle IconSourceRect => new Rectangle(16, 368, 16, 16);
    public override Color IconColor => Color.CornflowerBlue;
    
    public override void OnOpen()
    {
        RefreshFeed();
    }
    
    public override void Draw(SpriteBatch b, int x, int y, int width, int height)
    {
        // 标题栏
        b.DrawString(
            Game1.dialogueFont,
            "Town Feed",
            new Vector2(x + 10, y + 10),
            Game1.textColor
        );
        
        // 动态内容
        int entryY = y + 50;
        for (int i = _scrollOffset; i < Math.Min(_entries.Count, _scrollOffset + 5); i++)
        {
            DrawFeedEntry(b, _entries[i], x + 10, entryY, width - 20);
            entryY += 80;
        }
    }
    
    private void DrawFeedEntry(SpriteBatch b, FeedEntry entry, int x, int y, int width)
    {
        // 背景
        b.Draw(Game1.fadeToBlackRect, new Rectangle(x, y, width, 70), Color.LightGray * 0.3f);
        
        // NPC头像
        var portrait = Game1.content.Load<Texture2D>($"Portraits/{entry.NpcName}");
        b.Draw(portrait, new Vector2(x + 5, y + 5), new Rectangle(0, 0, 64, 64), Color.White, 0f, Vector2.Zero, 0.8f, SpriteEffects.None, 0f);
        
        // 内容
        b.DrawString(Game1.smallFont, $"{entry.NpcName}:", new Vector2(x + 60, y + 5), Game1.textColor);
        b.DrawString(Game1.smallFont, entry.Message, new Vector2(x + 60, y + 25), Game1.textColor * 0.8f);
        b.DrawString(Game1.smallFont, entry.Timestamp.ToString("HH:mm"), new Vector2(x + width - 50, y + 50), Game1.textColor * 0.5f);
    }
    
    private void RefreshFeed()
    {
        // 从ModData或内存加载动态
        _entries.Clear();
        // ...
    }
}

public class FeedEntry
{
    public string NpcName { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
```

## 集成 LivingTown 功能

### 根据对话动态发送邮件

```csharp
public async Task OnNpcConversationEnd(string npcName, ConversationSummary summary)
{
    // 根据对话内容生成邮件
    if (summary.Sentiment == Sentiment.Positive && summary.Topics.Contains("gift"))
    {
        var mailId = $"LivingTown_Thanks_{npcName}_{Game1.Date.TotalDays}";
        
        var content = new MailBuilder()
            .AddLine($"亲爱的 {Game1.player.Name}，")
            .AddBreak()
            .AddLine($"昨天和你聊天很愉快！")
            .AddLine($"这是我的一点心意。")
            .AddBreak()
            .AddLine($"    - {npcName}")
            .AddReward(GetNpcFavoriteGift(npcName), 1)
            .Build();
        
        RegisterMail(mailId, content);
        Game1.addMailForTomorrow(mailId);
    }
}
```

### NPC 主动打电话

```csharp
public void TriggerNpcPhoneCall(string npcName, string dialogueKey)
{
    // 检查玩家是否在线
    if (!Context.IsWorldReady) return;
    
    // 检查时间（不能太晚）
    if (Game1.timeOfDay < 900 || Game1.timeOfDay > 2100) return;
    
    // 检查玩家是否忙碌
    if (!Context.IsPlayerFree) 
    {
        // 延迟到玩家空闲时
        SchedulePhoneCall(npcName, dialogueKey, delayMinutes: 10);
        return;
    }
    
    // 触发电话
    var npc = Game1.getCharacterFromName(npcName);
    if (npc != null)
    {
        Phone.Call(npc, dialogueKey);
    }
}
```

## 最佳实践

### 1. 避免邮件轰炸

```csharp
private readonly HashSet<string> _sentMailsToday = new();

private void OnDayStarted(object? sender, DayStartedEventArgs e)
{
    _sentMailsToday.Clear();
}

public void SendMailSafely(string mailId)
{
    if (_sentMailsToday.Contains(mailId)) return;
    if (Game1.player.hasOrWillReceiveMail(mailId)) return;
    
    Game1.addMailForTomorrow(mailId);
    _sentMailsToday.Add(mailId);
}
```

### 2. 本地化支持

```csharp
// 为不同语言提供邮件内容
private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
{
    if (e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
    {
        string locale = e.Name.LocaleCode;
        
        e.Edit(asset =>
        {
            var data = asset.AsDictionary<string, string>().Data;
            
            switch (locale)
            {
                case "zh":
                    data["LivingTown_Welcome"] = "欢迎来到LivingTown！...";
                    break;
                case "ja":
                    data["LivingTown_Welcome"] = "LivingTownへようこそ！...";
                    break;
                default:
                    data["LivingTown_Welcome"] = "Welcome to LivingTown!...";
                    break;
            }
        });
    }
}
```

### 3. 条件性内容

```csharp
// 使用游戏状态查询
"TriggerCondition": "PLAYER_FRIENDSHIP_LEVEL Current Abigail 4, WEATHER Target Rainy"

// 在代码中检查
public bool ShouldSendMail(string mailId)
{
    // 检查友谊等级
    if (Game1.player.getFriendshipLevelForNPC("Abigail") < 1000) return false;
    
    // 检查天气
    if (!Game1.isRaining) return false;
    
    // 检查时间
    if (Game1.year < 2) return false;
    
    return true;
}
```

## 参考

- [邮件系统文档](https://stardewvalleywiki.com/Modding:Mail)
- [电话系统文档](https://stardewvalleywiki.com/Modding:Phone)
- [游戏状态查询](https://stardewvalleywiki.com/Modding:Game_state_queries)
