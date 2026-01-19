# S1: GlobTool Implementation Plan

## Overview

Create a GlobTool that finds files by name patterns using `Microsoft.Extensions.FileSystemGlobbing`.

## Prerequisites

- [x] `Microsoft.Extensions.FileSystemGlobbing` NuGet package is already referenced in `Azure.Sdk.Tools.Cli.csproj` (line 45)

## Implementation Details

### File Location
`tools/azsdk-cli/Azure.Sdk.Tools.Cli/Microagents/Tools/GlobTool.cs`

### Input/Output Schema

```csharp
public record GlobInput(
    [property: Description("Glob pattern to match files (e.g., '**/*.tsp', 'src/**/*.cs')")]
    string Pattern
);

public record GlobOutput(
    [property: Description("List of matching file paths relative to base directory")]
    string[] Files
);
```

### Implementation Approach

1. Use `Matcher` from `Microsoft.Extensions.FileSystemGlobbing`
2. Constructor takes `baseDir` parameter (consistent with `ReadFileTool`)
3. Add the glob pattern via `matcher.AddInclude(pattern)`
4. Execute against the `baseDir` using `DirectoryInfoWrapper`
5. Return matched file paths relative to `baseDir`

### Key Classes Used

- `Microsoft.Extensions.FileSystemGlobbing.Matcher` - The core glob matching engine
- `Microsoft.Extensions.FileSystemGlobbing.Abstractions.DirectoryInfoWrapper` - Wraps DirectoryInfo for the matcher

### Pattern Support

Standard glob patterns supported by `FileSystemGlobbing`:
- `*` - matches any characters within a path segment
- `**` - matches any characters across multiple path segments (recursive)
- `?` - matches a single character
- `{a,b}` - matches either `a` or `b`

### Edge Cases

1. **Empty pattern**: Throw `ArgumentException`
2. **No matches**: Return empty array (not an error)
3. **Invalid pattern**: Let the Matcher handle it (throws on truly invalid patterns)

## Verification

```bash
dotnet build tools/azsdk-cli/Azure.Sdk.Tools.Cli/Azure.Sdk.Tools.Cli.csproj
```

## Acceptance Criteria

- [x] Tool finds files matching glob patterns
- [x] Paths are relative to baseDir
- [x] Handles nested directories with `**`
- [x] Returns empty array if no matches
