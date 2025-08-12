using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMCP.Query.Services;
using RoslynMCP.MCP.Services;

namespace RoslynMCP.MCP.Tools
{
    public static partial class RoslynAnalysisTools
    {
        /// <summary>
        /// 获取符号的完整源代码
        /// </summary>
        [McpServerTool, Description("Get the full source code of any specific symbol, including classes, methods, properties, fields, etc.")]
        public static async Task<string> GetSourceCode(
            [Description("Exact symbol name or fully qualified name of any symbol (e.g., 'MyClass', 'MyClass.MyMethod')")]
            string symbolName,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var solutionmcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                if (solutionmcpServiceManager == null || !solutionmcpServiceManager.IsLoaded)
                {
                    return "❌ No solution is loaded. Please use the `SwitchSolution` tool first.";
                }

                var queryService = serviceProvider?.GetService<IQueryService>();
                if (queryService == null)
                {
                    return "❌ Query service is not available.";
                }

                logger?.LogInformation("Getting source code for symbol: {SymbolName}", symbolName);
                var sourceCode = await queryService.GetSourceCodeAsync(symbolName);

                if (sourceCode == null)
                {
                    return $"## Source Code Not Found\n\nSymbol `{symbolName}` was not found or has no source code.";
                }

                return $"## Source Code for `{symbolName}`\n\n```csharp\n{sourceCode}\n```";
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Failed to get source code for symbol: {SymbolName}", symbolName);
                return $"Error: An unexpected error occurred: {ex.Message}";
            }
        }

        /// <summary>
        /// 获取文件的完整内容
        /// </summary>
        [McpServerTool, Description("Get the full content of a source file within the solution")]
        public static async Task<string> GetFileContent(
            [Description("Path to the file relative to the solution root directory (e.g., 'src/MyProject/MyFile.cs'). Absolute paths are not supported for security reasons.")]
            string filePath,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var solutionmcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                if (solutionmcpServiceManager == null || !solutionmcpServiceManager.IsLoaded)
                {
                    return "❌ No solution is loaded. Please use the `SwitchSolution` tool first.";
                }

                var queryService = serviceProvider?.GetService<IQueryService>();
                if (queryService == null)
                {
                    return "❌ Query service is not available.";
                }

                logger?.LogInformation("Getting content for file: {FilePath}", filePath);
                var fileContent = await queryService.GetFileContentAsync(filePath);

                if (fileContent == null)
                {
                    var results = new StringBuilder();
                    results.AppendLine($"## 📁 文件内容未找到");
                    results.AppendLine();
                    results.AppendLine($"**文件路径**: `{filePath}`");
                    results.AppendLine();
                    results.AppendLine("**可能的原因**：");
                    results.AppendLine("- 文件路径不正确或文件不存在");
                    results.AppendLine("- 文件不在当前解决方案范围内");
                    results.AppendLine("- 文件权限不足或被其他程序占用");
                    results.AppendLine();
                    results.AppendLine("**建议尝试**：");
                    results.AppendLine("- 检查路径是否正确（必须是相对于解决方案根目录的路径）");
                    results.AppendLine("- 使用 `ListProjects` 查看解决方案中的项目和文件");
                    results.AppendLine("- 确认文件确实存在于解决方案中");
                    results.AppendLine("- 确保使用相对路径而不是绝对路径");
                    results.AppendLine();
                    results.AppendLine("**路径格式示例**：");
                    results.AppendLine("- 正确：`src/MyProject/MyFile.cs`");
                    results.AppendLine("- 错误：`C:\\Solution\\src\\MyProject\\MyFile.cs` (不支持绝对路径)");
                    return results.ToString();
                }
                
                var fileExtension = Path.GetExtension(filePath).TrimStart('.');
                return $"## Content of `{Path.GetFileName(filePath)}`\n\n```{fileExtension}\n{fileContent}\n```";
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Failed to get file content: {FilePath}", filePath);
                return $"Error: An unexpected error occurred: {ex.Message}";
            }
        }
    }
}
