// Hugin IRC Server - Logs Controller
// Copyright (c) 2024 Hugin Contributors
// Licensed under the MIT License

using Hugin.Server.Api.Hubs;
using Hugin.Server.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hugin.Server.Api.Controllers;

/// <summary>
/// API controller for log access and management.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class LogsController : ControllerBase
{
    private readonly SignalRSerilogSink? _logSink;
    private readonly ILogger<LogsController> _logger;
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogsController"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving optional dependencies.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="environment">Web host environment.</param>
    public LogsController(
        IServiceProvider serviceProvider,
        ILogger<LogsController> logger,
        IWebHostEnvironment environment)
    {
        _logSink = serviceProvider.GetService<SignalRSerilogSink>();
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// Gets recent log entries from the in-memory buffer.
    /// </summary>
    /// <param name="count">Number of entries to retrieve (max 1000).</param>
    /// <param name="level">Minimum log level to filter.</param>
    /// <returns>List of log entries.</returns>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(ApiResponse<LogsResponse>), 200)]
    public ActionResult<ApiResponse<LogsResponse>> GetRecentLogs(
        [FromQuery] int count = 100,
        [FromQuery] string? level = null)
    {
        count = Math.Clamp(count, 1, 1000);

        var logs = _logSink?.GetRecentLogs(count) ?? Enumerable.Empty<LogEntryDto>();

        if (!string.IsNullOrEmpty(level))
        {
            logs = FilterByLevel(logs, level);
        }

        var response = new LogsResponse
        {
            Entries = logs.ToList(),
            TotalCount = logs.Count(),
            HasMore = false
        };

        return Ok(ApiResponse<LogsResponse>.Ok(response));
    }

    /// <summary>
    /// Gets log files available on disk.
    /// </summary>
    /// <returns>List of available log files.</returns>
    [HttpGet("files")]
    [ProducesResponseType(typeof(ApiResponse<LogFilesResponse>), 200)]
    public ActionResult<ApiResponse<LogFilesResponse>> GetLogFiles()
    {
        var logDirectory = GetLogDirectory();
        var files = new List<LogFileInfo>();

        if (Directory.Exists(logDirectory))
        {
            foreach (var file in Directory.GetFiles(logDirectory, "*.log"))
            {
                var fileInfo = new FileInfo(file);
                files.Add(new LogFileInfo
                {
                    Name = fileInfo.Name,
                    Path = fileInfo.Name, // Relative path for security
                    SizeBytes = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    Created = fileInfo.CreationTimeUtc
                });
            }
        }

        var response = new LogFilesResponse
        {
            Files = files.OrderByDescending(f => f.LastModified).ToList(),
            LogDirectory = logDirectory
        };

        return Ok(ApiResponse<LogFilesResponse>.Ok(response));
    }

    /// <summary>
    /// Gets contents of a specific log file with pagination.
    /// </summary>
    /// <param name="fileName">Name of the log file.</param>
    /// <param name="offset">Byte offset to start reading from.</param>
    /// <param name="limit">Maximum bytes to read.</param>
    /// <returns>Log file contents.</returns>
    [HttpGet("files/{fileName}")]
    [ProducesResponseType(typeof(ApiResponse<LogFileContentResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<ActionResult<ApiResponse<LogFileContentResponse>>> GetLogFileContent(
        string fileName,
        [FromQuery] long offset = 0,
        [FromQuery] int limit = 65536)
    {
        // Security: Only allow .log files and prevent path traversal
        if (string.IsNullOrWhiteSpace(fileName) || 
            !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("..") ||
            Path.IsPathRooted(fileName))
        {
            return NotFound(ApiResponse<object>.Fail("Log file not found"));
        }

        var logDirectory = GetLogDirectory();
        var filePath = Path.Combine(logDirectory, fileName);

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(ApiResponse<object>.Fail("Log file not found"));
        }

        var fileInfo = new FileInfo(filePath);
        limit = Math.Clamp(limit, 1, 1024 * 1024); // Max 1MB per request

        string content;
        await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            stream.Seek(Math.Min(offset, stream.Length), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var buffer = new char[limit];
            var read = await reader.ReadBlockAsync(buffer, 0, limit);
            content = new string(buffer, 0, read);
        }

        var response = new LogFileContentResponse
        {
            FileName = fileName,
            Content = content,
            Offset = offset,
            BytesRead = content.Length,
            TotalSize = fileInfo.Length,
            HasMore = offset + content.Length < fileInfo.Length
        };

        return Ok(ApiResponse<LogFileContentResponse>.Ok(response));
    }

    /// <summary>
    /// Downloads a log file.
    /// </summary>
    /// <param name="fileName">Name of the log file.</param>
    /// <returns>Log file download.</returns>
    [HttpGet("files/{fileName}/download")]
    [ProducesResponseType(typeof(FileStreamResult), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public IActionResult DownloadLogFile(string fileName)
    {
        // Security: Only allow .log files and prevent path traversal
        if (string.IsNullOrWhiteSpace(fileName) || 
            !fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("..") ||
            Path.IsPathRooted(fileName))
        {
            return NotFound(ApiResponse<object>.Fail("Log file not found"));
        }

        var logDirectory = GetLogDirectory();
        var filePath = Path.Combine(logDirectory, fileName);

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(ApiResponse<object>.Fail("Log file not found"));
        }

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return File(stream, "text/plain", fileName);
    }

    /// <summary>
    /// Clears old log files.
    /// </summary>
    /// <param name="daysToKeep">Number of days of logs to keep.</param>
    /// <returns>Result of cleanup operation.</returns>
    [HttpDelete("files")]
    [ProducesResponseType(typeof(ApiResponse<LogCleanupResponse>), 200)]
    public ActionResult<ApiResponse<LogCleanupResponse>> CleanupLogFiles([FromQuery] int daysToKeep = 7)
    {
        daysToKeep = Math.Clamp(daysToKeep, 1, 365);
        var cutoff = DateTime.UtcNow.AddDays(-daysToKeep);
        var logDirectory = GetLogDirectory();
        var deletedFiles = new List<string>();
        long freedBytes = 0;

        if (Directory.Exists(logDirectory))
        {
            foreach (var file in Directory.GetFiles(logDirectory, "*.log"))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTimeUtc < cutoff)
                {
                    try
                    {
                        var size = fileInfo.Length;
                        System.IO.File.Delete(file);
                        deletedFiles.Add(fileInfo.Name);
                        freedBytes += size;
                        _logger.LogInformation("Deleted old log file: {FileName}", fileInfo.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete log file: {FileName}", fileInfo.Name);
                    }
                }
            }
        }

        var response = new LogCleanupResponse
        {
            DeletedFiles = deletedFiles,
            DeletedCount = deletedFiles.Count,
            FreedBytes = freedBytes
        };

        return Ok(ApiResponse<LogCleanupResponse>.Ok(response));
    }

    private string GetLogDirectory()
    {
        // Default log directory - can be configured
        var baseDir = _environment.ContentRootPath;
        return Path.Combine(baseDir, "logs");
    }

    private static IEnumerable<LogEntryDto> FilterByLevel(IEnumerable<LogEntryDto> logs, string minLevel)
    {
        var levelOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Trace"] = 0,
            ["Debug"] = 1,
            ["Information"] = 2,
            ["Warning"] = 3,
            ["Error"] = 4,
            ["Critical"] = 5
        };

        if (!levelOrder.TryGetValue(minLevel, out var minOrder))
        {
            return logs;
        }

        return logs.Where(l => levelOrder.TryGetValue(l.Level, out var order) && order >= minOrder);
    }
}
