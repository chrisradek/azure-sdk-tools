# S2: GrepTool Implementation Plan

## Overview

Create a GrepTool that searches file contents for regex patterns, supporting file filtering, context lines, and files-only mode.

## File Location

`tools/azsdk-cli/Azure.Sdk.Tools.Cli/Microagents/Tools/GrepTool.cs`

## Input/Output Schemas

### Input
- `Pattern` (string, required): Regex pattern to search for
- `FilePattern` (string?, optional): Glob pattern to filter files, defaults to all files
- `IgnoreCase` (bool, optional): Case-insensitive matching, defaults to false
- `ContextBefore` (int, optional): Lines before each match, defaults to 0
- `ContextAfter` (int, optional): Lines after each match, defaults to 0
- `FilesOnly` (bool, optional): Return only file paths, defaults to false
- `MaxResults` (int, optional): Maximum results to return, defaults to 100

### Output
- `Matches` (GrepMatch[]): Array of matches with File, Line, Content
- `TotalMatches` (int): Total matches found
- `Truncated` (bool): Whether results were truncated

## Implementation Details

### 1. File Discovery
- Use `Microsoft.Extensions.FileSystemGlobbing.Matcher` (like GlobTool)
- Default to `**/*` if no FilePattern provided
- Exclude common non-text directories: `node_modules`, `.git`, `bin`, `obj`

### 2. Binary File Detection
- Read first 8KB of each file
- Skip files containing null bytes (0x00) in the sample

### 3. Regex Matching
- Use `System.Text.RegularExpressions.Regex`
- Apply `RegexOptions.Compiled` for performance
- Apply 5-second timeout per file using `Regex.Match(input, pattern, options, timeout)`

### 4. Context Lines
- Read file into memory as lines array
- For each match, include ContextBefore and ContextAfter lines
- Format as "lineNumber: content" for each line in the context

### 5. FilesOnly Mode
- Track unique files with matches
- Stop processing a file after first match
- Return only file paths (no line content)

### 6. MaxResults Enforcement
- Count matches as they're found
- Stop processing when MaxResults reached
- Set Truncated=true if limit was hit

## Dependencies

- `Microsoft.Extensions.FileSystemGlobbing` (already added for GlobTool)
- `System.Text.RegularExpressions` (built into .NET)

## Error Handling

- Invalid regex pattern: throw ArgumentException with clear message
- File read errors: skip file and continue
- Regex timeout: skip file and continue
- Path traversal: use `ToolHelpers.TryGetSafeFullPath` for validation

## Build Verification

```bash
dotnet build tools/azsdk-cli/Azure.Sdk.Tools.Cli/Azure.Sdk.Tools.Cli.csproj
```
