using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMCP.MCP.Services;

namespace RoslynMCP.MCP.Tools
{
    /// <summary>
    /// 解决方案管理MCP工具类
    /// </summary>
    [McpServerToolType]
    public static class SolutionManagementTools
    {

        /// <summary>
        /// 获取当前解决方案状态
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
                    return "❌ MCP服务管理器服务不可用";
                }

                // 使用新的GetStatusAsync方法，支持等待加载
                return await mcpServiceManager.GetStatusAsync(waitForLoading, maxWaitMs);
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Error getting solution status");
                return $"❌ 获取解决方案状态时发生错误: {ex.Message}";
            }
        }
    }
}
