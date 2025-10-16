using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMCP.MCP.Services;

namespace RoslynMCP.MCP.Tools
{

    [McpServerToolType]
    public static partial class RoslynAnalysisTools
    {
        /// <summary>
        /// Ensure solution is loaded and services are initialized
        /// </summary>
        private static async Task<(bool success, string error)> EnsureSolutionLoadedAsync(
            IServiceProvider? serviceProvider, ILogger? logger = null)
        {
            // Get MCP service manager
            var mcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();
            if (mcpServiceManager == null)
            {
                return (false, "MCP service manager unavailable");
            }

            // Check if solution needs to be loaded
            if (!mcpServiceManager.IsLoaded)
            {
                var solutionPath = mcpServiceManager.CurrentSolutionPath;
                if (string.IsNullOrEmpty(solutionPath))
                {
                    return (false, "No solution path configured");
                }
                logger?.LogDebug("Solution not loaded, initializing service: {SolutionPath}", solutionPath);
                var loadResult = await mcpServiceManager.LoadSolutionAsync(solutionPath);
                
                // Check load result
                if (!mcpServiceManager.IsLoaded)
                {
                    return (false, $"Solution loading failed: {loadResult}");
                }
            }
            else
            {
                logger?.LogDebug("Solution loaded and service ready: {SolutionPath}", mcpServiceManager.CurrentSolutionPath);
            }

            return (true, string.Empty);
        }

        /// <summary>
        /// Check if it's a generated file
        /// </summary>
        private static bool IsGeneratedFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            var fileName = Path.GetFileName(filePath);
            return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
                   fileName.Contains("AssemblyInfo.cs") ||
                   fileName.Contains("TemporaryGeneratedFile") ||
                   fileName.EndsWith(".AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check if it's a system type (constructors, property accessors, etc.)
        /// </summary>
        private static bool IsSystemType(string symbolName, string symbolKind)
        {
            if (string.IsNullOrEmpty(symbolName)) return false;

            // Filter constructors
            if (symbolName.Equals(".ctor", StringComparison.OrdinalIgnoreCase) ||
                symbolName.Equals(".cctor", StringComparison.OrdinalIgnoreCase))
                return true;

            // Filter property accessors
            if (symbolName.StartsWith("get_", StringComparison.OrdinalIgnoreCase) ||
                symbolName.StartsWith("set_", StringComparison.OrdinalIgnoreCase))
                return true;

            // Filter event accessors
            if (symbolName.StartsWith("add_", StringComparison.OrdinalIgnoreCase) ||
                symbolName.StartsWith("remove_", StringComparison.OrdinalIgnoreCase))
                return true;

            // Filter serialization related
            if (symbolName.Contains("Serializer", StringComparison.OrdinalIgnoreCase) ||
                symbolName.Contains("Serialization", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// Get normalized file path (relative to solution root directory)
        /// </summary>
        private static string GetNormalizedPath(string filePath, string? solutionPath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;

            if (!string.IsNullOrEmpty(solutionPath))
            {
                var solutionDirectory = Path.GetDirectoryName(solutionPath);
                if (!string.IsNullOrEmpty(solutionDirectory))
                {
                    try
                    {
                        var relativePath = Path.GetRelativePath(solutionDirectory, filePath);
                        relativePath = relativePath.Replace('\\', '/');

                        if (relativePath.StartsWith("./", StringComparison.Ordinal))
                        {
                            relativePath = relativePath[2..];
                        }

                        if (!string.IsNullOrEmpty(relativePath))
                        {
                            return relativePath;
                        }
                    }
                    catch
                    {
                        // Fall back to default handling below
                    }
                }
            }

            if (Path.IsPathRooted(filePath))
            {
                return Path.GetFileName(filePath);
            }

            return filePath.Replace('\\', '/');
        }
    }
}
