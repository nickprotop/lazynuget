using System.Diagnostics;
using LazyNuGet.Models;
using SharpConsoleUI.Logging;

namespace LazyNuGet.Services;

/// <summary>
/// Service for executing dotnet CLI commands (add, remove, restore packages)
/// </summary>
public class DotNetCliService
{
    private readonly ILogService? _logService;

    public DotNetCliService(ILogService? logService = null)
    {
        _logService = logService;
    }

    /// <summary>
    /// Add a NuGet package to a project
    /// </summary>
    public async Task<OperationResult> AddPackageAsync(string projectPath, string packageId, string? version = null)
    {
        var args = $"add \"{projectPath}\" package {packageId}";
        if (!string.IsNullOrEmpty(version))
        {
            args += $" --version {version}";
        }

        _logService?.LogInfo($"Installing {packageId} to {Path.GetFileNameWithoutExtension(projectPath)}", "CLI");
        return await RunDotNetCommandAsync(args);
    }

    /// <summary>
    /// Remove a NuGet package from a project
    /// </summary>
    public async Task<OperationResult> RemovePackageAsync(string projectPath, string packageId)
    {
        var args = $"remove \"{projectPath}\" package {packageId}";

        _logService?.LogInfo($"Removing {packageId} from {Path.GetFileNameWithoutExtension(projectPath)}", "CLI");
        return await RunDotNetCommandAsync(args);
    }

    /// <summary>
    /// Restore packages for a project
    /// </summary>
    public async Task<OperationResult> RestorePackagesAsync(string projectPath)
    {
        var args = $"restore \"{projectPath}\"";

        _logService?.LogInfo($"Restoring packages for {Path.GetFileNameWithoutExtension(projectPath)}", "CLI");
        return await RunDotNetCommandAsync(args);
    }

    /// <summary>
    /// Update a package to a specific version (or latest if version is null)
    /// </summary>
    public async Task<OperationResult> UpdatePackageAsync(string projectPath, string packageId, string? version = null)
    {
        // dotnet doesn't have a direct "update" command — we re-add with the new version
        var args = $"add \"{projectPath}\" package {packageId}";
        if (!string.IsNullOrEmpty(version))
        {
            args += $" --version {version}";
        }

        _logService?.LogInfo($"Updating {packageId} in {Path.GetFileNameWithoutExtension(projectPath)}", "CLI");
        return await RunDotNetCommandAsync(args);
    }

    private async Task<OperationResult> RunDotNetCommandAsync(string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            // Read both streams concurrently to avoid deadlocks
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                _logService?.LogInfo($"Command succeeded: dotnet {arguments}", "CLI");
                return OperationResult.FromSuccess(stdout.Trim());
            }

            var errorMessage = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
            _logService?.LogError($"Command failed (exit {process.ExitCode}): {errorMessage}", null, "CLI");
            return OperationResult.FromError(
                $"dotnet {arguments.Split(' ')[0]} failed",
                errorMessage,
                process.ExitCode);
        }
        catch (Exception ex)
        {
            _logService?.LogError($"Failed to execute dotnet command: {ex.Message}", ex, "CLI");
            return OperationResult.FromError(
                "Failed to execute dotnet command",
                ex.Message,
                -1);
        }
    }
}
