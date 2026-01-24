# Replacing Microagents with GitHub Copilot SDK

## Executive Summary

This document summarizes research on migrating the azsdk-cli's "microagent" system to use the new GitHub Copilot SDK. The Copilot SDK provides a mature, multi-language client for programmatic access to GitHub Copilot's AI capabilities via JSON-RPC over stdio/TCP. This migration would leverage Copilot's infrastructure instead of directly calling OpenAI APIs.

---

## Part 1: Current Microagent Architecture

### Core Components

#### 1. `Microagent<TResult>` (Definition)
**Location:** `Azure.Sdk.Tools.Cli/Microagents/Microagent.cs`

A strongly-typed agent definition containing:
- **Instructions**: System prompt / task description for the LLM
- **Tools**: List of `IAgentTool` implementations available to the agent
- **Model**: OpenAI model name (default: `"gpt-4.1"`)
- **MaxToolCalls**: Iteration limit before timeout (default: `100`)
- **ValidateResult**: Optional async callback to validate output; on failure, agent is prompted to retry

```csharp
public class Microagent<TResult> where TResult : notnull
{
    public required string Instructions { get; init; }
    public Func<TResult, Task<MicroagentValidationResult>>? ValidateResult { get; init; }
    public IEnumerable<IAgentTool> Tools { get; init; } = [];
    public string Model { get; init; } = "gpt-4.1";
    public int MaxToolCalls { get; init; } = 100;
}
```

#### 2. `MicroagentHostService` (Execution Engine)
**Location:** `Azure.Sdk.Tools.Cli/Microagents/MicroagentHostService.cs`

Implements the agent loop:
1. Sends instructions to LLM as user message
2. Forces tool choice on every turn (`ToolChoice = ChatToolChoice.CreateRequiredChoice()`)
3. Single tool call per turn (`AllowParallelToolCalls = false`)
4. Dispatches tool calls to registered `IAgentTool` implementations
5. Adds a special `Exit` tool that returns the strongly-typed result
6. On `Exit` call, optionally validates result; if validation fails, prompts agent to retry
7. Returns `TResult` on successful exit

**Key Design Decisions:**
- Uses OpenAI SDK directly (`OpenAIClient`)
- Tracks token usage via `TokenUsageHelper`
- Logs full conversation via `ConversationLogger`

#### 3. `IAgentTool` / `AgentTool<TInput, TOutput>` (Tool Contract)
**Location:** `Azure.Sdk.Tools.Cli/Microagents/IAgentTool.cs`, `AgentTool.cs`

```csharp
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    string InputSchema { get; }  // JSON Schema string
    Task<string> Invoke(string input, CancellationToken ct = default);
}
```

`AgentTool<TInput, TOutput>` provides:
- Automatic JSON schema generation from `TInput` type
- JSON serialization/deserialization
- `FromFunc()` factory for inline tool definitions

---

### Current Microagent Implementations

| Microagent | Purpose | Tools Used | Result Type | Location |
|------------|---------|------------|-------------|----------|
| **Fibonacci** | Demo: compute Nth Fibonacci | `advance_state` | `int` | `ExampleTool.cs` |
| **README Generator** | Generate README from template | `check_readme_tool` | `ReadmeContents` | `ReadMeGeneratorTool.cs` |
| **Sample Generator** | Generate SDK code samples | None | `List<GeneratedSample>` | `SampleGeneratorTool.cs` |
| **Sample Translator** | Translate samples between languages | None | `List<TranslatedSample>` | `SampleTranslatorTool.cs` |
| **Spelling Fix** | Fix cspell errors | `ReadFileTool`, `WriteFileTool`, `UpdateCspellWordsTool` | `SpellingFixResult` | `CommonLanguageHelpers.cs` |
| **Java Patch** | Apply code patches to SDK | `ReadFileTool`, `ClientCustomizationCodePatchTool` | `bool` | `JavaLanguageService.cs` |

---

### Microagent Invocation Pattern

```csharp
// 1. Define the microagent
var microagent = new Microagent<List<GeneratedSample>>
{
    Instructions = enhancedPrompt,
    Model = model,
    Tools = [/* optional tools */],
    ValidateResult = async result => { /* optional validation */ }
};

// 2. Execute via host service
var result = await microagentHostService.RunAgentToCompletion(microagent, ct);

// 3. Use the strongly-typed result
foreach (var sample in result)
{
    await File.WriteAllTextAsync(sample.FileName, sample.Content);
}
```

---

## Part 2: GitHub Copilot SDK Architecture

### Overview

The Copilot SDK provides programmatic access to GitHub Copilot CLI via JSON-RPC. Available for .NET, Node.js, Python, and Go.

**Communication:** JSON-RPC over stdio (default) or TCP
**Client Lifecycle:** `client.StartAsync()` → `session.CreateAsync()` → `session.SendAsync()` → events → `session.DisposeAsync()`

### Core Components (.NET SDK)

#### 1. `CopilotClient`
**Location:** `GitHub.Copilot.SDK/Client.cs`

Manages connection to Copilot CLI server:
- Spawns CLI process or connects to existing server
- Creates/manages sessions
- Handles JSON-RPC communication via StreamJsonRpc

```csharp
await using var client = new CopilotClient(new CopilotClientOptions
{
    UseStdio = true,           // Use stdio transport (default)
    CliPath = "copilot",       // CLI executable path
    AutoStart = true,          // Auto-start on first use
    Logger = logger            // Optional ILogger
});
await client.StartAsync();
```

#### 2. `CopilotSession`
**Location:** `GitHub.Copilot.SDK/Session.cs`

Represents a conversation session:
- Send messages with `SendAsync()`
- Subscribe to events with `On()`
- Get history with `GetMessagesAsync()`
- Abort with `AbortAsync()`

```csharp
await using var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5",
    Tools = [/* AIFunction instances */],
    SystemMessage = new SystemMessageConfig
    {
        Mode = SystemMessageMode.Append,
        Content = "Additional instructions..."
    }
});
```

#### 3. Tools Integration

Uses `Microsoft.Extensions.AI` for tool definitions:

```csharp
var tools = new[]
{
    AIFunctionFactory.Create(
        async ([Description("File path")] string path) => 
            await File.ReadAllTextAsync(path),
        "read_file",
        "Reads a file from disk")
};

var session = await client.CreateSessionAsync(new SessionConfig
{
    Model = "gpt-5",
    Tools = tools
});
```

When Copilot invokes a tool:
1. SDK receives `tool.call` JSON-RPC request
2. Dispatches to registered `AIFunction`
3. Returns result to Copilot CLI
4. Result is passed back to LLM

#### 4. Event System

Sessions emit typed events for real-time monitoring:

```csharp
session.On(evt =>
{
    switch (evt)
    {
        case AssistantMessageEvent msg:
            Console.WriteLine(msg.Data.Content);
            break;
        case ToolExecutionStartEvent toolStart:
            Console.WriteLine($"Tool: {toolStart.Data.ToolName}");
            break;
        case SessionErrorEvent err:
            Console.WriteLine($"Error: {err.Data.Message}");
            break;
        case SessionIdleEvent:
            Console.WriteLine("Session idle");
            break;
    }
});
```

**Key Events:**
- `SessionStartEvent` / `SessionIdleEvent` - Lifecycle
- `AssistantMessageEvent` - LLM responses
- `ToolExecutionStartEvent` / `ToolExecutionCompleteEvent` - Tool calls
- `AssistantUsageEvent` - Token usage tracking
- `SessionErrorEvent` - Errors

---

## Part 3: Migration Analysis

### Feature Comparison

| Feature | Current Microagents | Copilot SDK | Risk/Effort | Decision |
|---------|---------------------|-------------|-------------|----------|
| **LLM Provider** | OpenAI direct | Copilot CLI (abstracts provider) | Low | Use Copilot auth; BYOK available if needed later |
| **Tool Contract** | `IAgentTool` (custom) | `AIFunction` (Microsoft.Extensions.AI) | Low | MEAI already in codebase; 1:1 mapping |
| **Schema Generation** | Custom `ToolHelpers.GetJsonSchemaRepresentation()` | Built into MEAI | Low | Auto-generation from C# types |
| **Agent Loop** | Custom implementation | Handled by Copilot CLI | Medium | Copilot handles loop; need wrapper for Exit + validation |
| **Result Typing** | Strongly-typed via `Exit` tool | Event-based | Low | Preserve Exit tool pattern via AIFunction |
| **Validation Loop** | Built-in `ValidateResult` callback | Must implement manually | Low-Medium | Small wrapper loop required |
| **Parallel Tools** | Disabled by design | Enabled by default | None | Not a hard requirement; trust Copilot for ordering |
| **Streaming** | Not supported | Via events | None | Not needed; ignore streaming events |
| **Token Tracking** | `TokenUsageHelper` | `AssistantUsageEvent` | Low | Event subscription to existing helper |

### Detailed Analysis

#### 1. LLM Provider (Low Risk)

- **Current:** `MicroagentHostService` calls `openAI.GetChatClient(model)` directly
- **Copilot SDK:** CLI abstracts the provider; specify model name like `"gpt-5"` or `"claude-sonnet-4.5"`

**Decision:** Use GitHub-authenticated models (simpler, no key management). BYOK exists via `ProviderConfig` if direct OpenAI access is needed later.

**Note:** Authentication in GitHub Coding Agent context assumed solvable - requires testing.

#### 2. Tool Contract (Low Risk)

- **Current:** Custom `IAgentTool` interface with manual JSON schema generation
- **Copilot SDK:** `AIFunction` from Microsoft.Extensions.AI with automatic schema generation

**Decision:** MEAI is already used in `Azure.Sdk.Tools.Cli.Evaluations` project. Migration is straightforward 1:1 mapping:

```csharp
// Before (Microagent)
AgentTool<ReadmeContents, CheckReadmeResult>.FromFunc(
    "check_readme_tool",
    "Checks a readme",
    CheckReadme)

// After (Copilot SDK)
AIFunctionFactory.Create(
    async ([Description("README contents")] ReadmeContents input) =>
        await CheckReadme(input, ct),
    "check_readme_tool",
    "Checks a readme")
```

#### 3. Agent Loop Ownership (Medium Effort)

- **Current:** `MicroagentHostService` implements the loop with forced single tool call per turn
- **Copilot SDK:** CLI handles the loop internally; SDK sends messages and receives events

**Decision:** The "force single tool call per turn" pattern is not a hard requirement - it was an implementation detail. Copilot CLI can manage the loop. A thin wrapper is needed for the Exit tool capture and validation retry.

#### 4. Exit Tool Pattern (Low Risk)

The Exit tool is a special tool automatically added to every microagent:

```csharp
ChatTool.CreateFunctionTool(
    "Exit",
    "Call this tool when you are finished with the work or are otherwise unable to continue.",
    JsonSchema(MicroagentResult<TResult>)
)
```

**Why it exists:** Forces structured output through a tool call with a defined schema, rather than parsing free-form text. This provides:
- Guaranteed structure (schema-validated by the LLM)
- Clear signal that the task is complete
- Hook for validation before accepting the result

**Decision:** Preserve this pattern using `AIFunctionFactory.Create()`:

```csharp
TResult? capturedResult = null;

var exitTool = AIFunctionFactory.Create(
    ([Description("The result of the agent run")] TResult result) => 
    {
        capturedResult = result;
        return "Exiting with result";
    },
    "Exit",
    "Call this tool when you are finished with the work or are otherwise unable to continue.");
```

#### 5. Validation Loop (Low-Medium Effort)

- **Current:** If `ValidateResult` returns failure, the loop automatically continues with a "try again" message
- **Copilot SDK:** Must implement manually with a wrapper loop

**Decision:** Implement wrapper loop:

```csharp
while (true)
{
    await session.SendAsync(prompt);
    await WaitForIdleAsync(session);
    
    if (capturedResult != null && validateResult != null)
    {
        var validation = await validateResult(capturedResult);
        if (!validation.Success)
        {
            prompt = $"The result did not pass validation: {validation.Reason}. Please try again.";
            capturedResult = null;
            continue;
        }
    }
    break;
}
return capturedResult;
```

#### 6. Parallel Tools (No Risk)

- **Current:** `AllowParallelToolCalls = false` - disabled by design
- **Copilot SDK:** Parallel execution enabled by default

**Decision:** Single-tool-per-turn is not a hard requirement. Parallel execution could be faster for some workloads. Trust Copilot to handle ordering for standard patterns (file reads/writes are well-understood).

#### 7. Streaming (No Action)

- **Current:** Wait for full response
- **Copilot SDK:** `AssistantMessageEvent` with `ChunkContent` for streaming

**Decision:** Streaming not needed at this point. Just wait for `SessionIdleEvent`.

#### 8. Token Tracking (Low Effort)

- **Current:** `tokenUsageHelper.Add(model, inputTokens, outputTokens)` after each OpenAI response
- **Copilot SDK:** `AssistantUsageEvent` contains same data

**Decision:** Subscribe to events and integrate with existing helper:

```csharp
session.On(evt =>
{
    if (evt is AssistantUsageEvent usage)
    {
        tokenUsageHelper.Add(
            usage.Data.Model,
            (int)(usage.Data.InputTokens ?? 0),
            (int)(usage.Data.OutputTokens ?? 0));
    }
});
```

---

## Part 4: Recommended Migration Strategy

### Approach: New Interface with AIFunction Tools

Create a new `ICopilotAgentRunner` interface that uses `AIFunction` for tools directly, rather than wrapping the old `IAgentTool` interface. This provides proper JSON schema support and a cleaner API.

**Why this approach:**
- Proper JSON schema enforcement (AIFunction generates schemas from C# types)
- Cleaner API using MEAI conventions
- Preserves the Exit tool pattern for structured output
- Preserves the validation retry loop
- Can leverage Copilot's parallel tool execution for performance
- Migrating tools to `AIFunctionFactory` is straightforward

### Implementation Sketch

```csharp
/// <summary>
/// Definition of a Copilot-powered agent using AIFunction tools.
/// </summary>
public class CopilotAgent<TResult> where TResult : notnull
{
    public required string Instructions { get; init; }
    public IEnumerable<AIFunction> Tools { get; init; } = [];
    public string Model { get; init; } = "gpt-5";
    public int MaxIterations { get; init; } = 100;
    public Func<TResult, Task<CopilotAgentValidationResult>>? ValidateResult { get; init; }
}

public class CopilotAgentValidationResult
{
    public required bool Success { get; set; }
    public object? Reason { get; set; }
}

public interface ICopilotAgentRunner
{
    Task<TResult> RunAsync<TResult>(CopilotAgent<TResult> agent, CancellationToken ct = default)
        where TResult : notnull;
}

public class CopilotAgentRunner : ICopilotAgentRunner
{
    private readonly CopilotClient _client;
    private readonly TokenUsageHelper _tokenUsageHelper;
    private readonly ILogger<CopilotAgentRunner> _logger;

    public CopilotAgentRunner(
        CopilotClient client,
        TokenUsageHelper tokenUsageHelper,
        ILogger<CopilotAgentRunner> logger)
    {
        _client = client;
        _tokenUsageHelper = tokenUsageHelper;
        _logger = logger;
    }

    public async Task<TResult> RunAsync<TResult>(
        CopilotAgent<TResult> agent,
        CancellationToken ct = default) where TResult : notnull
    {
        // Collect tools + Exit tool
        var tools = agent.Tools.ToList();
        
        TResult? capturedResult = default;
        tools.Add(AIFunctionFactory.Create(
            ([Description("The result of the agent run. Output the result requested exactly, without additional padding, explanation, or code fences unless requested.")] 
             TResult result) =>
            {
                capturedResult = result;
                return "Exiting with result";
            },
            "Exit",
            "Call this tool when you are finished with the work or are otherwise unable to continue."));

        // Create session
        await using var session = await _client.CreateSessionAsync(new SessionConfig
        {
            Model = agent.Model,
            Tools = tools,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = agent.Instructions
            }
        }, ct);

        // Subscribe to events for token tracking
        session.On(evt =>
        {
            if (evt is AssistantUsageEvent usage)
            {
                _tokenUsageHelper.Add(
                    usage.Data.Model ?? agent.Model,
                    (int)(usage.Data.InputTokens ?? 0),
                    (int)(usage.Data.OutputTokens ?? 0));
            }
        });

        // Validation retry loop
        var prompt = "Begin the task. Call tools as needed, then call Exit with the result.";
        var iterations = 0;

        while (iterations < agent.MaxIterations)
        {
            iterations++;
            capturedResult = default;

            // Send message and wait for idle
            await session.SendAsync(new MessageOptions { Prompt = prompt }, ct);
            await WaitForSessionIdleAsync(session, ct);

            // Check if Exit was called
            if (capturedResult == null)
            {
                throw new InvalidOperationException("Agent completed without calling Exit tool");
            }

            // Validate result if validator provided
            if (agent.ValidateResult != null)
            {
                var validation = await agent.ValidateResult(capturedResult);
                if (!validation.Success)
                {
                    var reason = validation.Reason is string str 
                        ? str 
                        : JsonSerializer.Serialize(validation.Reason);
                    _logger.LogWarning("Agent result failed validation: {Reason}. Retrying.", reason);
                    prompt = $"The result you provided did not pass validation: {reason}. Please try again.";
                    continue;
                }
            }

            // Success
            return capturedResult;
        }

        throw new InvalidOperationException(
            $"Agent did not return a valid result within {agent.MaxIterations} iterations");
    }

    private static async Task WaitForSessionIdleAsync(CopilotSession session, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource();
        
        using var subscription = session.On(evt =>
        {
            if (evt is SessionIdleEvent)
                tcs.TrySetResult();
            else if (evt is SessionErrorEvent err)
                tcs.TrySetException(new InvalidOperationException(err.Data.Message));
        });

        using var registration = ct.Register(() => tcs.TrySetCanceled());
        
        await tcs.Task;
    }
}
```

### Tool Migration Example

Before (using `AgentTool`):
```csharp
var checkReadmeTool = AgentTool<ReadmeContents, CheckReadmeResult>.FromFunc(
    "check_readme_tool",
    "Checks a readme to make sure that all the required values have been replaced",
    CheckReadme);

var microagent = new Microagent<ReadmeContents>
{
    Instructions = prompt,
    Model = model,
    Tools = [checkReadmeTool]
};

var result = await microagentHostService.RunAgentToCompletion(microagent, ct);
```

After (using `AIFunctionFactory`):
```csharp
var checkReadmeTool = AIFunctionFactory.Create(
    async ([Description("The README contents to check")] ReadmeContents contents) =>
        await CheckReadme(contents, ct),
    "check_readme_tool",
    "Checks a readme to make sure that all the required values have been replaced");

var agent = new CopilotAgent<ReadmeContents>
{
    Instructions = prompt,
    Model = "gpt-5",
    Tools = [checkReadmeTool]
};

var result = await copilotAgentRunner.RunAsync(agent, ct);
```

### Migration Steps

#### Phase 1: Infrastructure Setup

- [ ] **1.1** Add `GitHub.Copilot.SDK` package reference to `Azure.Sdk.Tools.Cli/Azure.Sdk.Tools.Cli.csproj`
- [ ] **1.2** Add `Microsoft.Extensions.AI` package reference (if not already present in main project)
- [ ] **1.3** Create `Azure.Sdk.Tools.Cli/CopilotAgents/` directory for new types
- [ ] **1.4** Create `CopilotAgentValidationResult.cs` with `Success` and `Reason` properties
- [ ] **1.5** Create `CopilotAgent<TResult>.cs` with `Instructions`, `Tools`, `Model`, `MaxIterations`, `ValidateResult` properties
- [ ] **1.6** Create `ICopilotAgentRunner.cs` interface with `RunAsync<TResult>()` method
- [ ] **1.7** Create `CopilotAgentRunner.cs` implementing `ICopilotAgentRunner` (see implementation sketch above)
- [ ] **1.8** Register `CopilotClient` in DI (see Appendix C)
- [ ] **1.9** Register `ICopilotAgentRunner` / `CopilotAgentRunner` in DI
- [ ] **1.10** Verify build succeeds

#### Phase 2: Migrate Fibonacci Example (Validation)

- [ ] **2.1** Open `Azure.Sdk.Tools.Cli/Tools/Example/ExampleTool.cs`
- [ ] **2.2** Add `ICopilotAgentRunner` to constructor injection
- [ ] **2.3** Convert `advance_state` tool from `AgentTool<>` to `AIFunctionFactory.Create()`
- [ ] **2.4** Replace `Microagent<int>` with `CopilotAgent<int>`
- [ ] **2.5** Replace `microagentHostService.RunAgentToCompletion()` with `copilotAgentRunner.RunAsync()`
- [ ] **2.6** Update model from `"gpt-4.1"` to `"gpt-5"`
- [ ] **2.7** Run Fibonacci example and verify correct output
- [ ] **2.8** Verify token usage is still tracked

#### Phase 3: Migrate README Generator

- [ ] **3.1** Open `Azure.Sdk.Tools.Cli/Tools/Package/ReadMeGeneratorTool.cs`
- [ ] **3.2** Add `ICopilotAgentRunner` to constructor injection
- [ ] **3.3** Convert `check_readme_tool` from `AgentTool<>` to `AIFunctionFactory.Create()`
- [ ] **3.4** Replace `Microagent<ReadmeContents>` with `CopilotAgent<ReadmeContents>`
- [ ] **3.5** Replace `microAgentHostService.RunAgentToCompletion()` with `copilotAgentRunner.RunAsync()`
- [ ] **3.6** Update model name
- [ ] **3.7** Test README generation

#### Phase 4: Migrate Sample Generator

- [ ] **4.1** Open `Azure.Sdk.Tools.Cli/Tools/Package/Samples/SampleGeneratorTool.cs`
- [ ] **4.2** Add `ICopilotAgentRunner` to constructor injection
- [ ] **4.3** Replace `Microagent<List<GeneratedSample>>` with `CopilotAgent<List<GeneratedSample>>`
- [ ] **4.4** Replace `microagentHostService.RunAgentToCompletion()` with `copilotAgentRunner.RunAsync()`
- [ ] **4.5** Update model name
- [ ] **4.6** Test sample generation

#### Phase 5: Migrate Sample Translator

- [ ] **5.1** Open `Azure.Sdk.Tools.Cli/Tools/Package/Samples/SampleTranslatorTool.cs`
- [ ] **5.2** Add `ICopilotAgentRunner` to constructor injection
- [ ] **5.3** Replace `Microagent<List<TranslatedSample>>` with `CopilotAgent<List<TranslatedSample>>`
- [ ] **5.4** Replace `microagentHostService.RunAgentToCompletion()` with `copilotAgentRunner.RunAsync()`
- [ ] **5.5** Update model name
- [ ] **5.6** Test sample translation

#### Phase 6: Migrate Spelling Fix

- [ ] **6.1** Open `Azure.Sdk.Tools.Cli/Helpers/CommonLanguageHelpers.cs`
- [ ] **6.2** Add `ICopilotAgentRunner` to constructor/method injection
- [ ] **6.3** Convert `ReadFileTool`, `WriteFileTool`, `UpdateCspellWordsTool` to `AIFunctionFactory.Create()`
- [ ] **6.4** Replace `Microagent<SpellingFixResult>` with `CopilotAgent<SpellingFixResult>`
- [ ] **6.5** Replace `_microagentHostService.RunAgentToCompletion()` with `copilotAgentRunner.RunAsync()`
- [ ] **6.6** Update model name
- [ ] **6.7** Test spelling fix

#### Phase 7: Migrate Java Patch

- [ ] **7.1** Open `Azure.Sdk.Tools.Cli/Services/Languages/JavaLanguageService.cs`
- [ ] **7.2** Add `ICopilotAgentRunner` to constructor injection
- [ ] **7.3** Convert `ReadFileTool`, `ClientCustomizationCodePatchTool` to `AIFunctionFactory.Create()`
- [ ] **7.4** Replace `Microagent<bool>` with `CopilotAgent<bool>`
- [ ] **7.5** Replace `microagentHost.RunAgentToCompletion()` with `copilotAgentRunner.RunAsync()`
- [ ] **7.6** Update model name
- [ ] **7.7** Test Java patch application

#### Phase 8: Cleanup

- [ ] **8.1** Remove `IMicroagentHostService` from all constructor injections
- [ ] **8.2** Remove DI registration for `MicroagentHostService`
- [ ] **8.3** Delete `Azure.Sdk.Tools.Cli/Microagents/Microagent.cs`
- [ ] **8.4** Delete `Azure.Sdk.Tools.Cli/Microagents/MicroagentHostService.cs`
- [ ] **8.5** Delete `Azure.Sdk.Tools.Cli/Microagents/IMicroagentHostService.cs`
- [ ] **8.6** Delete `Azure.Sdk.Tools.Cli/Microagents/IAgentTool.cs`
- [ ] **8.7** Delete `Azure.Sdk.Tools.Cli/Microagents/AgentTool.cs`
- [ ] **8.8** Delete `Azure.Sdk.Tools.Cli/Microagents/` directory if empty
- [ ] **8.9** Remove OpenAI SDK dependency if no longer used elsewhere
- [ ] **8.10** Run full test suite
- [ ] **8.11** Verify build succeeds

---

## Part 5: Implementation Considerations

### 1. Authentication

- **Current:** Uses `OpenAIClient` with API key from configuration
- **Copilot SDK:** Uses GitHub Copilot authentication (requires CLI login)

**Decision:** Use GitHub-authenticated models (preferred). BYOK available via `ProviderConfig` if direct OpenAI access needed later. Authentication in GitHub Coding Agent context assumed solvable - requires testing.

### 2. Model Selection

- **Current:** OpenAI model names (`gpt-4.1`, `gpt-4`)
- **Copilot SDK:** Copilot model names (`gpt-5`, `claude-sonnet-4.5`)

**Decision:** The `CopilotAgent<T>.Model` property uses Copilot model names directly. When migrating each agent, update the model name:
- `"gpt-4.1"` → `"gpt-5"`
- `"gpt-4"` → `"gpt-5"`

Alternatively, add a helper method if you want to support both naming conventions during transition:

```csharp
private static string MapModel(string model) => model switch
{
    "gpt-4.1" => "gpt-5",
    "gpt-4" => "gpt-5",
    _ => model
};
```

### 3. Token Usage Tracking

- **Current:** `TokenUsageHelper` aggregates from OpenAI responses
- **Copilot SDK:** `AssistantUsageEvent` provides per-turn usage

**Decision:** Subscribe to `AssistantUsageEvent` and integrate with existing `TokenUsageHelper` (see implementation sketch).

### 4. Logging

- **Current:** `ConversationLogger` logs full conversation turns
- **Copilot SDK:** Events provide all data needed for logging

**Decision:** Create event handler that subscribes to relevant events (`UserMessageEvent`, `AssistantMessageEvent`, `ToolExecutionStartEvent`, `ToolExecutionCompleteEvent`) and logs in same format.

### 5. Error Handling

- **Current:** Catches tool exceptions, returns error message to LLM
- **Copilot SDK:** Same pattern supported - AIFunction exceptions are caught and returned as error

**Decision:** No changes needed - error handling pattern is compatible.

---

## Part 6: Next Steps

See **Part 4: Migration Steps** for the detailed task breakdown with checkboxes.

**Summary:**
1. Phase 1: Infrastructure Setup (1.1-1.10)
2. Phase 2: Migrate Fibonacci Example - validate the approach works (2.1-2.8)
3. Phase 3-7: Migrate remaining agents one by one
4. Phase 8: Cleanup old implementation

---

## Appendix A: File References

### Azure SDK Tools (Microagents) - Full Paths
- `Azure.Sdk.Tools.Cli/Microagents/Microagent.cs`
- `Azure.Sdk.Tools.Cli/Microagents/MicroagentHostService.cs`
- `Azure.Sdk.Tools.Cli/Microagents/IMicroagentHostService.cs`
- `Azure.Sdk.Tools.Cli/Microagents/IAgentTool.cs`
- `Azure.Sdk.Tools.Cli/Microagents/AgentTool.cs`

### Microagent Usage Locations - Full Paths
- `Azure.Sdk.Tools.Cli/Tools/Example/ExampleTool.cs` - Fibonacci demo (lines ~480-530)
- `Azure.Sdk.Tools.Cli/Tools/Package/ReadMeGeneratorTool.cs` - README generator (lines ~140-170)
- `Azure.Sdk.Tools.Cli/Tools/Package/Samples/SampleGeneratorTool.cs` - Sample generator (lines ~170-220)
- `Azure.Sdk.Tools.Cli/Tools/Package/Samples/SampleTranslatorTool.cs` - Sample translator
- `Azure.Sdk.Tools.Cli/Helpers/CommonLanguageHelpers.cs` - Spelling fix (lines ~260-280)
- `Azure.Sdk.Tools.Cli/Services/Languages/JavaLanguageService.cs` - Java patch (lines ~340-360)

### GitHub Copilot SDK
**Note:** The Copilot SDK source is at `copilot-sdk-main/dotnet/` in the workspace. For implementation, reference the NuGet package:
```xml
<PackageReference Include="GitHub.Copilot.SDK" Version="1.0.0" />
```

SDK source files (for reference):
- `copilot-sdk-main/dotnet/src/Client.cs` - CopilotClient
- `copilot-sdk-main/dotnet/src/Session.cs` - CopilotSession
- `copilot-sdk-main/dotnet/src/Types.cs` - Configuration types
- `copilot-sdk-main/dotnet/src/Generated/SessionEvents.cs` - Event types
- `copilot-sdk-main/README.md` - Overview
- `copilot-sdk-main/dotnet/README.md` - .NET-specific documentation

---

## Appendix B: Required Imports

```csharp
// For CopilotAgentRunner
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json;

// Event types from SDK
// SessionIdleEvent, SessionErrorEvent, AssistantUsageEvent, etc.
```

---

## Appendix C: DI Registration

Current DI setup is in the CLI's startup. Add these registrations:

```csharp
// Register CopilotClient as singleton (manages CLI process lifecycle)
services.AddSingleton<CopilotClient>(sp => 
{
    var logger = sp.GetService<ILogger<CopilotClient>>();
    return new CopilotClient(new CopilotClientOptions
    {
        UseStdio = true,
        AutoStart = true,
        Logger = logger
    });
});

// Register the agent runner
services.AddSingleton<ICopilotAgentRunner, CopilotAgentRunner>();
```

Look for existing DI registration patterns in the codebase (search for `AddSingleton` or `AddScoped` in `Program.cs` or startup files).

---

## Appendix D: ConversationLogger Integration

The current `MicroagentHostService` uses `ConversationLogger` to log conversation turns. To preserve this:

```csharp
// In CopilotAgentRunner constructor
private readonly ConversationLogger _conversationLogger;

// In RunAsync, subscribe to events for logging
session.On(evt =>
{
    switch (evt)
    {
        case UserMessageEvent userMsg:
            _conversationLogger.LogUserMessage(userMsg.Data.Content);
            break;
        case AssistantMessageEvent assistantMsg:
            _conversationLogger.LogAssistantMessage(assistantMsg.Data.Content);
            break;
        case ToolExecutionStartEvent toolStart:
            _conversationLogger.LogToolStart(toolStart.Data.ToolName, toolStart.Data.Arguments);
            break;
        case ToolExecutionCompleteEvent toolComplete:
            _conversationLogger.LogToolComplete(toolComplete.Data.ToolCallId, toolComplete.Data.Success);
            break;
    }
});
```

Review `ConversationLogger` to understand the exact method signatures and adapt accordingly.
