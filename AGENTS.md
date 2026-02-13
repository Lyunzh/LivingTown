# SMAPI & LLM Integration Agent Knowledge Base

## 1. SMAPI Basics
- **Goal**: Intercept and modify NPC dialogue to inject LLM-generated responses.
- **Key Approaches**:
    1. **GameLoop.UpdateTicking Event**: Monitor `Game1.activeClickableMenu`. If it is a `DialogueBox`, we can inspect it.
    2. **Question Dialogues**: `GameLocation.createQuestionDialogue(string, Response[], delegate)` to present "Normal" vs "AI" choice.
    3. **Custom Menus**: Subclass `IClickableMenu` to create a custom dialogue interface that supports streaming text (updating content frame-by-frame).
    4. **Harmony Patching**: Intercept `NPC.checkAction` to suppress default behavior and show our Question Dialogue instead.

## 2. LLM Integration
- **Goal**: Generate dialogue based on context (NPC personality, relationship, season, location).
- **Architecture**:
    - **LLMService**: Handles HTTP requests to DeepSeek API (using keys from `.env`).
    - **ContextBuilder**: Gathers game state (NPC data, player data).
    - **DialogueManager**: Orchestrates the fetch and display of dialogue.
