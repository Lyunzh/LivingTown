# 🏘️ LivingTown — AI-Powered NPC Engine for Stardew Valley

> Make NPCs feel *alive* — not scripted.

LivingTown is a SMAPI mod that replaces Stardew Valley's static NPC dialogue with a **hybrid neuro-symbolic AI architecture**. NPCs remember your conversations, break the fourth wall, get angry and walk away, and respond to your *intent* — not just your words.

## ✨ Key Features

### 🧠 Hybrid Architecture (Not Just "ChatGPT in a Game")
Most AI mods blindly pipe every player input to an LLM. LivingTown uses a **3-layer gatekeeper system** that reduces API calls by ~80%:

```
Player Input
  → L1: LexicalCache (regex short-circuit for "hi", "bye", "thanks" → 0ms)
  → L1: HeuristicWatchdog (entropy scoring → is this worth an API call?)
  → L3: ReACT Agent (full reasoning loop, only when it matters)
```

### 🎭 Soul-Driven Personalities
Each NPC has a structured `soul.json` containing:
- **Core Traits** & **Behavioral Boundaries** — Sebastian never acts cheerful
- **Fourth-Wall Hooks** — mention "coding" to Sebastian and he'll talk about recursion
- **Intent Recognition** — flirting with Shane at low friendship gets you told off; at high friendship, he blushes

### 🎯 GOAP Behavioral Engine
When the LLM decides an NPC should *do something* (not just *say something*), it sets a high-level goal via the `set_goal` tool. A **GOAP (Goal-Oriented Action Planning)** engine resolves this into a valid action sequence:

```
Agent: "Sebastian is angry → he should go to the mountain"
  → set_goal(CurrentLocation=Mountain, priority=high)
  → GOAP Planner: WalkTo_Mountain (cost=3)
  → NPC executes pathfinding
```

### 💾 Dual-Layer Memory
- **Short-term**: In-RAM event buffer with importance scoring
- **Long-term**: Persists to `NPC.modData` (survives save/load)
- **Active Memory**: The `remember` tool lets the LLM explicitly decide what's worth storing

## 🏗️ Architecture

```
src/
├── game/                     # Game integration layer
│   ├── ChatCoordinator.cs    # Central dialogue flow controller
│   └── ChatInputMenu.cs      # In-game text input UI
│
├── goap/                     # Goal-Oriented Action Planning
│   ├── Blackboard.cs         # Shared state dictionary (Agent ↔ Planner)
│   ├── GOAPAction.cs         # 11 pre-built atomic actions
│   └── GOAPPlanner.cs        # Forward Dijkstra search planner
│
├── llm/core/                 # ReACT Multi-Agent Engine
│   ├── AgentFactory.cs       # One-liner agent creation
│   ├── BuiltinTools.cs       # new_task (sub-agents), final_answer
│   ├── ChatMessage.cs        # OpenAI-compatible message format
│   ├── GameTools.cs           # set_goal, play_emote, remember, web_search
│   ├── PromptManager.cs      # Mode-based prompt templates
│   ├── ReActAgent.cs         # Core Reason-Act-Observe loop
│   └── ToolRegistry.cs       # Parallel tool execution + concurrency control
│
├── state/                    # State & Gatekeeper Layer
│   ├── GameStateTracker.cs   # Per-NPC daily interaction tracking
│   ├── HeuristicWatchdog.cs  # O(1) entropy-based escalation scoring
│   ├── LexicalCache.cs       # Regex cache for trivial inputs
│   ├── MemoryManager.cs      # Dual-layer memory system
│   └── SoulLoader.cs         # Loads soul.json → prompt injection

assets/souls/                 # NPC personality definitions (6 NPCs)
├── Sebastian.json            # Introvert programmer, motorcycle, frogs
├── Abigail.json              # Hair mystery, Wizard theory, eats rocks
├── Haley.json                # Photography depth, Fibonacci sunflowers
├── Sam.json                  # Band dreams, Kent's PTSD, JojaMart humor
├── Shane.json                # Depression arc, chickens, cliff scene
└── Emily.json                # Crystals, dream analysis, Clint obliviousness
```

## 🔧 Tech Stack

| Component | Technology |
|:---|:---|
| Language | C# (.NET 6.0) |
| Modding API | SMAPI (Stardew Valley Modding API) |
| LLM | DeepSeek (OpenAI-compatible API) |
| Architecture | Hybrid Neuro-Symbolic |
| Planning | GOAP with Dijkstra search |
| Concurrency | async/await, ConcurrentQueue, SemaphoreSlim |

## 🚀 Quick Start

### Prerequisites
- [Stardew Valley](https://www.stardewvalley.net/) (v1.6+)
- [SMAPI](https://smapi.io/) (v4.0+)
- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)

### Setup
```bash
# Clone
git clone https://github.com/yourusername/LivingTown.git
cd LivingTown

# Create .env in LivingTown/ directory
echo "DEEPSEEK_API_KEY=your_key_here" > LivingTown/.env

# Build
dotnet build LivingTown.sln

# The mod DLL is output to LivingTown/bin/Debug/net6.0/
# Copy or symlink this folder to your SMAPI Mods directory
```

### In-Game Usage
1. Launch Stardew Valley via SMAPI
2. Walk up to any NPC with a soul file (Sebastian, Abigail, Haley, Sam, Shane, Emily)
3. Press **C** to open the chat input
4. Type anything — the system decides whether to use cache, heuristics, or the full AI

## 🧪 Testing

### Manual Test Path (~15 min)

**Phase 1 — Cache Short-Circuit**
1. Talk to Sebastian → type `hi` → expect instant reply, no API call
2. Type `bye` → instant farewell

**Phase 2 — Watchdog Escalation**
3. Type `你最近在写什么代码？我也是程序员` (>30 chars) → triggers `Dialogue_Complex` (+15 entropy)
4. Verify Sebastian responds with coding-related fourth-wall content

**Phase 3 — Cross-NPC Personality**
5. Ask Abigail `你的头发是天然紫色的吗` → expect hair mystery response
6. Ask Shane `Charlie 还好吗` → expect personality shift to warmth
7. Ask Haley `你觉得你很肤浅吗` → expect fierce comeback

**Phase 4 — GOAP Verification**
8. Check SMAPI console for `[Planner] ✅ Plan found!` after Agent calls `set_goal`

**Phase 5 — Day Lifecycle**
9. Sleep → verify `Daily states reset` + `All entropy pools reset` in console

## 📐 Design Decisions

| Decision | Rationale |
|:---|:---|
| **Watchdog before LLM** | ~80% of player inputs are trivial greetings. Don't waste API calls. |
| **GOAP over direct LLM control** | LLMs hallucinate. Let them set *goals*, not *pathfind*. |
| **Soul files as static JSON** | Generated offline via PersonaBuilder. Zero runtime overhead. |
| **SemaphoreSlim(2)** | Limit concurrent LLM calls to prevent API rate limiting. |
| **ConcurrentQueue for display** | SMAPI is single-threaded. Background LLM results marshal back via main-thread queue. |
| **Entropy resets daily** | Prevents snowball escalation. Each day is a fresh emotional slate. |

## 📄 License

MIT

## 🙏 Acknowledgments

- [ConcernedApe](https://www.stardewvalley.net/) — for creating Stardew Valley
- [SMAPI](https://smapi.io/) — the modding framework that makes this possible
- [DeepSeek](https://www.deepseek.com/) — LLM API provider
- [OpenClaw](https://github.com/AkakiAlice/OpenClaw) — inspiration for the memory architecture
