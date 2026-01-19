---
name: sdk-customization-workflow
description: >
  Coordinates fixing SDK build errors through TypeSpec decorators and SDK code customizations.
  Use this skill when asked to fix SDK build errors, apply SDK customizations, or when working
  with TypeSpec client.tsp files in Azure SDK repositories.
license: MIT
---

# SDK Customization Workflow

This skill guides you through fixing SDK build errors using the `azsdk_sdk_customization_workflow` MCP tool. The workflow determines whether errors can be fixed with TypeSpec decorators or require SDK code changes.

## CRITICAL: Workflow Loop Requirement

**YOU MUST ALWAYS call `azsdk_sdk_customization_workflow` after completing each step until `isComplete: true` is returned.**

The workflow tool is a state machine. It tracks progress and tells you what to do next. You are responsible for:
1. Executing the action it requests (run a tool, spawn a subagent, analyze errors)
2. Calling the workflow tool again with your result
3. Repeating until the workflow returns `isComplete: true`

**DO NOT:**
- Stop mid-workflow to report partial progress
- Attempt to fix errors without consulting the workflow
- Skip calling the workflow tool after completing a step
- Consider the task complete until `isComplete: true`

## Starting a Workflow

When you encounter SDK build errors or are asked to fix SDK customization issues:

```
Call: azsdk_sdk_customization_workflow
Arguments:
  request: "<the build errors or user request>"
  requestType: "build_error" or "user_request"
  packagePath: "<absolute path to SDK package>"
  typeSpecPath: "<optional: path to TypeSpec project>"
  maxIterations: 3  (optional, default is 3)
```

## Continuing a Workflow

After each step, the workflow returns instructions. Execute them, then call back:

```
Call: azsdk_sdk_customization_workflow
Arguments:
  workflowId: "<the workflow_id from previous response>"
  result: "<JSON result from your completed action>"
```

## Workflow Phases and Your Actions

### Phase: Classify

**What the workflow asks:** Analyze errors to determine if TypeSpec decorators can help.

**What you do:**
1. Use a subagent to read `eng/common/knowledge/customizing-client-tsp.md`
2. Analyze the errors against available TypeSpec customizations
3. Determine if decorators like `@clientName`, `@access`, `@usage` could fix the issues

**Your result:**
```json
{"type": "classification", "tspApplicable": true}
```
or
```json
{"type": "classification", "tspApplicable": false}
```

**THEN IMMEDIATELY call the workflow tool with this result.**

### Phase: AttemptTspFix

**What the workflow asks:** Apply TypeSpec decorators to fix errors.

**What you do:**
1. Spawn a subagent focused on TypeSpec
2. Have it read the customization documentation
3. Apply appropriate decorators to `client.tsp`

**Your result:**
```json
{"type": "tsp_fix_applied", "description": "Added @clientName decorator to rename X to Y"}
```
or
```json
{"type": "tsp_fix_not_applicable", "reason": "Error requires implementation changes"}
```

**THEN IMMEDIATELY call the workflow tool with this result.**

### Phase: Regenerate

**What the workflow asks:** Run `azsdk_package_generate` tool.

**What you do:**
1. Call the `azsdk_package_generate` tool with the provided arguments
2. Observe the result

**Your result:**
```json
{"type": "generate_complete", "success": true, "output": "Generation completed"}
```
or
```json
{"type": "generate_complete", "success": false, "errors": "<error messages>"}
```

**THEN IMMEDIATELY call the workflow tool with this result.**

### Phase: AttemptSdkFix

**What the workflow asks:** Run `azsdk_sdk_fix` tool.

**What you do:**
1. Call the `azsdk_sdk_fix` tool with the provided arguments
2. Observe the result

**Your result:**
```json
{"type": "sdk_fix_applied", "description": "Updated method signature"}
```
or
```json
{"type": "sdk_fix_failed", "reason": "Cannot automatically fix this error"}
```

**THEN IMMEDIATELY call the workflow tool with this result.**

### Phase: Build

**What the workflow asks:** Run `azsdk_package_build` tool.

**What you do:**
1. Call the `azsdk_package_build` tool with the provided arguments
2. Observe the result

**Your result:**
```json
{"type": "build_complete", "success": true}
```
or
```json
{"type": "build_complete", "success": false, "errors": "<new build errors>"}
```

**THEN IMMEDIATELY call the workflow tool with this result.**

### Phase: Success or Failure

When the workflow returns `isComplete: true`, the workflow is done. Report the outcome to the user using the `summary` field provided.

## Example Complete Workflow

```
User: "Fix the build errors in my SDK package"

You: Call azsdk_sdk_customization_workflow(
  request="error CS0246: The type 'MyModel' could not be found",
  requestType="build_error",
  packagePath="/path/to/sdk/package"
)

Workflow returns: phase=Classify, instruction="Analyze errors..."

You: [Use subagent to analyze]
     Call azsdk_sdk_customization_workflow(
       workflowId="workflow-abc123",
       result='{"type": "classification", "tspApplicable": true}'
     )

Workflow returns: phase=AttemptTspFix, useSubagent={...}

You: [Spawn TypeSpec subagent, apply fix]
     Call azsdk_sdk_customization_workflow(
       workflowId="workflow-abc123",
       result='{"type": "tsp_fix_applied", "description": "Added @clientName"}'
     )

Workflow returns: phase=Regenerate, runTool={name: "azsdk_package_generate", ...}

You: Call azsdk_package_generate(packagePath="/path/to/sdk/package")
     Call azsdk_sdk_customization_workflow(
       workflowId="workflow-abc123",
       result='{"type": "generate_complete", "success": true}'
     )

Workflow returns: phase=Build, runTool={name: "azsdk_package_build", ...}

You: Call azsdk_package_build(packagePath="/path/to/sdk/package")
     Call azsdk_sdk_customization_workflow(
       workflowId="workflow-abc123",
       result='{"type": "build_complete", "success": true}'
     )

Workflow returns: phase=Success, isComplete=true, summary="..."

You: [Report success to user with the summary]
```

## Key Reminders

1. **Always continue the loop** - Call the workflow tool after every action
2. **Use the exact workflowId** - It's returned in every response
3. **Format results as JSON** - Match the expected result format exactly
4. **Don't improvise** - Follow the workflow's instructions precisely
5. **Preserve context** - Use subagents for complex analysis to avoid filling your context

## TypeSpec vs SDK Fixes

**TypeSpec decorators can fix:**
- Naming issues (`@clientName`)
- Visibility/access (`@access`)
- Model usage patterns (`@usage`)
- Client structure (`@client`, `@operationGroup`)

**SDK code fixes are needed for:**
- Implementation logic errors
- Missing method implementations
- Incorrect customization code
- Dependency issues

When in doubt, let the classification phase determine the approach.
