using Azure.Sdk.Tools.Cli.CopilotAgents;
using Azure.Sdk.Tools.Cli.Helpers;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Azure.Sdk.Tools.Cli.Tests.CopilotAgents;

/// <summary>
/// Tests for CopilotAgentRunner.
/// Uses interface mocking (ICopilotClientWrapper, ICopilotSessionWrapper) to simulate
/// Copilot SDK behavior without requiring actual CLI connections.
/// </summary>
[TestFixture]
internal class CopilotAgentRunnerTests
{
    private Mock<ILogger<CopilotAgentRunner>> _loggerMock;
    private Mock<ILogger<ConversationLogger>> _conversationLoggerLoggerMock;
    private TokenUsageHelper _tokenUsageHelper;
    private ConversationLogger _conversationLogger;
    private Mock<ICopilotClientWrapper> _clientMock;
    private Mock<ICopilotSessionWrapper> _sessionMock;
    private List<SessionEventHandler> _eventHandlers;
    private ICollection<AIFunction>? _capturedTools;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<CopilotAgentRunner>>();
        _conversationLoggerLoggerMock = new Mock<ILogger<ConversationLogger>>();
        
        // Setup conversation logger to be disabled by default
        _conversationLoggerLoggerMock.Setup(l => l.IsEnabled(LogLevel.Debug))
            .Returns(false);
        _conversationLogger = new ConversationLogger(_conversationLoggerLoggerMock.Object);
        _tokenUsageHelper = new TokenUsageHelper(Mock.Of<IRawOutputHelper>());

        _eventHandlers = new List<SessionEventHandler>();
        _capturedTools = null;
        
        // Setup session mock
        _sessionMock = new Mock<ICopilotSessionWrapper>();
        _sessionMock.Setup(s => s.On(It.IsAny<SessionEventHandler>()))
            .Callback<SessionEventHandler>(handler => _eventHandlers.Add(handler))
            .Returns(() => Mock.Of<IDisposable>());
        _sessionMock.Setup(s => s.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        // Setup client mock
        _clientMock = new Mock<ICopilotClientWrapper>();
        _clientMock.Setup(c => c.CreateSessionAsync(
                It.IsAny<SessionConfig>(),
                It.IsAny<ICollection<AIFunction>>(),
                It.IsAny<CancellationToken>()))
            .Callback<SessionConfig?, ICollection<AIFunction>, CancellationToken>((config, tools, ct) =>
            {
                _capturedTools = tools;
            })
            .ReturnsAsync(_sessionMock.Object);
    }

    private void DispatchEvent(SessionEvent evt)
    {
        foreach (var handler in _eventHandlers.ToArray())
        {
            handler(evt);
        }
    }

    private void SimulateExitToolCall(string result)
    {
        // Find and invoke the Exit tool
        var exitTool = _capturedTools?.FirstOrDefault(t => t.Name == "Exit");
        if (exitTool != null)
        {
            var args = new AIFunctionArguments { ["result"] = result };
            _ = exitTool.InvokeAsync(args);
        }
    }

    private void SimulateUsageEvent(int inputTokens, int outputTokens, string model)
    {
        var usageEvent = new AssistantUsageEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Data = new AssistantUsageData
            {
                Model = model,
                InputTokens = inputTokens,
                OutputTokens = outputTokens
            }
        };
        DispatchEvent(usageEvent);
    }

    [Test]
    public async Task RunAsync_WithExitTool_ReturnsResult()
    {
        // Arrange
        const string expectedResult = "Success";
        
        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                // SendAsync blocks until completion, so Exit tool is invoked during the call
                SimulateExitToolCall(expectedResult);
            })
            .ReturnsAsync("msg-id");

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            _tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Test agent"
        };

        // Act
        var result = await runner.RunAsync(agent);

        // Assert
        Assert.That(result, Is.EqualTo(expectedResult));
    }

    [Test]
    public async Task RunAsync_WithValidationSuccess_ReturnsResult()
    {
        // Arrange
        const string expectedResult = "ValidResult";
        var validationCalled = false;

        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                SimulateExitToolCall(expectedResult);
            })
            .ReturnsAsync("msg-id");

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            _tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Test agent",
            ValidateResult = (result) =>
            {
                validationCalled = true;
                Assert.That(result, Is.EqualTo(expectedResult));
                return Task.FromResult(new CopilotAgentValidationResult { Success = true });
            }
        };

        // Act
        var result = await runner.RunAsync(agent);

        // Assert
        Assert.That(result, Is.EqualTo(expectedResult));
        Assert.That(validationCalled, Is.True, "Validation callback should have been called");
    }

    [Test]
    public async Task RunAsync_WithValidationFailureThenSuccess_Retries()
    {
        // Arrange
        var validationCallCount = 0;
        var sendCallCount = 0;

        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                sendCallCount++;
                // First call returns "Invalid", second call returns "Valid"
                SimulateExitToolCall(sendCallCount == 1 ? "InvalidResult" : "ValidResult");
            })
            .ReturnsAsync("msg-id");

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            _tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Test agent",
            ValidateResult = (result) =>
            {
                validationCallCount++;
                if (result == "InvalidResult")
                {
                    return Task.FromResult(new CopilotAgentValidationResult
                    {
                        Success = false,
                        Reason = "Result is invalid"
                    });
                }
                return Task.FromResult(new CopilotAgentValidationResult { Success = true });
            }
        };

        // Act
        var result = await runner.RunAsync(agent);

        // Assert
        Assert.That(result, Is.EqualTo("ValidResult"));
        Assert.That(validationCallCount, Is.EqualTo(2), "Validation should have been called twice");
    }

    [Test]
    public void RunAsync_MaxIterationsExceeded_ThrowsException()
    {
        // Arrange
        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                SimulateExitToolCall("AlwaysInvalid");
            })
            .ReturnsAsync("msg-id");

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            _tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Test agent",
            MaxIterations = 3,
            ValidateResult = (_) => Task.FromResult(new CopilotAgentValidationResult
            {
                Success = false,
                Reason = "Always fails"
            })
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await runner.RunAsync(agent);
        });

        Assert.That(ex.Message, Does.Contain("3 iterations"));
    }

    [Test]
    public void RunAsync_SessionError_ThrowsException()
    {
        // Arrange - session error is now thrown as an exception from SendAsync
        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error message"));

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            _tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Test agent"
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await runner.RunAsync(agent);
        });

        Assert.That(ex.Message, Does.Contain("Test error message"));
    }

    [Test]
    public void RunAsync_NoExitToolCalled_ThrowsException()
    {
        // Arrange - SendAsync completes but Exit tool was not called
        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("msg-id");  // Don't call SimulateExitToolCall

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            _tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Test agent"
        };

        // Act & Assert
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await runner.RunAsync(agent);
        });

        Assert.That(ex.Message, Does.Contain("without calling Exit"));
    }

    [Test]
    public async Task RunAsync_TokenUsageTracking_AddsTokens()
    {
        // Arrange
        var outputHelper = new Mock<IRawOutputHelper>();
        var tokenUsageHelper = new TokenUsageHelper(outputHelper.Object);

        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                // Events are dispatched during SendAsync
                SimulateUsageEvent(100, 50, "gpt-5");
                SimulateExitToolCall("Success");
            })
            .ReturnsAsync("msg-id");

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Test agent"
        };

        // Act
        await runner.RunAsync(agent);

        // Assert
        Assert.That(tokenUsageHelper.TotalTokens, Is.EqualTo(150));
    }

    [Test]
    public void RunAsync_Cancellation_ThrowsTaskCanceledException()
    {
        // Arrange - SendAsync respects cancellation token
        using var cts = new CancellationTokenSource();
        
        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .Returns<MessageOptions, CancellationToken>((options, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult("msg-id");
            });

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            _tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Test agent"
        };

        // Cancel before running
        cts.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await runner.RunAsync(agent, cts.Token);
        });
    }

    [Test]
    public async Task RunAsync_WithCustomTools_ToolsAreRegistered()
    {
        // Arrange
        var customTool = AIFunctionFactory.Create(() => "Tool result", "CustomTool", "A custom test tool");

        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                SimulateExitToolCall("Success");
            })
            .ReturnsAsync("msg-id");

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            _tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Test agent",
            Tools = [customTool]
        };

        // Act
        var result = await runner.RunAsync(agent);

        // Assert
        Assert.That(result, Is.EqualTo("Success"));
        // Verify that tools were passed to session config (CustomTool + Exit)
        Assert.That(_capturedTools, Has.Count.EqualTo(2));
        Assert.That(_capturedTools!.Any(t => t.Name == "CustomTool"), Is.True);
        Assert.That(_capturedTools!.Any(t => t.Name == "Exit"), Is.True);
    }

    [Test]
    public async Task RunAsync_ValidationFailureWithObjectReason_SerializesReason()
    {
        // Arrange
        var validationCallCount = 0;
        var sendCallCount = 0;
        string? capturedPrompt = null;

        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .Callback<MessageOptions, CancellationToken>((options, ct) =>
            {
                sendCallCount++;
                if (sendCallCount > 1)
                {
                    capturedPrompt = options.Prompt;
                }
                SimulateExitToolCall(sendCallCount == 1 ? "FirstResult" : "SecondResult");
            })
            .ReturnsAsync("msg-id");

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            _tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Test agent",
            ValidateResult = (result) =>
            {
                validationCallCount++;
                if (validationCallCount == 1)
                {
                    return Task.FromResult(new CopilotAgentValidationResult
                    {
                        Success = false,
                        Reason = new { Error = "Invalid", Code = 123 }
                    });
                }
                return Task.FromResult(new CopilotAgentValidationResult { Success = true });
            }
        };

        // Act
        var result = await runner.RunAsync(agent);

        // Assert
        Assert.That(result, Is.EqualTo("SecondResult"));
        Assert.That(validationCallCount, Is.EqualTo(2));
        // Verify the prompt contains the serialized validation error
        Assert.That(capturedPrompt, Does.Contain("Error"));
        Assert.That(capturedPrompt, Does.Contain("123"));
    }

    [Test]
    public async Task RunAsync_SessionConfigured_WithCorrectModelAndInstructions()
    {
        // Arrange
        SessionConfig? capturedConfig = null;
        
        _clientMock.Setup(c => c.CreateSessionAsync(
                It.IsAny<SessionConfig>(),
                It.IsAny<ICollection<AIFunction>>(),
                It.IsAny<CancellationToken>()))
            .Callback<SessionConfig?, ICollection<AIFunction>, CancellationToken>((config, tools, ct) =>
            {
                capturedConfig = config;
                _capturedTools = tools;
            })
            .ReturnsAsync(_sessionMock.Object);

        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                SimulateExitToolCall("Success");
            })
            .ReturnsAsync("msg-id");

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            _tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Custom instructions for testing",
            Model = "custom-model"
        };

        // Act
        await runner.RunAsync(agent);

        // Assert
        Assert.That(capturedConfig, Is.Not.Null);
        Assert.That(capturedConfig!.Model, Is.EqualTo("custom-model"));
        Assert.That(capturedConfig.SystemMessage?.Content, Is.EqualTo("Custom instructions for testing"));
        Assert.That(capturedConfig.SystemMessage?.Mode, Is.EqualTo(SystemMessageMode.Append));
    }

    [Test]
    public async Task RunAsync_ValidationWithStringReason_PassesReasonToPrompt()
    {
        // Arrange
        var sendCallCount = 0;
        string? capturedPrompt = null;

        _sessionMock.Setup(s => s.SendAsync(It.IsAny<MessageOptions>(), It.IsAny<CancellationToken>()))
            .Callback<MessageOptions, CancellationToken>((options, ct) =>
            {
                sendCallCount++;
                if (sendCallCount > 1)
                {
                    capturedPrompt = options.Prompt;
                }
                SimulateExitToolCall(sendCallCount == 1 ? "First" : "Second");
            })
            .ReturnsAsync("msg-id");

        var runner = new CopilotAgentRunner(
            _clientMock.Object,
            _tokenUsageHelper,
            _conversationLogger,
            _loggerMock.Object);

        var agent = new CopilotAgent<string>
        {
            Instructions = "Test",
            ValidateResult = (result) =>
            {
                if (result == "First")
                {
                    return Task.FromResult(new CopilotAgentValidationResult
                    {
                        Success = false,
                        Reason = "String validation error"
                    });
                }
                return Task.FromResult(new CopilotAgentValidationResult { Success = true });
            }
        };

        // Act
        await runner.RunAsync(agent);

        // Assert
        Assert.That(capturedPrompt, Does.Contain("String validation error"));
    }
}
