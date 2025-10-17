using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMCP.MCP.Services;

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
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var mcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                if (mcpServiceManager == null)
                {
                    logger?.LogError("MCPServiceManager service not available");
                    return "❌ MCP service manager service unavailable";
                }

                // Use new GetStatusAsync method, supports waiting for loading
                return await mcpServiceManager.GetStatusAsync(waitForLoading, maxWaitMs);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Error getting solution status");
                return $"❌ Error occurred while getting solution status: {ex.Message}";
            }
        }

        [McpServerTool, Description("Reload the current solution from disk, refreshing all cached symbols. Use this tool after making code changes (adding/removing/modifying methods, properties, classes) to ensure the analysis cache reflects the latest state. This operation may take 30 seconds to 2 minutes for large solutions.")]
        public static async Task<string> ReloadSolution(
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var mcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                if (mcpServiceManager == null)
                {
                    logger?.LogError("MCPServiceManager service not available");
                    return "❌ MCP service manager service unavailable";
                }

                if (!mcpServiceManager.IsLoaded)
                {
                    return "❌ No solution is currently loaded. Cannot reload.";
                }

                logger?.LogInformation("Reloading solution requested via MCP tool");
                return await mcpServiceManager.ReloadSolutionAsync();
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Error reloading solution");
                return $"❌ Error occurred while reloading solution: {ex.Message}";
            }
        }
    }
}
