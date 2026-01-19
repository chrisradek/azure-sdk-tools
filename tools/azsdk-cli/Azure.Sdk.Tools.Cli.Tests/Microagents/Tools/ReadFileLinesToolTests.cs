using Azure.Sdk.Tools.Cli.Microagents.Tools;
using Azure.Sdk.Tools.Cli.Tests.TestHelpers;

namespace Azure.Sdk.Tools.Cli.Tests.Microagents.Tools;

internal class ReadFileLinesToolTests
{
    private TempDirectory? _temp;
    private string baseDir => _temp!.DirectoryPath;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        _temp = TempDirectory.Create("readfilelinestests");

        // Create test files
        // multi-line file for range tests
        File.WriteAllText(Path.Combine(baseDir, "lines.txt"),
            "Line 1\nLine 2\nLine 3\nLine 4\nLine 5\nLine 6\nLine 7\nLine 8\nLine 9\nLine 10");

        // Empty file
        File.WriteAllText(Path.Combine(baseDir, "empty.txt"), "");

        // Single line file
        File.WriteAllText(Path.Combine(baseDir, "single.txt"), "Only one line");

        // Subdirectory for path tests
        Directory.CreateDirectory(Path.Combine(baseDir, "subdir"));
        File.WriteAllText(Path.Combine(baseDir, "subdir", "nested.txt"), "Nested content");
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _temp?.Dispose();
    }

    [Test]
    public async Task ReadFileLines_SpecificLineRange_ReturnsCorrectLines()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act
        var result = await tool.Invoke(new ReadFileLinesInput("lines.txt", StartLine: 3, EndLine: 5), CancellationToken.None);

        // Assert
        Assert.That(result.StartLine, Is.EqualTo(3));
        Assert.That(result.EndLine, Is.EqualTo(5));
        Assert.That(result.TotalLines, Is.EqualTo(10));
        Assert.That(result.Content, Does.Contain("3. Line 3"));
        Assert.That(result.Content, Does.Contain("4. Line 4"));
        Assert.That(result.Content, Does.Contain("5. Line 5"));
        Assert.That(result.Content, Does.Not.Contain("2. Line 2"));
        Assert.That(result.Content, Does.Not.Contain("6. Line 6"));
    }

    [Test]
    public async Task ReadFileLines_SingleLine_ReturnsOneLine()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act
        var result = await tool.Invoke(new ReadFileLinesInput("lines.txt", StartLine: 5, EndLine: 5), CancellationToken.None);

        // Assert
        Assert.That(result.StartLine, Is.EqualTo(5));
        Assert.That(result.EndLine, Is.EqualTo(5));
        Assert.That(result.Content, Is.EqualTo("5. Line 5"));
    }

    [Test]
    public async Task ReadFileLines_EndLineMinusOne_ReadsToEndOfFile()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act
        var result = await tool.Invoke(new ReadFileLinesInput("lines.txt", StartLine: 8, EndLine: -1), CancellationToken.None);

        // Assert
        Assert.That(result.StartLine, Is.EqualTo(8));
        Assert.That(result.EndLine, Is.EqualTo(10));
        Assert.That(result.Content, Does.Contain("8. Line 8"));
        Assert.That(result.Content, Does.Contain("9. Line 9"));
        Assert.That(result.Content, Does.Contain("10. Line 10"));
    }

    [Test]
    public async Task ReadFileLines_StartLineBeyondFile_ReturnsEmptyContent()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act
        var result = await tool.Invoke(new ReadFileLinesInput("lines.txt", StartLine: 100, EndLine: 110), CancellationToken.None);

        // Assert
        Assert.That(result.Content, Is.Empty);
        Assert.That(result.TotalLines, Is.EqualTo(10));
    }

    [Test]
    public async Task ReadFileLines_EndLineBeyondFile_ClampedToFileEnd()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act
        var result = await tool.Invoke(new ReadFileLinesInput("lines.txt", StartLine: 8, EndLine: 100), CancellationToken.None);

        // Assert
        Assert.That(result.StartLine, Is.EqualTo(8));
        Assert.That(result.EndLine, Is.EqualTo(10));
        Assert.That(result.Content, Does.Contain("10. Line 10"));
    }

    [Test]
    public async Task ReadFileLines_LineNumberPrefixesCorrect_MatchesLineNumbers()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act
        var result = await tool.Invoke(new ReadFileLinesInput("lines.txt", StartLine: 1, EndLine: 3), CancellationToken.None);

        // Assert
        var lines = result.Content.Split('\n');
        Assert.That(lines[0], Does.StartWith("1. "));
        Assert.That(lines[1], Does.StartWith("2. "));
        Assert.That(lines[2], Does.StartWith("3. "));
    }

    [Test]
    public void ReadFileLines_PathOutsideBaseDir_ThrowsArgumentException()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act / Assert
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await tool.Invoke(new ReadFileLinesInput("../../../etc/passwd", StartLine: 1, EndLine: 10), CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("invalid or outside the allowed base directory"));
    }

    [Test]
    public async Task ReadFileLines_EmptyFile_ReturnsEmptyContent()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act
        var result = await tool.Invoke(new ReadFileLinesInput("empty.txt", StartLine: 1, EndLine: -1), CancellationToken.None);

        // Assert
        Assert.That(result.Content, Is.Empty);
        Assert.That(result.TotalLines, Is.EqualTo(0));
    }

    [Test]
    public void ReadFileLines_FileDoesNotExist_ThrowsArgumentException()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act / Assert
        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
            await tool.Invoke(new ReadFileLinesInput("nonexistent.txt", StartLine: 1, EndLine: 10), CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("does not exist"));
    }

    [Test]
    public void ReadFileLines_NullOrEmptyPath_ThrowsArgumentException()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act / Assert
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await tool.Invoke(new ReadFileLinesInput("", StartLine: 1, EndLine: 10), CancellationToken.None));
    }

    [Test]
    public async Task ReadFileLines_NestedFile_ReadsCorrectly()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act
        var result = await tool.Invoke(new ReadFileLinesInput(Path.Join("subdir", "nested.txt"), StartLine: 1, EndLine: -1), CancellationToken.None);

        // Assert
        Assert.That(result.Content, Does.Contain("Nested content"));
    }

    [Test]
    public async Task ReadFileLines_StartLineZero_ClampedToOne()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act
        var result = await tool.Invoke(new ReadFileLinesInput("lines.txt", StartLine: 0, EndLine: 2), CancellationToken.None);

        // Assert
        Assert.That(result.StartLine, Is.EqualTo(1));
        Assert.That(result.Content, Does.Contain("1. Line 1"));
    }

    [Test]
    public async Task ReadFileLines_NegativeStartLine_ClampedToOne()
    {
        // Arrange
        var tool = new ReadFileLinesTool(baseDir);

        // Act
        var result = await tool.Invoke(new ReadFileLinesInput("lines.txt", StartLine: -5, EndLine: 2), CancellationToken.None);

        // Assert
        Assert.That(result.StartLine, Is.EqualTo(1));
    }
}
