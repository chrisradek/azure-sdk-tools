# S3: ReadFileLinesTool Implementation Plan

## Overview

Create a tool that reads specific line ranges from files, reducing token usage compared to reading entire files.

## Input/Output Schema

**Input:**
- `FilePath` (string): Relative path of the file to read
- `StartLine` (int): Starting line number (1-indexed, inclusive)
- `EndLine` (int, default=-1): Ending line number (1-indexed, inclusive). Use -1 to read to end of file.

**Output:**
- `Content` (string): The content of the requested lines, with line numbers prefixed
- `StartLine` (int): The actual start line returned
- `EndLine` (int): The actual end line returned
- `TotalLines` (int): Total number of lines in the file

## Implementation Steps

1. **Validate file path** using `ToolHelpers.TryGetSafeFullPath` (matches ReadFileTool pattern)
2. **Read file lines** using `File.ReadAllLinesAsync`
3. **Clamp line range** to valid bounds:
   - StartLine clamped to [1, TotalLines]
   - EndLine=-1 means read to end
   - EndLine clamped to [StartLine, TotalLines]
4. **Format output** with line number prefix (e.g., `45. content here`)
5. **Return metadata** including actual range and total lines

## Edge Cases

- StartLine < 1: Clamp to 1
- StartLine > TotalLines: Return empty content
- EndLine=-1: Read to end of file
- EndLine < StartLine: Clamp EndLine to StartLine
- EndLine > TotalLines: Clamp to TotalLines
- Empty file: Return empty content with TotalLines=0

## Reference Pattern

Following `ReadFileTool.cs`:
- Constructor takes `baseDir` parameter
- Uses `ToolHelpers.TryGetSafeFullPath` for path validation
- Throws `ArgumentException` for invalid inputs

## File Location

`tools/azsdk-cli/Azure.Sdk.Tools.Cli/Microagents/Tools/ReadFileLinesTool.cs`
