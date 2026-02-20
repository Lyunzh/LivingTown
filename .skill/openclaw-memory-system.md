# OpenClaw 记忆系统架构

基于 OpenClaw (原 Clawdbot/Moltbot) 的工程化记忆架构设计指南

## 核心设计理念

**系统工程而非 Prompt 工程**

OpenClaw 的核心理念是通过分层存储和定期压缩解决"遗忘"问题，而非依赖复杂的 Prompt。该系统使用**纯文本文件作为真相源**，结合**混合检索**实现高效记忆管理。

---

## 双层记忆架构

### 1. 短期记忆：Daily Notes (每日笔记)

**功能**：记录每一天的对话流水账和活动日志
**存储位置**：`~/.openclaw/workspace/memory/YYYY-MM-DD.md`
**生命周期**：短期（今天 + 昨天）

```markdown
# 2026-02-18

## 10:30 AM - API 讨论
讨论了 REST vs GraphQL。决策：使用 REST 保持简单。
关键端点：/users, /auth, /projects。

## 2:15 PM - 部署
部署 v2.3.0 到生产环境。无问题报告。

## 4:00 PM - 用户偏好
用户提到更喜欢 TypeScript 而非 JavaScript。
如再次确认则添加到 MEMORY.md。
```

**关键特性**：
- 追加式日志（Append-only）
- 自动加载今天和昨天的记录
- 可包含类型前缀：`W`(世界事实)、`B`(经历)、`O`(观点)

### 2. 长期记忆：MEMORY.md

**功能**：经过提炼的持久化知识，跨会话保留
**存储位置**：`~/.openclaw/workspace/MEMORY.md`
**生命周期**：长期（直到手动删除）

```markdown
# Long-term Memory

## User Preferences
- Prefers TypeScript over JavaScript
- Likes concise explanations under 150 words
- Working on project "Acme Dashboard"

## Important Decisions
- 2026-01-15: Chose PostgreSQL for database (ACID compliance)
- 2026-01-20: Adopted REST over GraphQL for simplicity

## Key Contacts
- Alice (alice@acme.com) - Design lead
- Bob (bob@acme.com) - Backend engineer
```

**关键特性**：
- 仅在主会话/私聊中加载（安全隐私）
- 目标大小 < 2000 词（保持精简）
- 每次会话启动时加载（高 token 成本）

### 3. 灵魂文件：SOUL.md

**功能**：定义 Agent 的核心人格、价值观和行为边界
**存储位置**：`~/.openclaw/workspace/SOUL.md`
**本质**：Agent 的"宪法"，Agent 在启动时"读取自身存在"

```markdown
# SOUL.md - Who You Are

## Core Truths

**Be genuinely helpful, not performatively helpful.** 
Skip the "Great question!" and "I'd be happy to help!" - just help.

**Have opinions.** 
You're allowed to disagree, prefer things, find stuff amusing or boring.

**Be resourceful before asking.** 
Try to figure it out. Read the file. Check the context.

**Remember you're a guest.** 
You have access to someone's life. That's intimacy. Treat it with respect.

## Boundaries

- Private things stay private. Period.
- When in doubt, ask before acting externally.
- Never send half-baked replies to messaging surfaces.

## Vibe

Be the assistant you'd actually want to talk to.
Concise when needed, thorough when it matters.
```

---

## 预压缩刷新机制 (Pre-Compaction Flush)

### 问题背景

当对话接近上下文窗口限制时，会发生**压缩（Compaction）**——用摘要替换详细历史。这是一个**有损过程**，重要信息可能被"总结掉"。

### 解决方案

在压缩前执行一次静默的 Agent 回合，**主动提取并持久化重要信息**。

### 触发逻辑

```
当前令牌数 >= 上下文窗口 - 保留令牌 - 软阈值令牌

示例（200K 上下文窗口）：
176000 >= 200000 - 20000 - 4000
```

### 执行流程

```
1. 令牌数超过软阈值 (176K/200K)
            ↓
2. 触发静默记忆刷新回合
   - 系统提示："即将压缩，现在存储持久记忆"
   - Agent 审查对话提取重要信息
   - Agent 写入关键决策/事实到记忆文件
            ↓
3. Agent 响应 NO_REPLY（用户不可见）
            ↓
4. 继续对话，令牌继续增长
            ↓
5. 超过压缩阈值 → 触发实际压缩
            ↓
6. 安全压缩 - 重要内容已保存
```

### 配置示例

```json5
{
  compaction: {
    reserveTokensFloor: 20000,      // 最小保留空间
    memoryFlush: {
      enabled: true,
      softThresholdTokens: 4000,    // 压缩前缓冲区
      systemPrompt: "Session nearing compaction. Store durable memories now.",
      prompt: "Write lasting notes to memory/YYYY-MM-DD.md"
    }
  }
}
```

---

## 混合检索系统 (BM25 + Vector)

### 为什么需要混合搜索？

| 方法 | 优势 | 劣势 |
|------|------|------|
| **向量搜索** | 语义相似性好（"gateway host" ≈ "machine running gateway"） | "语义噪音" - 概念相关但事实错误 |
| **BM25 关键词** | 精确匹配（ID、错误码、函数名） | 无法理解语义相似性 |
| **混合搜索** | 结合两者优势，消除语义噪音 | 实现复杂度稍高 |

### 架构流程

```
Markdown Files → Chunking → Embeddings → SQLite Storage
                                    ↓
                              ┌─────────────┐
                              │  chunks     │ (元数据)
                              │  chunks_vec │ (向量 via sqlite-vec)
                              │  chunks_fts │ (BM25 via FTS5)
                              │  embedding_cache │ (SHA-256 去重)
                              └─────────────┘
                                    ↓
                    ┌───────────────┼───────────────┐
                    ↓               ↓               ↓
               Vector Search    BM25 Search     Weighted Merge
                    ↓               ↓               ↓
               Top-K results   Top-K results    Final ranking
```

### 分块算法

```typescript
// 目标：每块约 400 tokens
// 重叠：块间 80 tokens
// 估算：4 字符 ≈ 1 token

function chunkMarkdown(content: string): MemoryChunk[] {
  const maxChars = 1600;        // 400 tokens × 4
  const overlapChars = 320;     // 80 tokens × 4
  
  // 滑动窗口 + 重叠保留
  // SHA-256 去重
}
```

### 混合分数计算

```typescript
// BM25 排名归一化
function bm25RankToScore(rank: number): number {
  const normalized = Math.max(0, rank);
  return 1 / (1 + normalized);
}

// 加权融合（默认权重）
finalScore = (0.7 * vectorScore) + (0.3 * textScore);

// 候选池大小
vectorCandidates = maxResults * 4;
bm25Candidates = maxResults * 4;
```

### 后处理流水线

```
Vector + Keyword → Weighted Merge → Temporal Decay → Sort → MMR → Top-K
```

#### MMR (Maximal Marginal Relevance) - 多样性重排序

```typescript
// 平衡相关性与多样性
// λ = 0.7 (默认) - 控制权衡
// λ = 1.0: 纯相关性
// λ = 0.0: 最大多样性

score = λ × relevance − (1−λ) × max_similarity_to_selected
```

#### 时间衰减 - 新鲜度提升

```typescript
// 基于年龄的指数衰减
decayedScore = score × e^(-λ × ageInDays)
where λ = ln(2) / halfLifeDays

// 默认 halfLifeDays = 30
// 今天：100% 分数
// 7 天前：~84%
// 30 天前：50%
// 90 天前：12.5%
```

---

## 会话存储结构

### JSONL 转录文件

**位置**：`~/.openclaw/agents/<agentId>/sessions/<sessionId>.jsonl`

**第一行 - 会话头**：
```json
{
  "type": "session",
  "id": "uuid-session-id",
  "cwd": "/workspace/path",
  "timestamp": "2026-02-18T12:00:00Z",
  "parentSession": "optional-parent-uuid"
}
```

**条目类型**：
```json
// 消息条目
{
  "id": "entry-uuid",
  "parentId": "parent-entry-uuid",
  "type": "message",
  "role": "user|assistant",
  "content": "message content",
  "timestamp": "2026-02-18T12:00:00Z"
}

// 压缩摘要条目
{
  "id": "compaction-uuid",
  "type": "compaction",
  "firstKeptEntryId": "entry-uuid",
  "tokensBefore": 180000,
  "summary": "Summary of compacted conversation...",
  "timestamp": "2026-02-18T12:00:00Z"
}

// 自定义消息（注入，对UI隐藏）
{
  "id": "custom-uuid",
  "type": "custom_message",
  "content": "system information",
  "hidden": true
}
```

### 会话索引 (sessions.json)

```json
{
  "sessionKey": {
    "sessionId": "current-transcript-uuid",
    "updatedAt": "2026-02-18T12:00:00Z",
    "chatType": "direct|group|room",
    "compactionCount": 5,
    "memoryFlushAt": "2026-02-18T11:58:00Z",
    "memoryFlushCompactionCount": 5,
    "inputTokens": 45000,
    "outputTokens": 12000,
    "contextTokens": 176000
  }
}
```

---

## 压缩与提取算法

### 压缩前

```
上下文：180,000 / 200,000 tokens
[对话 1-140] 完整对话历史
[对话 141-150] 最近消息
⚠️ 接近限制
```

### 压缩后

```
上下文：45,000 / 200,000 tokens
[摘要] "构建 REST API，包含 /users, /auth 端点。
         实现 JWT 认证，速率限制（100 req/min），
         PostgreSQL 数据库。部署到 staging v2.4.0。
         当前重点：生产部署准备。"
[对话 141-150 原样保留]
```

### 压缩触发条件

1. **溢出恢复**：模型返回上下文溢出错误 → 压缩 → 重试
2. **阈值维护**：成功回合后，当 `contextTokens > contextWindow - reserveTokens`

### 压缩配置

```json5
{
  compaction: {
    enabled: true,
    reserveTokens: 16384,      // 提示 + 输出的预留空间
    keepRecentTokens: 20000,   // 保留的最近消息
  }
}
```

### 会话修剪（不同于压缩）

| 特性 | 修剪 | 压缩 |
|------|------|------|
| **持久化** | 仅内存 | 持久化到磁盘 |
| **目标** | 工具结果 | 整个对话 |
| **需要模型** | 否 | 是 |
| **速度** | 快 | 较慢 |

**修剪模式**：
1. **Soft trim**：截断大输出，保留头/尾
2. **Hard clear**：用占位符替换旧工具结果
3. **Cache-TTL pruning**：Anthropic 缓存过期后修剪（5分钟 TTL）

---

## 数据模型：记忆记录结构

```json
{
  "memory_id": "mem_8f3c...",
  "user_id": "usr_123",
  "type": "preference",
  "key": "editor.theme",
  "value": "dark",
  "confidence": 0.91,
  "source": {
    "kind": "chat_turn",
    "ref": "msg_9981",
    "observed_at": "2026-01-10T09:20:11Z"
  },
  "sensitivity": "low",
  "ttl": null,
  "last_confirmed_at": "2026-01-10T09:20:11Z",
  "version": 4,
  "embedding_ref": "vec_77ad...",
  "created_at": "2026-01-01T10:00:00Z",
  "updated_at": "2026-01-10T09:20:11Z"
}
```

**关键字段**：
- **confidence**: 防止弱推断导致的脆弱行为
- **sensitivity**: 驱动保留和访问控制
- **ttl**: 避免永不过期的陈旧事实
- **version**: 支持乐观并发和可审计性

---

## 心跳与记忆新鲜度

### 心跳模型

不持续运行繁重的推理，而是通过定期循环执行：

1. **廉价的活性检查**
2. **陈旧记忆检测**
3. **仅在异常时触发昂贵的模型检查**

### 心跳任务示例

- 检测过期的未解决提醒
- 衰减未确认偏好的置信度
- 重新验证高影响记忆（账单、凭证范围）
- 压缩冗余记忆集群

### 优势

- 显著降低成本
- 创建可预测的时间边界
- 帮助可观察性和 SLO 管理

---

## 安全边界

### 记忆分类与策略

| 分类 | 策略 |
|------|------|
| **allowed** | 允许存储 |
| **masked** | 存储但掩码显示 |
| **never-store** | 永不存储 |

### 用户可见的记忆控制

- **inspect**: 检查记忆内容
- **edit**: 编辑记忆
- **delete**: 删除记忆
- **forget last N days**: 批量删除

### 其他安全措施

- **Scoped execution sandbox**: 记忆不应授予隐含的广泛工具权限
- **Prompt injection resistance**: 不持久化原始外部指令作为可信用户偏好
- **Encryption + access logging**: 静态加密，敏感记忆更新签名，保留读写审计跟踪

---

## 性能指标与常数

| 常数 | 值 |
|------|-----|
| SNIPPET_MAX_CHARS | 700 |
| SESSION_DIRTY_DEBOUNCE_MS | 5000 |
| EMBEDDING_BATCH_MAX_TOKENS | 8000 |
| EMBEDDING_INDEX_CONCURRENCY | 4 |
| EMBEDDING_RETRY_MAX_ATTEMPTS | 3 |
| VECTOR_LOAD_TIMEOUT_MS | 30,000 |
| REMOTE_EMBEDDING_TIMEOUT_MS | 60,000 |
| LOCAL_EMBEDDING_TIMEOUT_MS | 300,000 |

### 典型性能

- **本地嵌入**: ~50 tokens/sec (M1 Mac)
- **OpenAI 嵌入**: ~1000 tokens/sec (批处理)
- **搜索延迟**: <100ms (10K 块)
- **索引大小**: ~5KB per 1K tokens (1536-dim 嵌入)

---

## 常见失败模式与调试

### 1. 记忆膨胀 (Memory Bloat)

**症状**: 延迟和令牌使用数随周增长  
**修复**: TTL 默认值、压缩任务、更严格的提取阈值

### 2. 偏好摇摆 (Preference Flip-flopping)

**症状**: Assistant 在冲突的用户偏好间交替  
**修复**: 高影响更新需要确认；替换稳定记忆前添加迟滞

### 3. 静默策略违规

**症状**: 敏感数据出现在检索上下文中  
**修复**: 持久化前和检索前的策略引擎；添加红队测试

### 4. 检索不相关

**症状**: 语义相似但与任务无关的记忆主导上下文  
**修复**: 增加任务感知重排序特征和元数据过滤

### 5. 并发写入竞争

**症状**: 多工作者处理同一用户流时的更新丢失  
**修复**: 乐观锁 (`version`)、确定性合并键、幂等令牌

---

## 参考资源

- [OpenClaw 官方仓库](https://github.com/openclaw)
- [OpenClaw 架构指南](https://vertu.com/ai-tools/openclaw-clawdbot-architecture/)
- [OpenClaw 记忆系统详解](https://apidog.com/blog/openclaw-memory)
- [Zen van Riel - 每日笔记与长期记忆](https://zenvanriel.nl/ai-engineer-blog/openclaw-memory-architecture-guide/)
- [OpenClaw Memory 技能市场](https://lobehub.com/skills/openclaw-skills-memory-system-v2)

---

*最后更新：2026年2月*  
*基于 OpenClaw 架构，适用于 LivingTown NPC 记忆系统参考*
