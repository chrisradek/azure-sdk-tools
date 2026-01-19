# Microagent Infrastructure Research

This document details how microagents and prompt templates work in the Azure SDK Tools CLI, based on analysis of the existing codebase.

## Overview

A **microagent** is a lightweight, focused agent designed to perform a specific task. It operates an agent loop (LLM ↔ tool call) until the LLM determines its task is complete, at which point it calls a special "Exit" tool with the result.

---

## Core Components

### 1. `IAgentTool` Interface

**File:** `Azure.Sdk.Tools.Cli/Microagents/IAgentTool.cs`

```csharp
public interface IAgentTool
{
    string Name { get; }           // Tool name exposed to LLM
    string Description { get; }    // Agent-friendly description
    string InputSchema { get; }    // JSON schema of input
    Task<string> Invoke(string input, CancellationToken ct = default);
}
```

### 2. `AgentTool<TInput, TOutput>` Abstract Class

**File:** `Azure.Sdk.Tools.Cli/Microagents/AgentTool.cs`

Provides strongly-typed implementation with automatic JSON serialization:

```csharp
public abstract class AgentTool<TInput, TOutput> : IAgentTool
{
    // Auto-generates JSON schema from TInput type
    public string InputSchema { get; } = ToolHelpers.GetJsonSchemaRepresentation(typeof(TInput));
    
    public abstract string Name { get; init; }
    public abstract string Description { get; init; }
    
    // String-based invoke (IAgentTool) - handles deserialization
    public async Task<string> Invoke(string input, CancellationToken ct = default)
    {
        var deserialized = JsonSerializer.Deserialize<TInput>(input);
        var result = await this.Invoke(deserialized, ct);
        return JsonSerializer.Serialize(result);
    }
    
    // Strongly-typed invoke - implement this
    public abstract Task<TOutput> Invoke(TInput input, CancellationToken ct);
    
    // Factory method for functional tools (no class needed)
    public static AgentTool<TInput, TOutput> FromFunc(
        string name, 
        string description, 
        Func<TInput, CancellationToken, Task<TOutput>> invokeHandler);
}
```

### 3. `Microagent<TResult>` Class

**File:** `Azure.Sdk.Tools.Cli/Microagents/Microagent.cs`

Defines a microagent configuration:

```csharp
public class Microagent<TResult> where TResult : notnull
{
    // System prompt / instructions
    public required string Instructions { get; init; }
    
    // Available tools (ReadFile, WriteFile, etc.)
    public IEnumerable<IAgentTool> Tools { get; init; } = [];
    
    // Model to use (default: "gpt-4.1")
    public string Model { get; init; } = "gpt-4.1";
    
    // Max iterations before failure (default: 100)
    public int MaxToolCalls { get; init; } = 100;
    
    // Optional validation callback - if returns failure, agent retries
    public Func<TResult, Task<MicroagentValidationResult>>? ValidateResult { get; init; }
}
```

### 4. `MicroagentValidationResult`

```csharp
public class MicroagentValidationResult
{
    public required bool Success { get; set; }
    public object? Reason { get; set; }  // Only populated when Success is false
}
```

### 5. `MicroagentHostService`

**File:** `Azure.Sdk.Tools.Cli/Microagents/MicroagentHostService.cs`

Runs the agent loop:

```csharp
public interface IMicroagentHostService
{
    Task<TResult> RunAgentToCompletion<TResult>(
        Microagent<TResult> agentDefinition, 
        CancellationToken ct = default) where TResult : notnull;
}
```

**Key behavior:**
1. Adds special "Exit" tool with schema from `TResult`
2. Initializes conversation with instructions as first user message
3. Loops: call LLM → get tool call → execute tool → add result to history
4. When LLM calls "Exit", validates result (if validator provided)
5. If validation fails, prompts agent to retry
6. Throws exception if `MaxToolCalls` exceeded

---

## Existing Tools

Located in `Azure.Sdk.Tools.Cli/Microagents/Tools/`:

| Tool | Input | Output | Purpose |
|------|-------|--------|---------|
| `ReadFileTool` | `FilePath` (relative) | `FileContent` | Read file within base directory |
| `WriteFileTool` | `FilePath`, `Content` | `Message` | Write file within base directory |
| `ListFilesTool` | `Path`, `Recursive`, `Filter` | `fileNames[]` | List directory contents |
| `ClientCustomizationCodePatchTool` | `FilePath`, `OldContent`, `NewContent` | `Success`, `Message` | Find/replace in file |
| `UpdateCspellWordsTool` | (varies) | (varies) | Add words to cspell dictionary |

**Key Pattern:** Tools take a `baseDir` in constructor for path sandboxing via `ToolHelpers.TryGetSafeFullPath()`.

---

## ToolHelpers

**File:** `Azure.Sdk.Tools.Cli/Microagents/ToolHelpers.cs`

Two key methods:

1. **`GetJsonSchemaRepresentation(Type schema)`** - Generates JSON schema from C# type, including `[Description]` attributes

2. **`TryGetSafeFullPath(string baseDirectory, string relativePath, out string fullPath)`** - Validates path stays within base directory (security sandbox)

---

## Prompt Templates

### Base Class

**File:** `Azure.Sdk.Tools.Cli/Prompts/BasePromptTemplate.cs`

```csharp
public abstract class BasePromptTemplate
{
    public abstract string TemplateId { get; }
    public abstract string Version { get; }
    public abstract string Description { get; }
    
    // Implement this to build the prompt
    public abstract string BuildPrompt();
    
    // Helper that structures prompt with sections
    protected string BuildStructuredPrompt(
        string taskInstructions, 
        string? constraints = null, 
        string? examples = null, 
        string? outputRequirements = null);
    
    // Adds safety guidelines automatically
    protected virtual string BuildSystemRole();
    
    protected virtual string GetDefaultOutputRequirements();
}
```

### Structured Prompt Format

`BuildStructuredPrompt()` creates:

```
## SYSTEM ROLE
You are an AI assistant for Azure SDK development. Your task: {description}.

## SAFETY GUIDELINES
- Follow Azure SDK standards and Microsoft policies
- Do not process or expose sensitive information
- ...

## TASK INSTRUCTIONS
{taskInstructions}

## CONSTRAINTS
{constraints}

## EXAMPLES
{examples}

## OUTPUT REQUIREMENTS
{outputRequirements}
```

### Existing Templates

| Template | Purpose |
|----------|---------|
| `JavaPatchGenerationTemplate` | Guides AI to analyze API changes and create patches for Java customization code |
| `ReadMeGenerationTemplate` | Generates README files for Azure SDK packages |
| `SpellingValidationTemplate` | Validates and fixes spelling using cspell output |

---

## Process Helpers

### Interface Hierarchy

```
IProcessHelper     → ProcessHelper     (generic process)
IPowershellHelper  → PowershellHelper  (PowerShell scripts)
INpxHelper         → NpxHelper         (npx commands)
IMavenHelper       → MavenHelper       (Maven builds)
IPythonHelper      → PythonHelper      (Python scripts)
```

All extend `ProcessHelperBase<T>` which handles:
- Process execution with timeout
- Output/error stream capture
- Logging via `ILogger<T>`

### ProcessOptions

```csharp
public class ProcessOptions : IProcessOptions
{
    public string Command { get; }
    public List<string> Args { get; }
    public string WorkingDirectory { get; }
    public TimeSpan Timeout { get; }
    public bool LogOutputStream { get; }
}
```

### NpxOptions

Extends `ProcessOptions` with npx-specific handling:

```csharp
new NpxOptions(
    "@azure-tools/typespec-client-generator-cli",  // package
    ["tsp-client", "compile"],                      // args
    logOutputStream: true,
    workingDirectory: projectPath,
    timeout: TimeSpan.FromMinutes(30)
);
```

Automatically builds: `npx --yes --package=@package -- args...`

---

## Key Helpers for TypeSpec

### `ITspClientHelper`

```csharp
public interface ITspClientHelper
{
    // Convert Swagger to TypeSpec
    Task<TspToolResponse> ConvertSwaggerAsync(...);
    
    // Regenerate SDK from TypeSpec (tsp-client update)
    Task<TspToolResponse> UpdateGenerationAsync(
        string tspLocationPath, 
        string outputDirectory, 
        string? commitSha = null, 
        bool isCli = false, 
        CancellationToken ct = default);
    
    // Initialize SDK generation (tsp-client init)
    Task<TspToolResponse> InitializeGenerationAsync(...);
}
```

### `ISpecGenSdkConfigHelper`

Reads `eng/swagger_to_sdk_config.json` for build configuration:

```csharp
public interface ISpecGenSdkConfigHelper
{
    // Get config value by JSON path
    Task<T> GetConfigValueFromRepoAsync<T>(string repositoryRoot, string jsonPath);
    
    // Get build/update scripts configuration
    Task<(SpecGenSdkConfigContentType type, string value)> GetConfigurationAsync(
        string repositoryRoot, 
        SpecGenSdkConfigType configType);
    
    // Create process options for execution
    ProcessOptions? CreateProcessOptions(...);
    
    // Execute and return result
    Task<PackageOperationResponse> ExecuteProcessAsync(...);
}
```

Config types: `Build`, `UpdateChangelogContent`, `UpdateVersion`, `UpdateMetadata`

### `ITypeSpecHelper`

```csharp
public interface ITypeSpecHelper
{
    bool IsValidTypeSpecProjectPath(string path);
    bool IsTypeSpecProjectForMgmtPlane(string path);
    bool IsRepoPathForPublicSpecRepo(string path);
    bool IsRepoPathForSpecRepo(string path);
    string GetSpecRepoRootPath(string path);
    string GetTypeSpecProjectRelativePath(string typeSpecProjectPath);
}
```

---

## Creating a New Tool

### Pattern

```csharp
// 1. Define input record with [Description] attributes
public record MyToolInput(
    [property: Description("Description for LLM")] string Param1,
    [property: Description("Another param")] int Param2
);

// 2. Define output record
public record MyToolOutput(
    [property: Description("Whether operation succeeded")] bool Success,
    [property: Description("Result message")] string Message
);

// 3. Implement AgentTool<TInput, TOutput>
public class MyTool : AgentTool<MyToolInput, MyToolOutput>
{
    // Inject dependencies via constructor
    private readonly ISomeHelper _helper;
    
    public MyTool(ISomeHelper helper)
    {
        _helper = helper;
    }
    
    public override string Name { get; init; } = "MyTool";
    public override string Description { get; init; } = "Does something useful";
    
    public override async Task<MyToolOutput> Invoke(MyToolInput input, CancellationToken ct)
    {
        // Implement tool logic
        try
        {
            // ... do work ...
            return new MyToolOutput(true, "Success");
        }
        catch (Exception ex)
        {
            return new MyToolOutput(false, ex.Message);
        }
    }
}
```

---

## Creating a New Microagent

### Pattern

```csharp
// 1. Define result type
public record MyMicroagentResult
{
    public required bool Success { get; init; }
    public required string[] Actions { get; init; }
    public string? Error { get; init; }
}

// 2. Create prompt template
public class MyTemplate : BasePromptTemplate
{
    public override string TemplateId => "my-template";
    public override string Version => "1.0.0";
    public override string Description => "Does X with Y";
    
    private readonly string _context;
    
    public MyTemplate(string context)
    {
        _context = context;
    }
    
    public override string BuildPrompt()
    {
        var instructions = $"Given this context: {_context}\nDo X, Y, Z.";
        var constraints = "Only do A, never do B.";
        var examples = "Example: ...";
        var output = "Return result using Exit tool.";
        
        return BuildStructuredPrompt(instructions, constraints, examples, output);
    }
}

// 3. Create factory (optional but recommended)
public class MyMicroagentFactory
{
    private readonly IDependency _dep;
    
    public MyMicroagentFactory(IDependency dep)
    {
        _dep = dep;
    }
    
    public Microagent<MyMicroagentResult> Create(string context)
    {
        var template = new MyTemplate(context);
        
        return new Microagent<MyMicroagentResult>
        {
            Instructions = template.BuildPrompt(),
            Tools = new IAgentTool[]
            {
                new ReadFileTool(baseDir),
                new WriteFileTool(baseDir),
                new MyCustomTool(_dep),
            },
            Model = "gpt-4.1",
            MaxToolCalls = 20,
            ValidateResult = async (result) =>
            {
                if (result.Success)
                    return new MicroagentValidationResult { Success = true };
                
                // Could also do external validation here
                return new MicroagentValidationResult
                {
                    Success = false,
                    Reason = result.Error
                };
            }
        };
    }
}

// 4. Run the microagent
var factory = new MyMicroagentFactory(dep);
var agent = factory.Create("some context");
var result = await hostService.RunAgentToCompletion(agent, ct);
```

---

## Answers to Plan Questions

Based on this research:

1. **Microagent Base Classes**: All exist - `Microagent<TResult>`, `MicroagentHostService`, `IAgentTool`, `AgentTool<TInput, TOutput>`, `BasePromptTemplate`

2. **NPX Helper**: `INpxHelper` exists in `Helpers/Process/CommandHelpers.cs`. Use this for running `tsp compile`.

3. **Language Service Access**: Not found as a standalone service. The plan's mention of `LanguageService` for `BuildSDKTool` should use `ISpecGenSdkConfigHelper` which reads build configuration from `swagger_to_sdk_config.json`.

4. **Tool Restriction Scope**: Tools like `ReadFileTool` are scoped to a single base directory. To access multiple directories (TypeSpec project + SDK package), either:
   - Pass both paths and create two sets of tools, OR
   - Use a common parent directory, OR
   - Create tools that accept absolute paths (less secure)

5. **Open Questions**:
   - Client.tsp location: Standard is `{typespecProjectPath}/client.tsp`
   - Multi-file: Start simple, can extend later
   - Rollback: Framework doesn't auto-rollback; implement manually if needed
   - Retries: `ValidateResult` callback enables internal retries within `MaxToolCalls`

6. **Testing**: Use mocks for `INpxHelper`, `ITspClientHelper`, `ISpecGenSdkConfigHelper` dependencies.
