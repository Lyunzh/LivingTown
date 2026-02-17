# LivingTown Agent Development Guide

C# SMAPI mod for Stardew Valley adding AI-powered NPC conversations via LLM integration.

## Build Commands

```bash
# Build entire solution
dotnet build LivingTown.sln

# Build in Release mode
dotnet build LivingTown.sln -c Release

# Clean build artifacts
dotnet clean LivingTown.sln

# Restore NuGet packages
dotnet restore LivingTown.sln
```

**Note**: No test project currently configured. Tests should be added to a `LivingTown.Tests` project using xUnit/NUnit.

## Project Structure

```
LivingTown/
├── ModEntry.cs           # SMAPI entry point, event handlers
├── manifest.json         # SMAPI mod metadata
├── LivingTown.csproj     # Project file (targets net10.0)
└── src/
    ├── pipeline/         # Core message routing
    │   ├── pipeline.cs   # Main pipeline orchestrator
    │   ├── agent.cs      # IAgent interface, Session, Endpoint
    │   └── Messages.cs   # Message types (GameMsg, NpcMsg, LLMMsg)
    ├── game/             # SMAPI game integration
    │   ├── agent.cs      # GameAgent: SMAPI events → Pipeline
    │   ├── client.cs     # IGameClient interface
    │   └── ChatInputMenu.cs  # Custom UI for player input
    ├── npc/              # NPC behavior logic
    │   ├── agent.cs      # NpcAgent: NPC decision making
    │   └── client.cs     # INpcClient interface
    └── llm/              # LLM integration
        ├── agent.cs      # LLMAgent: Async LLM calls
        └── client.cs     # DeepSeek API client
```

## Architecture Overview

### Multi-Agent Pipeline Pattern

The mod uses a **channel-based pipeline architecture** inspired by Go's goroutines:

1. **Session**: Per-NPC conversation context (Guid Id, CancellationToken)
2. **Pipeline**: Central router managing flow between agents using `Task.WhenAny`
3. **Agents**: Implement `IAgent` interface
   - `Game.Agent`: Bridges SMAPI events → Pipeline
   - `Npc.Agent`: NPC personality & decision logic
   - `LLM.Agent`: Async HTTP calls to DeepSeek API
4. **Messages**: Immutable records in `Messages.cs`
   - `GameMsg.PlayerChat`, `TimeChange`, `LocationChange`
   - `NpcMsg.Speak`, `Move`, `Emote`, `RequestLLM`
   - `LLMMsg.Request`, `Response`

### Data Flow

```
Player Input → ChatInputMenu → GameAgent → Pipeline → NpcAgent → LLMAgent
                                                                    ↓
NPC Response ← GameAgent ← Pipeline ← NpcAgent ← LLMMsg.Response ← API
```

## Code Style Guidelines

### C# Conventions

- **Framework**: .NET 10, C# 10 with nullable reference types enabled
- **Naming**: PascalCase for types/methods, camelCase for locals, _camelCase for private fields
- **Imports**: Group System.* first, then third-party, then project. Use file-scoped namespaces.
- **Types**: Prefer `record` for DTOs (messages), `class` for services/agents
- **Async**: Always use `Async` suffix for async methods. Use `CancellationToken` throughout.

### Key Patterns

```csharp
// Channel-based communication (Go-like select)
var gameTask = endpoints.Game.Out.ReadAsync(ct).AsTask();
var llmTask = endpoints.LLM.Out.ReadAsync(ct).AsTask();
var completed = await Task.WhenAny(gameTask, llmTask);

// Thread-safe action queue for main thread
private readonly ConcurrentQueue<object> _pendingActions = new();
public IEnumerable<object> PollActions() { /* called from ModEntry.OnUpdateTicked */ }

// Defensive null checks with early returns
var npc = Game1.getCharacterFromName(NpcName);
if (npc == null) return;
```

### Error Handling

- Use `try/catch` around LLM calls and channel operations
- Log with `IMonitor.Log("[Component] Message", LogLevel.Error)`
- Graceful degradation: Return fallback messages on LLM failure
- Always handle `OperationCanceledException` for clean shutdown

### SMAPI Integration

```csharp
// Event subscription in ModEntry.Entry()
helper.Events.GameLoop.GameLaunched += OnGameLaunched;
helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
helper.Events.Input.ButtonPressed += OnButtonPressed;

// Check world state before operations
if (!Context.IsWorldReady) return;
if (!Context.IsPlayerFree) return;

// Suppress default game behavior
Helper.Input.Suppress(e.Button);
```

## Configuration

API keys stored in `.env` file at repo root:
```
DEEPSEEK_API_KEY=your_key_here
DEEPSEEK_BASE_URL=https://api.deepseek.com
```

Loaded via `LLMClient.LoadConfig()` using simple line parsing.

## Development Guidelines

### Adding New Message Types

1. Add record to appropriate `Messages.cs` section
2. Add handler case in `Pipeline.On*Message()` methods
3. Update relevant agent's message loop

### Adding New Agents

1. Implement `IAgent` interface
2. Return `Endpoint` with Reader/Writer channels
3. Handle `SessionGone()` for cleanup
4. Register in `ModEntry.Entry()`

### Thread Safety

- **Background threads**: OK for HTTP calls, file I/O, channel operations
- **Main thread ONLY**: All Stardew Valley API calls (NPC manipulation, UI)
- Use `ConcurrentDictionary` and `ConcurrentQueue` for shared state

### Memory Management

- Sessions auto-terminate on cancellation
- Agents clean up resources in `SessionGone()`
- Use `Channel.CreateUnbounded<object>()` for message queues

## Key Documentation References

See `docs/` folder:
- `Agent_Memory_Architecture.md`: OpenClaw memory pattern adaptation
- `Network.md`: PelicanNet social/economic simulation design
- `GOAP.md`: Stanford Smallville generative agents research

## SMAPI Event Lifecycle

- `GameLaunched`: Initialize agents and pipeline
- `SaveLoaded`: Restore per-save session data
- `DayStarted`: Daily NPC behavior reset
- `DayEnding`: Memory compression, reflection
- `UpdateTicked` (60fps): Poll and execute pending actions

## Dependencies

- `Microsoft.Extensions.AI.OpenAI` (10.3.0): OpenAI-compatible LLM client
- `Newtonsoft.Json` (13.0.4): JSON serialization
- `Pathoschild.Stardew.ModBuildConfig` (4.4.0): SMAPI build configuration
