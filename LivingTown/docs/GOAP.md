# 基于 GOAP 的 NPC 行为树重构与状态机设计

此前的文档中充斥着“用 LLM 写坐标代码给 NPC 寻路”的危险幻想，在此我们彻底拨乱反正：**让 LLM 负责出主意（目标层），让 GOAP 负责干苦力（执行层）。**

**目标导向行动规划（GOAP, Goal-Oriented Action Planning）** 是一个逆向推导的 AI 决策模型。在 PelicanNet 中，GOAP 必须通过严谨的 C# 逻辑进行实现，绝不允许大模型直接插手每一帧的路径演算。

---

## 1. GOAP 的基座：状态黑板 (The Blackboard)

要让 GOAP 运作，游戏内存中必须有一块 NPC 和系统共享的“黑板”。这块黑板记录了当前世界的量化快照。在 C# 中，这通常是一个 `Dictionary<string, object>`。

### 1.1 状态定义示例
GOAP 规划器只能理解这些数字和布尔值：
- `IsHungry` = `true` / `false`
- `HasItem_Salad` = `true` / `false`
- `TimeOfDay` = `1200`
- `PlayerProximity` = `Nearby` / `Far`
- `Weather` = `"Rain"` / `"Sunny"`

---

## 2. 原子动作库设计 (Action Library)

我们在 C# 中预先编写几十种绝对安全、不会报错的基层动作 (Atoms)。每一个 Action 都必须严格定义三要素：**前置条件 (Preconditions)**、**后置影响 (Effects)**、**成本估算 (Cost/Heuristics)**。

### 2.1 动作示例：`Action_EatSalad`
```csharp
public class Action_EatSalad : GOAPAction {
    public Action_EatSalad() {
        this.Name = "Eat Salad";
        
        // 【必须满足】包里得有沙拉，且必须在 saloon 或者自己家里
        this.Preconditions.Add("HasItem_Salad", true);
        
        // 【执行后果】吃完就不饿了
        this.Effects.Add("IsHungry", false);
        
        // 【默认成本】
        this.Cost = 5f; 
    }
    
    public override bool Execute(NPC actor) {
        actor.playEatAnimation("Salad");
        actor.Inventory.Remove("Salad");
        return true; // 行动成功
    }
}
```

### 2.2 动作示例：`Action_GoToSaloonToBuyFood`
```csharp
public class Action_GoToSaloonBuyFood : GOAPAction {
    public Action_GoToSaloonBuyFood() {
        this.Name = "Buy Food at Saloon";
        
        // 只有晚上或下午开门时才能执行
        this.Preconditions.Add("Saloon_IsOpen", true);
        
        // 执行后，身上就有沙拉了
        this.Effects.Add("HasItem_Salad", true);
        
        // 成本计算：距离越远成本越高
        this.Cost = 10f; 
    }
    
    public override float CalculateDynamicCost(NPC actor) {
        return Vector2.Distance(actor.Position, Saloon.Position); 
    }
}
```

---

## 3. LLM 的唯一职责：下达 Goal (目标注入)

当“启发式看门狗”判定当前事件熵极低时，NPC 只会在后台跑常规的时间表（Schedule）。
当触发**随机事件**或 LLM 的接管逻辑时，大语言模型通过 API 只向游戏客户端吐出一个短悍的 JSON 指令：

```json
{
  "Goal": "IsHungry",
  "TargetValue": false,
  "Priority": 100
}
```

### 3.1 规划器的逆向推导 (A* Search)
C# 中的 GOAP 引擎收到要求 `IsHungry = false` 的最高指示。
它开始在脑海中（在一帧的时间里）推导：
- 需要 `IsHungry = false` -> 能够满足这个 Effect 的是 `Action_EatSalad`。
- 但是检查黑板：`HasItem_Salad = false`，前置条件不满足。
- 怎样才能让 `HasItem_Salad = true`？ -> 发现动作 `Action_GoToSaloonBuyFood` 可以提供这个 Effect。
- 检查 `Action_GoToSaloonBuyFood` 的前置条件：`Saloon_IsOpen = true` -> 黑板显示当前是下午，满足。

**最终规划出的动作序列 (Plan)：**
`[ Action_GoToSaloonBuyFood ]` -> `[ Action_EatSalad ]`

引擎将这个方案转换为游戏内实际使用的 PathFinding 和按键模拟，NPC 便如真人般自行转身走向了酒馆。

---

## 4. 为什么这么设计？
**安全性与防幻觉：** 
如果让 LLM 说出：“我要去酒馆吃东西”，LLM 极其容易生成死循环坐标或者卡墙路线（Hallucination）。
而采用 GOAP，由于 `Action` 库和 `Cost` 计算全都是我们自己写的原生 C# 逻辑，NPC **绝不会做出超出游戏界限的动作**。LLM 只是一个下达宏观业务指令的“老板”，底层的寻路容错和避障全由可靠的 C# “打工人”全权负责。
