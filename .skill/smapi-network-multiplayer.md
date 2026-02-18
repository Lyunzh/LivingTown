# SMAPI 多人游戏与网络通信技巧

针对 LivingTown 模组的多人同步与网络功能实现

## 多人游戏基础

### 检查多人游戏状态

```csharp
// 检查是否是多人游戏
if (Context.IsMultiplayer)
{
    // 多人游戏特定逻辑
}

// 检查是否是主机（服务器）
if (Context.IsMainPlayer)
{
    // 只有主机执行的代码，如全局状态管理
}

// 获取当前玩家ID
long myPlayerId = Game1.player.UniqueMultiplayerID;
```

### 获取连接的玩家

```csharp
// 获取所有连接的玩家（包括自己）
foreach (IMultiplayerPeer peer in helper.Multiplayer.GetConnectedPlayers())
{
    long playerId = peer.PlayerID;
    bool isHost = peer.IsHost;
    bool hasSmaPI = peer.HasSmapi;
    
    // 检查玩家是否加载了特定模组
    IMultiplayerPeerMod? modInfo = peer.GetMod("YourMod.UniqueID");
    if (modInfo != null)
    {
        // 玩家有此模组，版本：modInfo.Version
    }
}
```

## 模组消息传递

### 发送消息

```csharp
// 广播给所有玩家
helper.Multiplayer.SendMessage(
    message: myData,
    messageType: "ChatMessage",
    modIDs: new[] { this.ModManifest.UniqueID }
);

// 发送给特定玩家
helper.Multiplayer.SendMessage(
    message: myData,
    messageType: "PrivateMessage",
    modIDs: new[] { this.ModManifest.UniqueID },
    playerIDs: new[] { targetPlayerId }
);

// 发送给除自己外的所有玩家
var otherPlayers = helper.Multiplayer.GetConnectedPlayers()
    .Where(p => p.PlayerID != Game1.player.UniqueMultiplayerID)
    .Select(p => p.PlayerID)
    .ToArray();

helper.Multiplayer.SendMessage(
    message: myData,
    messageType: "Broadcast",
    modIDs: new[] { this.ModManifest.UniqueID },
    playerIDs: otherPlayers
);
```

### 接收消息

```csharp
// 订阅消息接收事件
helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;

private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
{
    // 验证消息来源
    if (e.FromModID != this.ModManifest.UniqueID) return;
    
    switch (e.Type)
    {
        case "ChatMessage":
            var chatData = e.ReadAs<ChatMessageData>();
            HandleChatMessage(chatData);
            break;
            
        case "NpcStateSync":
            var npcState = e.ReadAs<NpcStateData>();
            SyncNpcState(npcState);
            break;
    }
}

// 消息数据类（需要可序列化）
public class ChatMessageData
{
    public string NpcName { get; set; } = "";
    public string Message { get; set; } = "";
    public long SenderId { get; set; }
    public DateTime Timestamp { get; set; }
}
```

## 分屏支持

### PerScreen 模式

```csharp
// 为每个分屏玩家维护独立数据
private readonly PerScreen<int> PlayerScore = new PerScreen<int>();
private readonly PerScreen<ConversationContext> PlayerConversation = new PerScreen<ConversationContext>();

// 访问当前屏幕的值
public void AddScore(int points)
{
    this.PlayerScore.Value += points;
}

public int GetScore()
{
    return this.PlayerScore.Value;
}

// 检查是否是本地玩家
public bool IsLocalPlayer(long playerId)
{
    return playerId == Game1.player.UniqueMultiplayerID;
}
```

## 连接事件

```csharp
// 玩家连接时
helper.Events.Multiplayer.PeerConnected += (s, e) => {
    Monitor.Log($"玩家 {e.Peer.PlayerID} 已连接", LogLevel.Info);
    
    // 发送当前状态给新玩家（如果是主机）
    if (Context.IsMainPlayer)
    {
        SendFullStateToPlayer(e.Peer.PlayerID);
    }
};

// 玩家断开时
helper.Events.Multiplayer.PeerDisconnected += (s, e) => {
    Monitor.Log($"玩家 {e.Peer.PlayerID} 已断开", LogLevel.Info);
    
    // 清理该玩家的相关数据
    CleanupPlayerData(e.Peer.PlayerID);
};

// 最早可以发送消息的时机
helper.Events.Multiplayer.PeerContextReceived += (s, e) => {
    // 此时可以开始向该玩家发送消息
};
```

## LivingTown 网络同步模式

### NPC 状态同步

```csharp
public class NpcNetworkSync
{
    private readonly IModHelper _helper;
    private readonly Dictionary<string, NpcState> _localStates = new();
    
    // 主机广播NPC状态变化
    public void BroadcastNpcState(string npcName, NpcState state)
    {
        if (!Context.IsMainPlayer) return;
        
        _helper.Multiplayer.SendMessage(
            new NpcStateData { NpcName = npcName, State = state },
            "NpcStateUpdate",
            modIDs: new[] { _helper.ModRegistry.ModID }
        );
    }
    
    // 客户端接收状态
    public void ReceiveNpcState(NpcStateData data)
    {
        if (Context.IsMainPlayer) return; // 主机不需要接收
        
        _localStates[data.NpcName] = data.State;
        ApplyStateToNpc(data.NpcName, data.State);
    }
}

public class NpcStateData
{
    public string NpcName { get; set; } = "";
    public NpcState State { get; set; } = new();
}

public class NpcState
{
    public Vector2 Position { get; set; }
    public int FacingDirection { get; set; }
    public string CurrentDialogue { get; set; } = "";
    public Dictionary<string, string> ModData { get; set; } = new();
}
```

### 社交图谱传播

```csharp
public class SocialGraphSync
{
    // 主机计算并广播社交更新
    public void PropagateGossip(string sourceNpc, string targetNpc, string topic)
    {
        if (!Context.IsMainPlayer) return;
        
        var gossip = new GossipData
        {
            SourceNpc = sourceNpc,
            TargetNpc = targetNpc,
            Topic = topic,
            SpreadRadius = CalculateSpreadRadius(sourceNpc),
            Timestamp = DateTime.Now
        };
        
        _helper.Multiplayer.SendMessage(gossip, "GossipUpdate");
    }
}

public class GossipData
{
    public string SourceNpc { get; set; } = "";
    public string TargetNpc { get; set; } = "";
    public string Topic { get; set; } = "";
    public int SpreadRadius { get; set; }
    public DateTime Timestamp { get; set; }
}
```

## 最佳实践

### 1. 减少网络流量

```csharp
// ✗ 不要每帧发送
helper.Events.GameLoop.UpdateTicked += (s, e) => {
    SendFullState();  // 太频繁！
};

// ✓ 只在变化时发送
private NpcState _lastSentState;

public void UpdateNpcState(NpcState newState)
{
    if (!StatesEqual(_lastSentState, newState)) return;
    
    BroadcastNpcState(newState);
    _lastSentState = newState;
}
```

### 2. 数据压缩

```csharp
// 只发送必要的数据
public class CompactNpcUpdate
{
    public string NpcName { get; set; } = "";  // 必须
    public Vector2? Position { get; set; }       // 可选
    public int? FacingDirection { get; set; }    // 可选
    public string? CurrentEmote { get; set; }    // 可选
}
```

### 3. 错误处理

```csharp
private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
{
    try
    {
        var data = e.ReadAs<MyMessageType>();
        ProcessMessage(data);
    }
    catch (Exception ex)
    {
        Monitor.Log($"处理消息失败: {ex.Message}", LogLevel.Error);
        // 不要抛出异常，避免破坏消息处理链
    }
}
```

### 4. 版本兼容性

```csharp
// 发送带版本号的消息
public class VersionedMessage
{
    public string Version { get; set; } = "1.0.0";
    public object Payload { get; set; }
}

// 接收时检查版本
private void OnMessageReceived(ModMessageReceivedEventArgs e)
{
    var message = e.ReadAs<VersionedMessage>();
    if (!IsVersionCompatible(message.Version))
    {
        Monitor.Log($"收到不兼容版本的消息: {message.Version}", LogLevel.Warn);
        return;
    }
}
```

## 调试技巧

```csharp
// 启用详细日志
helper.Events.Multiplayer.ModMessageReceived += (s, e) => {
    Monitor.Log($"[网络] 收到 {e.Type} 来自 {e.FromPlayerID}", LogLevel.Trace);
};

// 统计网络流量
private int _messagesSent = 0;
private int _bytesSent = 0;

public void LogNetworkStats()
{
    Monitor.Log($"网络统计: {_messagesSent} 条消息, {_bytesSent} 字节", LogLevel.Debug);
}
```

## 参考

- [多人游戏API文档](https://stardewvalleywiki.com/Modding:Modder_Guide/APIs/Multiplayer)
- [SMAPI官方示例](https://github.com/Pathoschild/SMAPI/tree/develop/src/SMAPI.Tests)
