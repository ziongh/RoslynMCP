using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMCP.Query.Services;
using RoslynMCP.Analysis.Services;
using RoslynMCP.MCP.Services;
using RoslynMCP.MCP.Utils;

namespace RoslynMCP.MCP.Tools
{
    public static partial class RoslynAnalysisTools
    {
        /// <summary>
        /// 查找符号引用
        /// </summary>
        [McpServerTool, Description("Find all references to any specific symbol, including classes, methods, properties, fields, etc.")]
        public static async Task<string> FindReferences(
            [Description("Exact symbol name or fully qualified name of any symbol to find references for (e.g., 'MyClass', 'MyClass.MyMethod')")]
            string symbolName,
            [Description("Include symbol definition in results")]
            bool includeDefinition = true,
            [Description("Maximum number of references to return, or 0 to use default (20)")]
            int maxResults = 0,
            [Description("Exclude auto-generated files (*.g.cs, *.Designer.cs, etc.)")]
            bool excludeGeneratedFiles = true,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var solutionmcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                // 检查解决方案是否已加载
                if (solutionmcpServiceManager == null || !solutionmcpServiceManager.IsLoaded)
                {
                    return "❌ 没有加载解决方案。请先使用 `SwitchSolution` 工具加载解决方案文件。";
                }

                var solutionPath = solutionmcpServiceManager.CurrentSolutionPath!;

                // 设置默认值
                maxResults = ParameterUtils.GetMaxDisplayResultsDefault(maxResults, serviceProvider);

                if (string.IsNullOrWhiteSpace(symbolName))
                {
                    return "❌ 符号名称不能为空。";
                }

                // 获取查询服务
                var queryService = serviceProvider?.GetService<IQueryService>();
                if (queryService == null)
                {
                    return "❌ 查询服务不可用。";
                }

                logger?.LogInformation("查找引用: {SymbolName} 在解决方案: {SolutionPath}", symbolName, solutionPath);

                // 确保解决方案已加载且服务已初始化
                var (success, error) = await EnsureSolutionLoadedAsync(serviceProvider, logger);
                if (!success)
                {
                    return $"Error: {error}";
                }

                // 查找引用
                var references = await queryService.FindReferencesAsync(symbolName);

                // 应用过滤器
                var filteredReferences = references.AsEnumerable();
                
                if (excludeGeneratedFiles)
                {
                    filteredReferences = filteredReferences.Where(r => !IsGeneratedFile(r.DocumentPath));
                }
                
                if (!includeDefinition)
                {
                    filteredReferences = filteredReferences.Where(r => !r.IsDefinition);
                }

                // 格式化结果
                var results = new StringBuilder();
                results.AppendLine("# Reference Search Results");
                results.AppendLine();
                results.AppendLine($"**Symbol**: `{symbolName}`");
                results.AppendLine($"**Solution**: {Path.GetFileName(solutionPath)}");
                results.AppendLine($"**Include Definition**: {includeDefinition}");
                results.AppendLine($"**Max Results**: {maxResults}");
                if (excludeGeneratedFiles) results.AppendLine("**Excludes**: Generated files");
                results.AppendLine();

                var allReferences = filteredReferences.ToList();
                if (!allReferences.Any())
                {
                    results.AppendLine("No references found for this symbol.");
                    return results.ToString();
                }

                var totalReferences = allReferences.Count;
                var referenceList = allReferences.Take(maxResults).ToList();
                
                results.AppendLine($"Found **{totalReferences}** references");
                var truncationHint = ParameterUtils.GenerateTruncationHint(totalReferences, maxResults, "maxResults", "use a more specific search");
                if (!string.IsNullOrEmpty(truncationHint))
                {
                    results.AppendLine(truncationHint);
                }
                results.AppendLine();

                // 按文件分组显示
                var groupedByFile = referenceList.GroupBy(r => r.DocumentPath).OrderBy(g => Path.GetFileName(g.Key));
                
                foreach (var fileGroup in groupedByFile)
                {
                    var fileName = Path.GetFileName(fileGroup.Key);
                    var normalizedPath = GetNormalizedPath(fileGroup.Key);
                    
                    results.AppendLine($"## {fileName}");
                    results.AppendLine($"**Path**: `{normalizedPath}`");
                    results.AppendLine();

                    foreach (var reference in fileGroup.OrderBy(r => r.LineNumber))
                    {
                        var kindIndicator = reference.IsDefinition ? "🔷 **定义**" : "🔸 **引用**";
                        results.AppendLine($"{kindIndicator} Line {reference.LineNumber}:{reference.ColumnNumber} ({reference.ReferenceKind})");

                        if (!string.IsNullOrEmpty(reference.Context))
                        {
                            results.AppendLine("```csharp");
                            results.AppendLine(reference.Context.Trim());
                            results.AppendLine("```");
                        }
                        results.AppendLine();
                    }
                }

                logger?.LogInformation("引用查找完成，找到 {Count} 个引用", referenceList.Count);
                return results.ToString();
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "引用查找失败: {SymbolName}", symbolName);
                return $"Error: An unexpected error occurred while finding references: {ex.Message}";
            }
        }
        
        /// <summary>
        /// 获取符号的继承层次结构
        /// </summary>
        [McpServerTool, Description("Get the inheritance hierarchy for a class or interface")]
        public static async Task<string> GetInheritanceHierarchy(
            [Description("The name of the symbol to analyze")]
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

                var analysisService = serviceProvider?.GetService<IAnalysisService>();
                if (analysisService == null)
                {
                    return "❌ Analysis service is not available.";
                }

                logger?.LogInformation("Getting inheritance hierarchy for: {SymbolName}", symbolName);
                var hierarchy = await analysisService.GetInheritanceHierarchyAsync(symbolName);

                if (hierarchy == null)
                {
                    return $"## Inheritance Hierarchy Not Found\n\nSymbol `{symbolName}` could not be analyzed.";
                }

                var results = new StringBuilder();
                results.AppendLine($"# Inheritance Hierarchy for: {symbolName}");
                results.AppendLine();

                results.AppendLine("## Inheritance Chain (Up)");
                if (hierarchy.BaseNode != null)
                {
                    var current = hierarchy.BaseNode;
                    var indent = "";
                    while (current != null)
                    {
                        var line = $"{indent}- {current.SymbolName}";
                        if (current.SymbolName == symbolName)
                        {
                            line += " (self)";
                        }
                        results.AppendLine(line);
                        current = current.Children.FirstOrDefault();
                        indent += "  ";
                    }
                }
                results.AppendLine();

                results.AppendLine("## Derived Types (Down the chain)");
                if (hierarchy.DerivedNodes.Any())
                {
                    foreach (var derived in hierarchy.DerivedNodes)
                    {
                        results.AppendLine($"- {derived.SymbolName}");
                    }
                }
                else
                {
                    results.AppendLine("None");
                }

                return results.ToString();
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Failed to get inheritance hierarchy for {SymbolName}", symbolName);
                return $"Error: An unexpected error occurred: {ex.Message}";
            }
        }

        /// <summary>
        /// 获取方法体内的调用
        /// </summary>
        [McpServerTool, Description("Get all method calls within a specific method body. Analyzes method invocations and provides detailed call information.")]
        public static async Task<string> GetMethodBodyInvocations(
            [Description("The name of the method to analyze (e.g., 'MyMethod' or 'MyClass.MyMethod'). Can be a partial or fully qualified name.")]
            string methodName,
            [Description("Optional. The name of the project to limit the search to. Use 'ListProjects' to find project names.")]
            string? projectName = null,
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

                var analysisService = serviceProvider?.GetService<IAnalysisService>();
                if (analysisService == null)
                {
                    return "❌ Analysis service is not available.";
                }

                logger?.LogInformation("Getting method body invocations for: {MethodName} in project scope: {ProjectName}", methodName, projectName ?? "All");
                var invocations = await analysisService.GetMethodBodyInvocationsAsync(methodName, projectName);
                
                var results = new StringBuilder();
                results.AppendLine($"# Method Invocations in: {methodName}");
                if (projectName != null)
                {
                    results.AppendLine($" (Project: {projectName})");
                }
                results.AppendLine();

                var invocationList = invocations.ToList();
                if (!invocationList.Any())
                {
                    results.AppendLine("## 📝 分析结果");
                    results.AppendLine();
                    results.AppendLine("**无法找到方法调用**。这可能是由于以下原因：");
                    results.AppendLine("- 方法体为空或只包含简单语句。");
                    results.AppendLine("- 方法名称不正确或在指定的项目范围中不存在。");
                    results.AppendLine("- 方法过于复杂或包含当前工具无法分析的特殊语法结构。");
                    results.AppendLine();
                    results.AppendLine("**建议尝试**：");
                    results.AppendLine("- 检查方法名称和项目名称是否正确。");
                    results.AppendLine("- 使用 `GetSourceCode` 直接查看方法的源码。");
                    results.AppendLine("- 使用 `SearchSymbols` 在更广的范围内查找相似的方法名。");
                    return results.ToString();
                }

                // 增加对找到多个重载的提示
                var methodSymbol = (await (serviceProvider?.GetService<IQueryService>()!)
                    .FindMethodSymbolsAsync(methodName, projectName))
                    .FirstOrDefault();

                if (methodSymbol != null)
                {
                    results.AppendLine($"*分析的目标方法: `{methodSymbol.ToDisplayString()}`*");
                    results.AppendLine();
                }

                foreach (var invocation in invocationList)
                {
                    results.AppendLine($"- **Call**: `{invocation.CalledMethodName}`");
                    results.AppendLine($"  - **Containing Type**: `{invocation.ContainingType}`");
                    results.AppendLine($"  - **Location**: {Path.GetFileName(invocation.FilePath)}({invocation.LineNumber})");
                    results.AppendLine();
                }

                return results.ToString();
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Failed to get method body invocations for {MethodName}", methodName);
                return $"Error: An unexpected error occurred: {ex.Message}";
            }
        }


    }
}
