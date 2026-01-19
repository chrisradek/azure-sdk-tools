// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Sdk.Tools.Cli.Helpers;
using Azure.Sdk.Tools.Cli.Microagents.Tools;
using Moq;

namespace Azure.Sdk.Tools.Cli.Tests.Microagents.Tools;

[TestFixture]
internal class CompileTypeSpecToolTests
{
    [Test]
    public async Task Invoke_CompilationSucceeds_ReturnsSuccess()
    {
        // Arrange
        var mockNpxHelper = new Mock<INpxHelper>();
        mockNpxHelper
            .Setup(x => x.Run(It.IsAny<NpxOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult { ExitCode = 0 });

        var tool = new CompileTypeSpecTool("/fake/path", mockNpxHelper.Object);

        // Act
        var result = await tool.Invoke(new CompileTypeSpecInput(), CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Output, Is.EqualTo("Compilation succeeded"));
    }

    [Test]
    public async Task Invoke_CompilationSucceeds_WithOutput_ReturnsOutput()
    {
        // Arrange
        var mockNpxHelper = new Mock<INpxHelper>();
        var processResult = new ProcessResult { ExitCode = 0 };
        processResult.AppendStdout("Compiled successfully with warnings");
        mockNpxHelper
            .Setup(x => x.Run(It.IsAny<NpxOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processResult);

        var tool = new CompileTypeSpecTool("/fake/path", mockNpxHelper.Object);

        // Act
        var result = await tool.Invoke(new CompileTypeSpecInput(), CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Output, Does.Contain("Compiled successfully with warnings"));
    }

    [Test]
    public async Task Invoke_CompilationFails_ReturnsFailureWithErrors()
    {
        // Arrange
        var mockNpxHelper = new Mock<INpxHelper>();
        var processResult = new ProcessResult { ExitCode = 1 };
        processResult.AppendStderr("error: Cannot find module '@typespec/http'");
        mockNpxHelper
            .Setup(x => x.Run(It.IsAny<NpxOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(processResult);

        var tool = new CompileTypeSpecTool("/fake/path", mockNpxHelper.Object);

        // Act
        var result = await tool.Invoke(new CompileTypeSpecInput(), CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Output, Does.Contain("Cannot find module"));
    }

    [Test]
    public async Task Invoke_Timeout_ReturnsTimeoutError()
    {
        // Arrange
        var mockNpxHelper = new Mock<INpxHelper>();
        mockNpxHelper
            .Setup(x => x.Run(It.IsAny<NpxOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var tool = new CompileTypeSpecTool("/fake/path", mockNpxHelper.Object);

        // Act
        var result = await tool.Invoke(new CompileTypeSpecInput(), CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Output, Does.Contain("timed out"));
    }

    [Test]
    public async Task Invoke_Exception_ReturnsErrorMessage()
    {
        // Arrange
        var mockNpxHelper = new Mock<INpxHelper>();
        mockNpxHelper
            .Setup(x => x.Run(It.IsAny<NpxOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("npx not found"));

        var tool = new CompileTypeSpecTool("/fake/path", mockNpxHelper.Object);

        // Act
        var result = await tool.Invoke(new CompileTypeSpecInput(), CancellationToken.None);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Output, Does.Contain("npx not found"));
    }

    [Test]
    public void Invoke_PassesCorrectNpxOptions()
    {
        // Arrange
        var mockNpxHelper = new Mock<INpxHelper>();
        NpxOptions? capturedOptions = null;
        mockNpxHelper
            .Setup(x => x.Run(It.IsAny<NpxOptions>(), It.IsAny<CancellationToken>()))
            .Callback<NpxOptions, CancellationToken>((opts, _) => capturedOptions = opts)
            .ReturnsAsync(new ProcessResult { ExitCode = 0 });

        var tool = new CompileTypeSpecTool("/my/typespec/project", mockNpxHelper.Object);

        // Act
        tool.Invoke(new CompileTypeSpecInput(), CancellationToken.None).Wait();

        // Assert
        Assert.That(capturedOptions, Is.Not.Null);
        Assert.That(capturedOptions!.WorkingDirectory, Is.EqualTo("/my/typespec/project"));
        Assert.That(capturedOptions.Args, Does.Contain("tsp"));
        Assert.That(capturedOptions.Args, Does.Contain("compile"));
        Assert.That(capturedOptions.Args, Does.Contain("./client.tsp"));
    }
}
