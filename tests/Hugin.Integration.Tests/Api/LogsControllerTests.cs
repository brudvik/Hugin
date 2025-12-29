// Hugin IRC Server - Logs Controller Tests
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using FluentAssertions;
using Hugin.Server.Api.Controllers;
using Hugin.Server.Api.Hubs;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hugin.Integration.Tests.Api;

/// <summary>
/// Unit tests for the LogsController.
/// </summary>
public sealed class LogsControllerTests
{
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ILogger<LogsController>> _loggerMock;
    private readonly Mock<IWebHostEnvironment> _environmentMock;
    private readonly LogsController _controller;

    public LogsControllerTests()
    {
        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<LogsController>>();
        _environmentMock = new Mock<IWebHostEnvironment>();
        
        // Setup environment
        _environmentMock
            .Setup(x => x.ContentRootPath)
            .Returns(Path.GetTempPath());

        _controller = new LogsController(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _environmentMock.Object);
    }

    [Fact]
    public void GetRecentLogsReturnsEmptyWhenNoSinkConfigured()
    {
        // Arrange
        _serviceProviderMock
            .Setup(x => x.GetService(typeof(SignalRSerilogSink)))
            .Returns((object?)null);

        // Act
        var result = _controller.GetRecentLogs(100, null);

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<LogsResponse>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data!.Entries.Should().BeEmpty();
        response.Data.TotalCount.Should().Be(0);
    }

    [Fact]
    public void GetRecentLogsLimitsCountTo1000()
    {
        // Arrange - request more than max
        _serviceProviderMock
            .Setup(x => x.GetService(typeof(SignalRSerilogSink)))
            .Returns((object?)null);

        // Act
        var result = _controller.GetRecentLogs(5000, null);

        // Assert - should not throw, should be limited internally
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<LogsResponse>>().Subject;
        response.Success.Should().BeTrue();
    }

    [Fact]
    public void GetLogFilesReturnsEmptyWhenNoLogDirectory()
    {
        // Arrange - use a temp directory that doesn't have logs
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _environmentMock
            .Setup(x => x.ContentRootPath)
            .Returns(tempDir);

        var controller = new LogsController(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _environmentMock.Object);

        // Act
        var result = controller.GetLogFiles();

        // Assert
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<ApiResponse<LogFilesResponse>>().Subject;
        
        response.Success.Should().BeTrue();
        response.Data!.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLogFileContentReturnsNotFoundForMissingFile()
    {
        // Act
        var result = await _controller.GetLogFileContent("nonexistent.log", 0, 1024);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetLogFileContentRejectsPathTraversal()
    {
        // Act
        var result = await _controller.GetLogFileContent("../../../etc/passwd", 0, 1024);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetLogFileContentRejectsNonLogFiles()
    {
        // Act
        var result = await _controller.GetLogFileContent("config.json", 0, 1024);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetLogFileContentRejectsAbsolutePaths()
    {
        // Act
        var result = await _controller.GetLogFileContent("C:\\Windows\\System32\\config.log", 0, 1024);

        // Assert
        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void DownloadLogFileReturnsNotFoundForMissingFile()
    {
        // Act
        var result = _controller.DownloadLogFile("nonexistent.log");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void DownloadLogFileRejectsPathTraversal()
    {
        // Act
        var result = _controller.DownloadLogFile("..\\..\\secret.log");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void CleanupLogFilesWithValidDaysReturnsSuccess()
    {
        // Arrange - create temp log directory
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var logDir = Path.Combine(tempDir, "logs");
        Directory.CreateDirectory(logDir);

        _environmentMock
            .Setup(x => x.ContentRootPath)
            .Returns(tempDir);

        var controller = new LogsController(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _environmentMock.Object);

        try
        {
            // Act
            var result = controller.CleanupLogFiles(7);

            // Assert
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var response = okResult.Value.Should().BeOfType<ApiResponse<LogCleanupResponse>>().Subject;
            
            response.Success.Should().BeTrue();
            response.Data!.DeletedCount.Should().Be(0); // No old files
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(logDir))
            {
                Directory.Delete(logDir, true);
            }
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void CleanupLogFilesClampsDaysToValidRange()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        _environmentMock
            .Setup(x => x.ContentRootPath)
            .Returns(tempDir);

        var controller = new LogsController(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _environmentMock.Object);

        try
        {
            // Act - request negative days (should be clamped to 1)
            var result = controller.CleanupLogFiles(-5);

            // Assert - should succeed without error
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var response = okResult.Value.Should().BeOfType<ApiResponse<LogCleanupResponse>>().Subject;
            response.Success.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task GetLogFileContentReadsExistingFile()
    {
        // Arrange - create a temp log file
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var logDir = Path.Combine(tempDir, "logs");
        Directory.CreateDirectory(logDir);
        
        var logFile = Path.Combine(logDir, "test.log");
        var logContent = "2024-01-01 12:00:00 [INFO] Test log entry\n2024-01-01 12:00:01 [INFO] Another entry";
        await File.WriteAllTextAsync(logFile, logContent);

        _environmentMock
            .Setup(x => x.ContentRootPath)
            .Returns(tempDir);

        var controller = new LogsController(
            _serviceProviderMock.Object,
            _loggerMock.Object,
            _environmentMock.Object);

        try
        {
            // Act
            var result = await controller.GetLogFileContent("test.log", 0, 1024);

            // Assert
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var response = okResult.Value.Should().BeOfType<ApiResponse<LogFileContentResponse>>().Subject;
            
            response.Success.Should().BeTrue();
            response.Data!.FileName.Should().Be("test.log");
            response.Data.Content.Should().Contain("Test log entry");
            response.Data.HasMore.Should().BeFalse();
        }
        finally
        {
            // Cleanup
            if (File.Exists(logFile))
            {
                File.Delete(logFile);
            }
            if (Directory.Exists(logDir))
            {
                Directory.Delete(logDir, true);
            }
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
