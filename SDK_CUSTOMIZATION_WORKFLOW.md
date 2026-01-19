# SDK Customization Workflow Tool Documentation

This document describes the `azsdk_sdk_customization_workflow` MCP tool, which coordinates automated fixing of SDK build errors through TypeSpec decorators and SDK code customizations.

## Overview

The SDK Customization Workflow Tool acts as a **state machine coordinator** that:
- Analyzes build errors to determine the best fix approach
- Orchestrates TypeSpec decorator fixes or SDK code patches
- Manages iterative fix attempts with build verification
- Maintains workflow state across multiple tool invocations
- **Enforces continuation** - uses explicit continuation instructions to ensure the agent follows through with each workflow step

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Copilot Agent (Caller)                       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│              SdkCustomizationWorkflowTool                       │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │              State Machine Coordinator                    │   │
│  │  • Tracks workflow phases                                 │   │
│  │  • Returns instructions for Copilot                       │   │
│  │  • Does NOT execute fixes directly                        │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
┌─────────────────┐  ┌────────────────┐  ┌────────────────┐
│  TypeSpec       │  │  azsdk_sdk_fix │  │ azsdk_package_ │
│  Subagent       │  │                │  │ generate/build │
│  (decorators)   │  │  (code patches)│  │                │
└─────────────────┘  └────────────────┘  └────────────────┘
```

## Workflow Phases

The workflow tool now handles regeneration and building **internally**, reducing the number of agent round-trips. The agent only needs to interact for classification and fix application steps.

```mermaid
stateDiagram-v2
    [*] --> Start: New Workflow
    Start --> Classify: Analyze errors
    
    Classify --> AttemptTspFix: tspApplicable=true
    Classify --> AttemptSdkFix: tspApplicable=false
    
    AttemptTspFix --> Success: tsp_fix_applied (regen+build internal)
    AttemptTspFix --> Failure: regen/build failed
    AttemptTspFix --> AttemptSdkFix: tsp_fix_not_applicable
    
    AttemptSdkFix --> Success: sdk_fix_applied (build internal)
    AttemptSdkFix --> Failure: sdk_fix_failed or build failed
    AttemptSdkFix --> Classify: build failed (retry)
    
    Success --> [*]
    Failure --> [*]
```

**Note:** After a fix is applied, the workflow tool automatically:
1. Regenerates the SDK (for TypeSpec fixes only)
2. Builds to verify the fix
3. Returns success, failure, or loops back to classify if build fails

This means the agent only needs to:
1. Classify errors → call workflow with result
2. Apply fix (TypeSpec or SDK) → call workflow with result
3. Workflow handles the rest internally

## Sequence Diagrams

### Successful TypeSpec Fix Flow

```mermaid
sequenceDiagram
    participant Copilot
    participant Workflow as SdkCustomizationWorkflowTool
    participant StateService as WorkflowStateService
    participant Subagent as TypeSpec Subagent

    Note over Copilot: Build fails with errors
    
    Copilot->>Workflow: Start workflow (request, requestType, packagePath)
    Workflow->>StateService: CreateWorkflow(state)
    StateService-->>Workflow: workflowId
    Workflow-->>Copilot: Phase=Classify, Instruction (analyze errors)
    
    Note over Copilot: Copilot uses subagent to classify
    Copilot->>Copilot: Analyze errors with subagent
    
    Copilot->>Workflow: Continue (workflowId, {type: "classification", tspApplicable: true})
    Workflow->>StateService: GetWorkflow(workflowId)
    Workflow-->>Copilot: Phase=AttemptTspFix, UseSubagent (TypeSpec fix prompt)
    
    Note over Copilot: Copilot spawns TypeSpec subagent
    Copilot->>Subagent: Apply TypeSpec decorators
    Subagent-->>Copilot: Decorators applied
    
    Copilot->>Workflow: Continue (workflowId, {type: "tsp_fix_applied", description: "..."})
    
    Note over Workflow: Workflow handles regen+build internally
    Workflow->>Workflow: GenerateSdkAsync (internal)
    Workflow->>Workflow: BuildSdkAsync (internal)
    
    Workflow->>StateService: TryCompleteWorkflow(workflowId)
    Workflow-->>Copilot: Phase=Success, IsComplete=true, Summary
```

### SDK Code Fix Flow (TypeSpec Not Applicable)

```mermaid
sequenceDiagram
    participant Copilot
    participant Workflow as SdkCustomizationWorkflowTool
    participant SdkFix as azsdk_sdk_fix

    Copilot->>Workflow: Start workflow (request, requestType, packagePath)
    Workflow-->>Copilot: Phase=Classify, Instruction
    
    Copilot->>Workflow: Continue ({type: "classification", tspApplicable: false})
    Workflow-->>Copilot: Phase=AttemptSdkFix, RunTool=azsdk_sdk_fix
    
    Copilot->>SdkFix: Fix SDK code (packagePath, errors)
    SdkFix-->>Copilot: Fix applied
    
    Copilot->>Workflow: Continue ({type: "sdk_fix_applied", description: "..."})
    
    Note over Workflow: Workflow handles build internally
    Workflow->>Workflow: BuildSdkAsync (internal)
    
    Workflow-->>Copilot: Phase=Success, IsComplete=true
```

### Iterative Fix Flow (Build Fails, Retry)

```mermaid
sequenceDiagram
    participant Copilot
    participant Workflow as SdkCustomizationWorkflowTool

    Note over Copilot,Workflow: ... fix applied ...
    
    Copilot->>Workflow: Continue ({type: "tsp_fix_applied" or "sdk_fix_applied"})
    
    Note over Workflow: Workflow runs build internally, fails
    Workflow->>Workflow: BuildSdkAsync (internal) - FAILS
    
    alt iteration < maxIterations
        Workflow-->>Copilot: Phase=Classify (iteration 2), Instruction with new errors
        Note over Copilot: Cycle repeats with new errors
    else iteration >= maxIterations
        Workflow-->>Copilot: Phase=Failure, IsComplete=true, Summary
    end
```

## API Reference

### Starting a New Workflow

| Parameter | Required | Description |
|-----------|----------|-------------|
| `request` | Yes | Build errors or user request text |
| `requestType` | Yes | `"build_error"` or `"user_request"` |
| `packagePath` | Yes | Absolute path to SDK package directory |
| `typeSpecPath` | No | Absolute path to TypeSpec project (can be discovered) |
| `maxIterations` | No | Maximum fix iterations (default: 3) |

**Example:**
```json
{
  "request": "error CS0246: The type or name 'MyModel' could not be found",
  "requestType": "build_error",
  "packagePath": "/path/to/azure-sdk-for-net/sdk/storage/Azure.Storage.Blobs",
  "maxIterations": 3
}
```

### Continuing a Workflow

| Parameter | Required | Description |
|-----------|----------|-------------|
| `workflowId` | Yes | Workflow ID from previous call |
| `result` | Yes | JSON result from completed step |

### Result Types

#### Classification Result
```json
{
  "type": "classification",
  "tspApplicable": true
}
```

#### TypeSpec Fix Result
```json
// Success
{
  "type": "tsp_fix_applied",
  "description": "Added @clientName decorator to rename Property1 to Name"
}

// Failure
{
  "type": "tsp_fix_not_applicable",
  "reason": "Error requires implementation changes, not naming"
}
```

#### SDK Fix Result
```json
// Success
{
  "type": "sdk_fix_applied",
  "description": "Updated method signature to match generated code"
}

// Failure
{
  "type": "sdk_fix_failed",
  "reason": "Cannot automatically fix this error type"
}
```

> **Note:** Generate and build operations are now handled internally by the workflow tool. The agent no longer needs to provide `generate_complete` or `build_complete` results.

## Response Structure

Each workflow response includes:

| Field | Description |
|-------|-------------|
| `phase` | Current workflow phase |
| `message` | Human-readable status message |
| `instruction` | Text instructions for Copilot (Classify phase) |
| `expected_result` | Schema for the expected result JSON |
| `use_subagent` | Suggestion to spawn a subagent (TypeSpec fixes) |
| `run_tool` | Tool to execute with arguments |
| `workflow_id` | ID to use for continuation |
| `is_complete` | Whether workflow has finished |
| `status` | Final status (`"success"` or `"failure"`) |
| `summary` | Markdown summary of workflow (on completion) |
| `continuation_required` | **Boolean indicating the agent MUST call back** |
| `continuation_instruction` | **Explicit instruction for how to continue** |
| `progress` | **Step tracking (current/total, completed/remaining steps)** |

### Continuation Enforcement

The tool uses three mechanisms to ensure agents stay on-script:

1. **`continuation_required: true`** - Explicit boolean flag that signals the workflow is not complete
2. **`continuation_instruction`** - Imperative text telling the agent exactly what to do next, e.g.:
   ```
   ⚠️ CRITICAL: After running the build tool, you MUST immediately call the 
   azsdk_sdk_customization_workflow tool again with:
     - workflowId: "workflow-abc123"
     - result: <JSON result of the completed action>
   
   DO NOT proceed to other tasks. DO NOT report completion to the user. 
   The workflow is NOT complete until you receive a response with is_complete=true.
   ```
3. **`progress`** - Visual indicator showing step X of Y to make incomplete state obvious:
   ```json
   {
     "current_step": 2,
     "total_steps": 4,
     "completed_steps": ["Classify"],
     "remaining_steps": ["Regenerate", "Build"]
   }
   ```

### Example Response (Non-Terminal)

```json
{
  "phase": "AttemptTspFix",
  "message": "Attempting to fix with TypeSpec decorators...",
  "use_subagent": {
    "type": "typespec",
    "prompt": "..."
  },
  "expected_result": { ... },
  "workflow_id": "workflow-abc123",
  "is_complete": false,
  "continuation_required": true,
  "continuation_instruction": "⚠️ CRITICAL: After applying TypeSpec decorators, you MUST immediately call the azsdk_sdk_customization_workflow tool again...",
  "progress": {
    "current_step": 2,
    "total_steps": 4,
    "completed_steps": ["Classify"],
    "remaining_steps": ["Regenerate", "Build"]
  }
}
```

### Example Response (Terminal - Success)

```json
{
  "phase": "Success",
  "message": "✅ Build succeeded! Workflow complete.",
  "is_complete": true,
  "status": "success",
  "summary": "## SDK Customization Workflow Summary\n...",
  "workflow_id": "workflow-abc123",
  "continuation_required": false,
  "progress": {
    "current_step": 4,
    "total_steps": 4,
    "completed_steps": ["Classify", "Fix", "Build"],
    "remaining_steps": []
  }
}
```

## Workflow Phases Detail

### 1. Classify Phase

**Purpose:** Analyze build errors to determine if TypeSpec decorators can help.

**What Copilot Does:**
1. Reads `eng/common/knowledge/customizing-client-tsp.md` via subagent
2. Analyzes errors against available TypeSpec customizations
3. Returns classification result

**TypeSpec Can Help With:**
- Naming issues (`@clientName`)
- Visibility/access issues (`@access`)
- Model usage issues (`@usage`)
- Client structure issues (`@client`, `@operationGroup`)

**TypeSpec Cannot Help With:**
- Implementation logic errors
- Missing dependencies
- Syntax errors in customization code

### 2. AttemptTspFix Phase

**Purpose:** Apply TypeSpec decorators to fix the errors.

**What Copilot Does:**
1. Spawns TypeSpec-focused subagent
2. Subagent reads customization documentation
3. Applies appropriate decorators to `client.tsp`
4. Reports what was changed

### 3. Regenerate Phase

**Purpose:** Regenerate SDK code after TypeSpec changes.

**Tool:** `azsdk_package_generate`

### 4. AttemptSdkFix Phase

**Purpose:** Apply patches to SDK customization code.

**Tool:** `azsdk_sdk_fix`

This tool uses language-specific services to automatically patch customization files.

### 5. Build Phase

**Purpose:** Verify the fix by building the SDK.

**Tool:** `azsdk_package_build`

**On Success:** Workflow completes successfully
**On Failure:** 
- If iterations remain: Returns to Classify with new errors
- If max iterations reached: Workflow fails

## State Management

Workflow state is managed by `WorkflowStateService`:

```
┌─────────────────────────────────────────┐
│         WorkflowStateService            │
│                                         │
│  activeWorkflows: ConcurrentDictionary  │
│  completedWorkflowIds: HashSet          │
│                                         │
│  • CreateWorkflow() → workflowId        │
│  • GetWorkflow(id) → state              │
│  • UpdateWorkflow(id, state)            │
│  • TryCompleteWorkflow(id) → bool       │
└─────────────────────────────────────────┘
```

**Notes:**
- State is stored in-memory (lost on server restart)
- Completed workflows cannot be reused
- Each workflow gets a unique GUID-based ID

## Data Models

### WorkflowState

```csharp
class WorkflowState {
    string WorkflowId
    WorkflowPhase Phase
    string EntryType         // "build_error" | "user_request"
    string OriginalRequest
    string CurrentErrors     // Updated on build failure
    string? LastFixType      // "tsp" | "sdk"
    int Iteration
    int MaxIterations
    string PackagePath
    string TypeSpecPath
    List<WorkflowHistoryEntry> History
}
```

### WorkflowHistoryEntry

```csharp
class WorkflowHistoryEntry {
    int Iteration
    WorkflowPhase Phase
    string Action
    string Result           // "success" | "failure" | "skipped"
    string? Details
}
```

## Example Usage

### CLI Usage

```bash
# Start new workflow
azsdk package workflow \
  --request "error CS0246: The type 'MyModel' could not be found" \
  --request-type build_error \
  --package-path /path/to/sdk/package

# Continue workflow
azsdk package workflow \
  --workflow-id workflow-abc123 \
  --result '{"type": "classification", "tspApplicable": true}'
```

### MCP Tool Usage

```json
// Start
{
  "name": "azsdk_sdk_customization_workflow",
  "arguments": {
    "request": "error CS0246: The type 'MyModel' could not be found",
    "requestType": "build_error",
    "packagePath": "/path/to/sdk/package"
  }
}

// Continue
{
  "name": "azsdk_sdk_customization_workflow",
  "arguments": {
    "workflowId": "workflow-abc123",
    "result": "{\"type\": \"classification\", \"tspApplicable\": true}"
  }
}
```

## Complete Workflow Flowchart

```mermaid
flowchart TD
    Start([Start Workflow]) --> ValidateParams{Valid Parameters?}
    ValidateParams -->|No| Error1[Return Error]
    ValidateParams -->|Yes| CreateState[Create WorkflowState]
    CreateState --> StoreState[Store in WorkflowStateService]
    StoreState --> Classify[Phase: Classify]
    
    Classify --> ReturnClassifyInstr[Return: Instruction to analyze errors]
    ReturnClassifyInstr --> CopilotClassify[Copilot analyzes with subagent]
    CopilotClassify --> ContinueClassify[Continue with classification result]
    
    ContinueClassify --> TspApplicable{tspApplicable?}
    TspApplicable -->|Yes| TspFix[Phase: AttemptTspFix]
    TspApplicable -->|No| SdkFix[Phase: AttemptSdkFix]
    
    TspFix --> ReturnTspPrompt[Return: Subagent suggestion]
    ReturnTspPrompt --> CopilotTsp[Copilot spawns TypeSpec subagent]
    CopilotTsp --> ContinueTsp[Continue with TSP result]
    
    ContinueTsp --> TspSuccess{tsp_fix_applied?}
    TspSuccess -->|Yes| Regenerate[Phase: Regenerate]
    TspSuccess -->|No| SdkFix
    
    Regenerate --> ReturnGenTool[Return: RunTool azsdk_package_generate]
    ReturnGenTool --> CopilotGen[Copilot runs generate tool]
    CopilotGen --> ContinueGen[Continue with generate result]
    
    ContinueGen --> GenSuccess{success?}
    GenSuccess -->|Yes| Build[Phase: Build]
    GenSuccess -->|No| Failure1[Phase: Failure]
    
    SdkFix --> ReturnSdkTool[Return: RunTool azsdk_sdk_fix]
    ReturnSdkTool --> CopilotSdk[Copilot runs SDK fix tool]
    CopilotSdk --> ContinueSdk[Continue with SDK fix result]
    
    ContinueSdk --> SdkSuccess{sdk_fix_applied?}
    SdkSuccess -->|Yes| Build
    SdkSuccess -->|No| Failure2[Phase: Failure]
    
    Build --> ReturnBuildTool[Return: RunTool azsdk_package_build]
    ReturnBuildTool --> CopilotBuild[Copilot runs build tool]
    CopilotBuild --> ContinueBuild[Continue with build result]
    
    ContinueBuild --> BuildSuccess{success?}
    BuildSuccess -->|Yes| Success[Phase: Success]
    BuildSuccess -->|No| CheckIter{iteration < max?}
    
    CheckIter -->|Yes| IncrIter[Increment iteration]
    IncrIter --> Classify
    CheckIter -->|No| Failure3[Phase: Failure]
    
    Success --> Complete1([Workflow Complete - Success])
    Failure1 --> Complete2([Workflow Complete - Failure])
    Failure2 --> Complete2
    Failure3 --> Complete2
    Error1 --> Complete2
```

## Related Tools

| Tool | Purpose |
|------|---------|
| `azsdk_sdk_customization_workflow` | Workflow coordinator (this tool) |
| `azsdk_sdk_fix` | Applies patches to SDK customization code |
| `azsdk_package_generate` | Regenerates SDK from TypeSpec |
| `azsdk_package_build` | Builds/compiles the SDK package |

## Related Documentation

- [`eng/common/knowledge/customizing-client-tsp.md`](eng/common/knowledge/customizing-client-tsp.md) - TypeSpec customization reference
