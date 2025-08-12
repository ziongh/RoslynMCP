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
        /// 确保解决方案已加载并且服务已初始化
        /// </summary>
        private static async Task<(bool success, string error)> EnsureSolutionLoadedAsync(
            IServiceProvider? serviceProvider, ILogger? logger = null)
        {
            // 获取MCP服务管理器
            var mcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();
            if (mcpServiceManager == null)
            {
                return (false, "MCP服务管理器不可用");
            }

            // 检查是否需要加载解决方案
            if (!mcpServiceManager.IsLoaded)
            {
                var solutionPath = mcpServiceManager.CurrentSolutionPath;
                if (string.IsNullOrEmpty(solutionPath))
                {
                    return (false, "没有配置解决方案路径");
                }
                logger?.LogDebug("解决方案未加载，正在初始化服务: {SolutionPath}", solutionPath);
                var loadResult = await mcpServiceManager.LoadSolutionAsync(solutionPath);
                
                // 检查加载结果
                if (!mcpServiceManager.IsLoaded)
                {
                    return (false, $"解决方案加载失败: {loadResult}");
                }
            }
            else
            {
                logger?.LogDebug("解决方案已加载且服务已就绪: {SolutionPath}", mcpServiceManager.CurrentSolutionPath);
            }

            return (true, string.Empty);
        }

        /// <summary>
        /// 检查是否为生成的文件
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
        /// 检查是否为系统类型（构造函数、属性访问器等）
        /// </summary>
        private static bool IsSystemType(string symbolName, string symbolKind)
        {
            if (string.IsNullOrEmpty(symbolName)) return false;

            // 过滤构造函数
            if (symbolName.Equals(".ctor", StringComparison.OrdinalIgnoreCase) ||
                symbolName.Equals(".cctor", StringComparison.OrdinalIgnoreCase))
                return true;

            // 过滤属性访问器
            if (symbolName.StartsWith("get_", StringComparison.OrdinalIgnoreCase) ||
                symbolName.StartsWith("set_", StringComparison.OrdinalIgnoreCase))
                return true;

            // 过滤事件访问器
            if (symbolName.StartsWith("add_", StringComparison.OrdinalIgnoreCase) ||
                symbolName.StartsWith("remove_", StringComparison.OrdinalIgnoreCase))
                return true;

            // 过滤序列化相关
            if (symbolName.Contains("Serializer", StringComparison.OrdinalIgnoreCase) ||
                symbolName.Contains("Serialization", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// 获取标准化的文件路径（相对于解决方案根目录）
        /// </summary>
        private static string GetNormalizedPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;

            // 如果是绝对路径，尝试转换为相对路径
            if (Path.IsPathRooted(filePath))
            {
                // 这里可以进一步优化，根据解决方案路径计算相对路径
                return Path.GetFileName(filePath);
            }

            return filePath;
        }
    }
}
