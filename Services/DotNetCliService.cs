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
    public async Task<OperationResult> AddPackageAsync(
        string projectPath,
        string packageId,
        string? version = null,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var args = $"add \"{projectPath}\" package {packageId}";
        if (!string.IsNullOrEmpty(version))
        {
            args += $" --version {version}";
        }

        _logService?.LogInfo($"Installing {packageId} to {Path.GetFileNameWithoutExtension(projectPath)}", "CLI");
        return await RunDotNetCommandAsync(args, cancellationToken, progress);
    }

    /// <summary>
    /// Remove a NuGet package from a project
    /// </summary>
    public async Task<OperationResult> RemovePackageAsync(
        string projectPath,
        string packageId,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var args = $"remove \"{projectPath}\" package {packageId}";

        _logService?.LogInfo($"Removing {packageId} from {Path.GetFileNameWithoutExtension(projectPath)}", "CLI");
        return await RunDotNetCommandAsync(args, cancellationToken, progress);
    }

    /// <summary>
    /// Restore packages for a project
    /// </summary>
    public async Task<OperationResult> RestorePackagesAsync(
        string projectPath,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var args = $"restore \"{projectPath}\"";

        _logService?.LogInfo($"Restoring packages for {Path.GetFileNameWithoutExtension(projectPath)}", "CLI");
        return await RunDotNetCommandAsync(args, cancellationToken, progress);
    }

    /// <summary>
    /// Update a package to a specific version (or latest if version is null)
    /// </summary>
    public async Task<OperationResult> UpdatePackageAsync(
        string projectPath,
        string packageId,
        string? version = null,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        // dotnet doesn't have a direct "update" command — we re-add with the new version
        var args = $"add \"{projectPath}\" package {packageId}";
        if (!string.IsNullOrEmpty(version))
        {
            args += $" --version {version}";
        }

        _logService?.LogInfo($"Updating {packageId} in {Path.GetFileNameWithoutExtension(projectPath)}", "CLI");
        return await RunDotNetCommandAsync(args, cancellationToken, progress);
    }

    /// <summary>
    /// Update multiple packages in a batch operation
    /// </summary>
    public async Task<OperationResult> UpdateAllPackagesAsync(
        string projectPath,
        List<(string packageId, string version)> packages,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        var successCount = 0;
        var failureCount = 0;
        var errors = new List<string>();

        _logService?.LogInfo($"Updating {packages.Count} packages in {Path.GetFileNameWithoutExtension(projectPath)}", "CLI");

        for (int i = 0; i < packages.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                var cancelMessage = $"Cancelled after updating {successCount}/{packages.Count} packages";
                _logService?.LogWarning(cancelMessage, "CLI");
                progress?.Report(cancelMessage);
                return OperationResult.FromError("Operation cancelled", cancelMessage);
            }

            var (packageId, version) = packages[i];
            progress?.Report($"[{i + 1}/{packages.Count}] Updating {packageId} to {version}...");

            var result = await UpdatePackageAsync(
                projectPath, packageId, version, cancellationToken, progress);

            if (result.Success)
            {
                successCount++;
                progress?.Report($"✓ {packageId} updated successfully");
            }
            else
            {
                failureCount++;
                var errorMsg = $"✗ {packageId}: {result.Message}";
                errors.Add(errorMsg);
                progress?.Report(errorMsg);
                _logService?.LogWarning($"Failed to update {packageId}: {result.Message}", "CLI");
            }
        }

        var summary = $"Updated {successCount}/{packages.Count} packages";
        _logService?.LogInfo(summary, "CLI");

        if (failureCount > 0)
        {
            var errorDetails = string.Join("\n", errors);
            return OperationResult.FromError(summary, errorDetails);
        }

        return OperationResult.FromSuccess(summary);
    }

    /// <summary>
    /// List all packages (including transitive) for a project
    /// </summary>
    public async Task<List<ProjectDependencyTree>> ListTransitiveDependenciesAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var args = $"list \"{projectPath}\" package --include-transitive";
        _logService?.LogInfo($"Listing transitive dependencies for {Path.GetFileNameWithoutExtension(projectPath)}", "CLI");

        var result = await RunDotNetCommandAsync(args, cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Message))
            return new List<ProjectDependencyTree>();

        return ParseTransitiveDependencyOutput(result.Message, projectPath);
    }

    internal static List<ProjectDependencyTree> ParseTransitiveDependencyOutput(string output, string projectPath)
    {
        var trees = new List<ProjectDependencyTree>();
        var lines = output.Split('\n', StringSplitOptions.None);

        ProjectDependencyTree? currentTree = null;
        bool inTopLevel = false;
        bool inTransitive = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            // Detect framework section: [net9.0]:
            if (line.TrimStart().StartsWith("[") && line.TrimEnd().EndsWith("]:"))
            {
                var framework = line.Trim().TrimStart('[').TrimEnd(':', ']');
                currentTree = new ProjectDependencyTree
                {
                    ProjectName = Path.GetFileNameWithoutExtension(projectPath),
                    TargetFramework = framework
                };
                trees.Add(currentTree);
                inTopLevel = false;
                inTransitive = false;
                continue;
            }

            if (currentTree == null) continue;

            // Detect section headers
            if (line.Contains("Top-level Package"))
            {
                inTopLevel = true;
                inTransitive = false;
                continue;
            }
            if (line.Contains("Transitive Package"))
            {
                inTopLevel = false;
                inTransitive = true;
                continue;
            }

            // Parse package lines (start with > or whitespace followed by >)
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith(">")) continue;

            // Remove the leading "> " marker
            var packageLine = trimmed.Substring(1).Trim();
            var parts = packageLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (inTopLevel && parts.Length >= 3)
            {
                // Top-level: PackageName  RequestedVersion  ResolvedVersion
                currentTree.TopLevelPackages.Add(new DependencyNode
                {
                    PackageId = parts[0],
                    RequestedVersion = parts[1],
                    ResolvedVersion = parts[2],
                    IsTransitive = false
                });
            }
            else if (inTransitive && parts.Length >= 2)
            {
                // Transitive: PackageName  ResolvedVersion
                currentTree.TransitivePackages.Add(new DependencyNode
                {
                    PackageId = parts[0],
                    ResolvedVersion = parts[1],
                    IsTransitive = true
                });
            }
        }

        return trees;
    }

    private async Task<OperationResult> RunDotNetCommandAsync(
        string arguments,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        try
        {
            // Log the full command before execution
            _logService?.LogInfo($"Executing: dotnet {arguments}", "CLI");

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

            // Register cancellation callback to kill process
            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        _logService?.LogWarning("Process killed due to cancellation", "CLI");
                    }
                }
                catch (Exception ex)
                {
                    _logService?.LogError($"Error killing process: {ex.Message}", ex, "CLI");
                    // Non-fatal: process may have already exited
                }
            });

            // Read both streams with progress reporting
            var outputBuilder = new System.Text.StringBuilder();
            var errorBuilder = new System.Text.StringBuilder();

            var stdoutTask = Task.Run(async () =>
            {
                while (await process.StandardOutput.ReadLineAsync() is string line)
                {
                    outputBuilder.AppendLine(line);
                    progress?.Report(line);
                    _logService?.LogDebug($"[stdout] {line}", "CLI");
                }
            }, cancellationToken);

            var stderrTask = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync() is string line)
                {
                    errorBuilder.AppendLine(line);
                    progress?.Report(line);
                    _logService?.LogDebug($"[stderr] {line}", "CLI");
                }
            }, cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(stdoutTask, stderrTask);
            }
            catch (OperationCanceledException)
            {
                _logService?.LogWarning("Operation cancelled by user", "CLI");
                return OperationResult.FromError("Operation cancelled", "The operation was cancelled by the user.");
            }

            var stdout = outputBuilder.ToString().Trim();
            var stderr = errorBuilder.ToString().Trim();

            if (process.ExitCode == 0)
            {
                _logService?.LogInfo($"Command succeeded (exit 0)", "CLI");

                // Check for warnings in stderr even on success
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    _logService?.LogWarning($"Warnings: {stderr}", "CLI");
                }

                return OperationResult.FromSuccess(stdout);
            }

            var errorMessage = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
            // Log command failure with exit code
            _logService?.LogError($"Command failed (exit {process.ExitCode}): {errorMessage}", null, "CLI");
            return OperationResult.FromError(
                $"dotnet {arguments.Split(' ')[0]} failed",
                errorMessage,
                process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            _logService?.LogWarning("Operation cancelled by user", "CLI");
            return OperationResult.FromError("Operation cancelled", "The operation was cancelled by the user.");
        }
        catch (Exception ex)
        {
            // Catch unexpected exceptions (e.g., process start failure)
            _logService?.LogError($"Failed to execute dotnet command: {ex.Message}", ex, "CLI");
            return OperationResult.FromError(
                "Failed to execute dotnet command",
                ex.Message,
                -1);
        }
    }
}
