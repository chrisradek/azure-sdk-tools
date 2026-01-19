# Task A1: TypeSpecCustomizationTemplate Implementation

## What Was Implemented

Created `tools/azsdk-cli/Azure.Sdk.Tools.Cli/Prompts/Templates/TypeSpecCustomizationTemplate.cs` - a prompt template class for guiding AI agents to apply TypeSpec client.tsp customizations.

### Template Metadata
- **TemplateId**: `typespec-customization`
- **Version**: `1.0.0`
- **Description**: `Apply TypeSpec client.tsp customizations`

### Constructor Parameters
1. `customizationRequest` - The request (build error, user prompt, API feedback, etc.)
2. `typespecProjectPath` - Path to the TypeSpec project (contains tspconfig.yaml)
3. `referenceDocPath` - Path to the `customizing-client-tsp.md` reference document

### BuildPrompt() Implementation
The method:
1. Reads the reference document content from `referenceDocPath` at runtime using `File.ReadAllText()`
2. Builds a structured prompt using the inherited `BuildStructuredPrompt()` helper with:
   - **Task Instructions**: Includes the full reference document content, customization request, and project path
   - **Constraints**: Rules for only modifying client.tsp, incremental changes, compilation verification
   - **Examples**: Common customization patterns (renaming, access control, language-specific, custom clients)
   - **Output Requirements**: Workflow steps and expected summary format

## Key Design Decisions

1. **Runtime File Reading**: The reference document is read at runtime in `BuildPrompt()` rather than in the constructor. This ensures the most current version of the reference doc is used and follows the pattern of other templates that process content during prompt building.

2. **Static Helper Methods**: Following the pattern from `JavaPatchGenerationTemplate`, the constraint, example, and output requirement builders are static methods since they don't depend on instance state.

3. **Comprehensive Reference Documentation**: The entire `customizing-client-tsp.md` content is embedded in the prompt to give the agent full context about available decorators and patterns.

4. **Incremental Change Workflow**: The template emphasizes applying changes one at a time and compiling after each, with rollback on failure - this mirrors the requirements for robust, iterative customization.

5. **Read-Only Emphasis**: The constraints section clearly states that only `client.tsp` should be modified, while `main.tsp` and `tspconfig.yaml` are read-only references.

## Deviations from Plan

None - the implementation follows the requirements as specified.

## Files Created

- `tools/azsdk-cli/Azure.Sdk.Tools.Cli/Prompts/Templates/TypeSpecCustomizationTemplate.cs`

## Verification

- Build succeeded with 0 warnings and 0 errors
