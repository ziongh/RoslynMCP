using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMCP.MCP.Services;
using RoslynMCP.MCP.Models;
using RoslynMCP.MCP.Utils;

namespace RoslynMCP.MCP.Tools
{
    /// <summary>
    /// Solution management MCP tool class
    /// </summary>
    [McpServerToolType]
    public static class SolutionManagementTools
    {

        /// <summary>
        /// Get current solution status
        /// </summary>
        [McpServerTool, Description("Get the current solution status and information. If solution is loading, it will wait for completion")]
        public static async Task<string> GetSolutionStatus(
            [Description("Whether to wait for loading to complete if solution is currently loading (default: true)")]
            bool waitForLoading = true,
            [Description("Maximum wait time in milliseconds if waiting for loading (default: 30000)")]
            int maxWaitMs = 30000,
            [Description("Return response as JSON instead of formatted text (default: false)")]
            bool outputAsJson = false,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var mcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                if (mcpServiceManager == null)
                {
                    logger?.LogError("MCPServiceManager service not available");
                    var error = "❌ MCP service manager service unavailable";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new SolutionStatusResponse
                        {
                            Success = false,
                            Error = error
                        });
                    }
                    return error;
                }

                // Use new GetStatusAsync method, supports waiting for loading
                var status = await mcpServiceManager.GetStatusAsync(waitForLoading, maxWaitMs);
                
                if (outputAsJson)
                {
                    // Parse the text status to extract structured data
                    var isLoaded = mcpServiceManager.IsLoaded;
                    var solutionPath = mcpServiceManager.CurrentSolutionPath;
                    var projectNames = new List<string>();
                    
                    if (isLoaded)
                    {
                        var queryService = serviceProvider?.GetService<Query.Services.IQueryService>();
                        if (queryService != null)
                        {
                            var projects = await queryService.GetProjectsAsync();
                            projectNames = projects.Select(p => p.Name).ToList();
                        }
                    }
                    
                    return JsonResponseFormatter.ToJson(new SolutionStatusResponse
                    {
                        Success = true,
                        IsLoaded = isLoaded,
                        SolutionPath = solutionPath,
                        SolutionFileName = solutionPath != null ? Path.GetFileName(solutionPath) : null,
                        ProjectCount = projectNames.Count,
                        ProjectNames = projectNames,
                        Status = status
                    });
                }
                
                return status;
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Error getting solution status");
                var error = $"❌ Error occurred while getting solution status: {ex.Message}";
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new SolutionStatusResponse
                    {
                        Success = false,
                        Error = error
                    });
                }
                return error;
            }
        }

        /// <summary>
        /// Reload the current solution from disk
        /// </summary>

        [McpServerTool, Description("Reload the current solution from disk, refreshing all cached symbols. Use this tool after making code changes (adding/removing/modifying methods, properties, classes) to ensure the analysis cache reflects the latest state. This operation may take 30 seconds to 2 minutes for large solutions.")]
        public static async Task<string> ReloadSolution(
            [Description("Return response as JSON instead of formatted text (default: false)")]
            bool outputAsJson = false,
            IServiceProvider? serviceProvider = null)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var mcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                if (mcpServiceManager == null)
                {
                    logger?.LogError("MCPServiceManager service not available");
                    var error = "❌ MCP service manager service unavailable";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new ReloadSolutionResponse
                        {
                            Success = false,
                            Error = error
                        });
                    }
                    return error;
                }

                if (!mcpServiceManager.IsLoaded)
                {
                    var error = "❌ No solution is currently loaded. Cannot reload.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new ReloadSolutionResponse
                        {
                            Success = false,
                            Error = error
                        });
                    }
                    return error;
                }

                logger?.LogInformation("Reloading solution requested via MCP tool");
                var result = await mcpServiceManager.ReloadSolutionAsync();
                var duration = DateTime.UtcNow - startTime;
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new ReloadSolutionResponse
                    {
                        Success = true,
                        Message = result,
                        Duration = duration
                    });
                }
                
                return result;
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Error reloading solution");
                var error = $"❌ Error occurred while reloading solution: {ex.Message}";
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new ReloadSolutionResponse
                    {
                        Success = false,
                        Error = error,
                        Duration = DateTime.UtcNow - startTime
                    });
                }
                return error;
            }
        }

        /// <summary>
        /// Notify the server that code files have been modified
        /// </summary>

        [McpServerTool, Description("Notify the server that code files have been modified externally, triggering an incremental cache update. This is much faster than ReloadSolution (2-5 seconds vs 30s-2min) and accurately updates symbols, references, and line numbers for the specified files. Use after making targeted code changes.")]
        public static async Task<string> NotifyCodeChanges(
            [Description("Array of file paths that have been modified. Can be absolute paths or relative to solution root.")]
            string[] changedFiles,
            [Description("Return response as JSON instead of formatted text (default: false)")]
            bool outputAsJson = false,
            IServiceProvider? serviceProvider = null)
        {
            var startTime = DateTime.UtcNow;
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var mcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                if (mcpServiceManager == null)
                {
                    logger?.LogError("MCPServiceManager service not available");
                    var error = "❌ MCP service manager service unavailable";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new NotifyCodeChangesResponse
                        {
                            Success = false,
                            Error = error
                        });
                    }
                    return error;
                }

                if (!mcpServiceManager.IsLoaded)
                {
                    var error = "❌ No solution is currently loaded. Cannot update symbols.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new NotifyCodeChangesResponse
                        {
                            Success = false,
                            Error = error
                        });
                    }
                    return error;
                }

                if (changedFiles == null || changedFiles.Length == 0)
                {
                    var error = "⚠️ No files specified. Please provide at least one file path.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new NotifyCodeChangesResponse
                        {
                            Success = false,
                            Error = error
                        });
                    }
                    return error;
                }

                // Convert relative paths to absolute
                var solutionPath = mcpServiceManager.CurrentSolutionPath;
                if (string.IsNullOrEmpty(solutionPath))
                {
                    var error = "❌ Solution path not available";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new NotifyCodeChangesResponse
                        {
                            Success = false,
                            Error = error
                        });
                    }
                    return error;
                }

                var solutionDir = Path.GetDirectoryName(solutionPath);
                if (string.IsNullOrEmpty(solutionDir))
                {
                    var error = "❌ Invalid solution directory";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new NotifyCodeChangesResponse
                        {
                            Success = false,
                            Error = error
                        });
                    }
                    return error;
                }

                var absolutePaths = changedFiles
                    .Select(f => 
                    {
                        if (Path.IsPathRooted(f))
                            return f;
                        
                        // Convert relative path to absolute
                        var normalizedPath = f.Replace('/', Path.DirectorySeparatorChar);
                        return Path.GetFullPath(Path.Combine(solutionDir, normalizedPath));
                    })
                    .ToArray();

                logger?.LogInformation("Notifying code changes for {Count} files", absolutePaths.Length);
                
                var result = await mcpServiceManager.NotifyCodeChangesAsync(absolutePaths);
                var duration = DateTime.UtcNow - startTime;
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new NotifyCodeChangesResponse
                    {
                        Success = true,
                        Message = result,
                        FilesUpdated = absolutePaths.Length,
                        UpdatedFiles = absolutePaths.Select(p => Path.GetRelativePath(solutionDir, p).Replace('\\', '/')).ToList(),
                        Duration = duration
                    });
                }
                
                return result;
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Error notifying code changes");
                var error = $"❌ Error occurred while updating symbols: {ex.Message}";
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new NotifyCodeChangesResponse
                    {
                        Success = false,
                        Error = error,
                        Duration = DateTime.UtcNow - startTime
                    });
                }
                return error;
            }
        }
    }
}
