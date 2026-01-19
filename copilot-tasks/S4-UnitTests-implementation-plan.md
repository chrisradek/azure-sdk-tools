# S4: Unit Tests for Search Tools - Implementation Plan

## Overview
Create unit tests for GlobTool, GrepTool, and ReadFileLinesTool following the existing patterns in the test project.

## Test Framework
- **NUnit** - used across the project
- **TempDirectory helper** - for creating/cleaning up temp files

## Test Structure Pattern (from existing tests)
```csharp
internal class ToolTests
{
    private TempDirectory? _temp;
    private string baseDir => _temp!.DirectoryPath;

    [OneTimeSetUp]
    public void OneTimeSetup() { /* create temp dir and files */ }

    [OneTimeTearDown] 
    public void OneTimeTearDown() { _temp?.Dispose(); }
}
```

## Test Files to Create

### 1. `GlobToolTests.cs`
**Test Scenarios:**
- Find all `.tsp` files with `**/*.tsp`
- Find files in specific directory
- Empty result for non-matching pattern
- Multiple file extensions: `**/*.{tsp,json}`
- Throws on null/empty pattern

### 2. `GrepToolTests.cs`
**Test Scenarios:**
- Find simple string pattern
- Find regex pattern
- Case-insensitive search
- Context lines before/after
- Files-only mode
- MaxResults truncation
- File filtering with glob
- No matches returns empty
- Throws on null/empty pattern
- Throws on invalid regex pattern

### 3. `ReadFileLinesToolTests.cs`
**Test Scenarios:**
- Read specific line range
- Read single line (start == end)
- Read to end with EndLine=-1
- Out-of-range handling (StartLine beyond file)
- Line number prefixes are correct
- Path outside baseDir rejected
- Empty file handling
- File does not exist throws

## Implementation Steps
1. Create test files with appropriate namespaces
2. Set up temp directories with test file structures
3. Implement all test methods
4. Build and run tests

## Test Directory Structure (for all tests)
```
tempDir/
  file1.tsp
  file2.tsp
  subdir/
    nested.tsp
    config.json
  data.txt (multi-line file for ReadFileLines tests)
```
