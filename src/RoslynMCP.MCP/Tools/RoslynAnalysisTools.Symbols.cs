using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using RoslynMCP.Query.Services;
using RoslynMCP.Analysis.Services;
using RoslynMCP.SymbolCache.Security;
using RoslynMCP.MCP.Services;
using RoslynMCP.MCP.Utils;
using static RoslynMCP.Analysis.Services.IAnalysisService;

namespace RoslynMCP.MCP.Tools
{
    public static partial class RoslynAnalysisTools
    {
        /// <summary>
        /// 搜索符号（支持通配符）
        /// </summary>
        [McpServerTool, Description("Search for symbols in C# code using wildcard patterns (* and ?)")]
        public static async Task<string> SearchSymbols(
            [Description("Wildcard pattern to search for (e.g., 'User*', '*Service', 'Get*User')")]
            string pattern,
            [Description("Comma-separated list of symbol types to include. Valid options: 'class', 'interface', 'method', 'property', 'field', 'enum'. Default is 'class'. Example: 'class,method' to search only classes and methods")]
            string symbolTypes = "class",
            [Description("Maximum number of results to return, or 0 to use default (20)")]
            int maxResults = 0,
            [Description("Exclude auto-generated files (*.g.cs, *.Designer.cs, etc.)")]
            bool excludeGeneratedFiles = true,
            [Description("Exclude system types (constructors, property accessors, etc.)")]
            bool excludeSystemTypes = true,
            [Description("Specifies if the search is case-sensitive. Defaults to 'true' for precision. Consider setting to 'false' when unsure of the exact casing.")]
            bool caseSensitive = true,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var options = serviceProvider?.GetService<IOptions<AnalyzerOptions>>()?.Value;
                var solutionmcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                // 检查解决方案是否已加载
                if (solutionmcpServiceManager == null || !solutionmcpServiceManager.IsLoaded)
                {
                    return "❌ 没有加载解决方案。解决方案未加载。请使用 --solution 参数或设置 ROSLYN_MCP_SOLUTION_PATH 环境变量指定解决方案路径。";
                }

                var solutionPath = solutionmcpServiceManager.CurrentSolutionPath!;

                // 设置默认值
                maxResults = ParameterUtils.GetMaxDisplayResultsDefault(maxResults, serviceProvider);

                // 验证输入
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    return "❌ 搜索模式不能为空。";
                }

                // 获取查询服务
                var queryService = serviceProvider?.GetService<IQueryService>();
                if (queryService == null)
                {
                    return "Error: Query service not available.";
                }

                logger?.LogInformation("搜索符号: {Pattern} 在解决方案: {SolutionPath}", pattern, solutionPath);

                // 确保解决方案已加载且服务已初始化
                var (success, error) = await EnsureSolutionLoadedAsync(serviceProvider, logger);
                if (!success)
                {
                    return $"Error: {error}";
                }

                // 构建搜索请求
                var symbolTypesList = symbolTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var searchRequest = new SymbolSearchRequest
                {
                    Pattern = pattern,
                    SymbolKinds = symbolTypesList,
                    MaxResults = maxResults,
                    CaseSensitive = caseSensitive
                };

                // 执行搜索
                var searchResults = await queryService.SearchSymbolsAsync(searchRequest);

                // 应用过滤器
                var filteredResults = searchResults.AsEnumerable();
                
                if (excludeGeneratedFiles)
                {
                    filteredResults = filteredResults.Where(r => !IsGeneratedFile(r.FilePath));
                }
                
                if (excludeSystemTypes)
                {
                    filteredResults = filteredResults.Where(r => !IsSystemType(r.Name, r.SymbolKind));
                }

                // 格式化结果
                var results = new StringBuilder();
                results.AppendLine("# Symbol Search Results");
                results.AppendLine();
                results.AppendLine($"**Pattern**: `{pattern}`");
                results.AppendLine($"**Solution**: {Path.GetFileName(solutionPath)}");
                results.AppendLine($"**Symbol Types**: {symbolTypes}");
                results.AppendLine($"**Case Sensitive**: {caseSensitive}");
                if (excludeGeneratedFiles) results.AppendLine("**Excludes**: Generated files");
                if (excludeSystemTypes) results.AppendLine("**Excludes**: System types");
                results.AppendLine();

                var resultList = filteredResults.ToList();
                if (!resultList.Any())
                {
                    results.AppendLine("No symbols found matching the pattern.");
                    return results.ToString();
                }

                var totalCount = resultList.Count;
                var displayResults = resultList.Take(maxResults).ToList();
                
                results.AppendLine($"Found **{totalCount}** symbols");
                var truncationHint = ParameterUtils.GenerateSearchTruncationHint(totalCount, maxResults, "maxResults");
                if (!string.IsNullOrEmpty(truncationHint))
                {
                    results.AppendLine(truncationHint);
                }
                results.AppendLine();

                foreach (var result in displayResults)
                {
                    results.AppendLine($"## {result.Name}");
                    results.AppendLine($"- **Type**: {result.SymbolKind}");
                    results.AppendLine($"- **Namespace**: {result.Namespace}");
                    results.AppendLine($"- **File**: {Path.GetFileName(result.FilePath)}");
                    results.AppendLine($"- **Location**: Line {result.LineNumber}");
                    results.AppendLine($"- **Accessibility**: {result.Accessibility}");

                    if (!string.IsNullOrEmpty(result.Summary))
                    {
                        results.AppendLine($"- **Summary**: {result.Summary}");
                    }

                    results.AppendLine();
                }

                results.AppendLine("---");
                results.AppendLine("💡 **Tip**: To get more information about a symbol, including its full file path, use the `get_symbol_details` tool with the symbol's `FullName`.");
                results.AppendLine();

                logger?.LogInformation("搜索完成，找到 {Count} 个符号", resultList.Count);
                return results.ToString();
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "符号搜索失败: {Pattern}", pattern);
                return $"Error: An unexpected error occurred during symbol search: {ex.Message}";
            }
        }

        /// <summary>
        /// 获取符号的详细聚合信息（包含基本信息、源码、引用、继承关系等）
        /// </summary>
        [McpServerTool, Description("Get comprehensive analysis of any symbol (e.g., class, method, property). Provides details like source code, references, and inheritance hierarchy (if applicable). This is the most detailed and recommended tool for symbol inspection.")]
        public static async Task<string> GetSymbolDetails(
            [Description("Exact symbol name or full qualified name of any symbol (e.g., 'MyNamespace.MyClass', 'MyNamespace.MyClass.MyMethod')")]
            string symbolName,
            [Description("Include source code in the response")]
            bool includeSourceCode = true,
            [Description("Include references in the response")]
            bool includeReferences = true,
            [Description("Include inheritance hierarchy in the response")]
            bool includeInheritanceHierarchy = true,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var solutionmcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                // 设置默认值
                var maxReferences = ParameterUtils.GetMaxDisplayResultsDefault(0, serviceProvider);

                // 检查解决方案是否已加载
                if (solutionmcpServiceManager == null || !solutionmcpServiceManager.IsLoaded)
                {
                    return "❌ 解决方案未加载。请使用 --solution 参数或设置 ROSLYN_MCP_SOLUTION_PATH 环境变量指定解决方案路径。";
                }

                var solutionPath = solutionmcpServiceManager.CurrentSolutionPath!;

                if (string.IsNullOrWhiteSpace(symbolName))
                {
                    return "❌ 符号名称不能为空。";
                }

                // 获取服务
                var queryService = serviceProvider?.GetService<IQueryService>();
                var analysisService = serviceProvider?.GetService<IAnalysisService>();
                if (queryService == null)
                {
                    return "❌ 查询服务不可用。";
                }

                logger?.LogInformation("获取符号详细信息: {SymbolName}", symbolName);

                // 确保解决方案已加载且服务已初始化
                var (success, error) = await EnsureSolutionLoadedAsync(serviceProvider, logger);
                if (!success)
                {
                    return $"Error: {error}";
                }

                // 构建聚合结果
                var results = new StringBuilder();
                results.AppendLine($"# Complete Symbol Analysis: `{symbolName}`");
                results.AppendLine();
                results.AppendLine($"**Solution**: {Path.GetFileName(solutionPath)}");
                results.AppendLine($"**Analysis Date**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                results.AppendLine();

                // 1. 基本符号信息
                results.AppendLine("## 📋 基本信息");
                try
                {
                    var symbolDetails = await queryService.GetSymbolDetailsAsync(symbolName);
                    if (symbolDetails != null)
                    {
                        results.AppendLine($"- **名称**: `{symbolDetails.Name}`");
                        results.AppendLine($"- **完整名称**: `{symbolDetails.FullName}`");
                        results.AppendLine($"- **类型**: {symbolDetails.SymbolKind}");
                        results.AppendLine($"- **命名空间**: {symbolDetails.Namespace}");
                        results.AppendLine($"- **程序集**: {symbolDetails.AssemblyName}");
                        results.AppendLine($"- **可访问性**: {symbolDetails.Accessibility}");
                        results.AppendLine($"- **源位置**: `{symbolDetails.SourceLocation}`");

                        if (!string.IsNullOrEmpty(symbolDetails.Documentation))
                        {
                            results.AppendLine();
                            results.AppendLine("**文档说明**:");
                            results.AppendLine(symbolDetails.Documentation);
                        }
                    }
                    else
                    {
                        results.AppendLine($"⚠️ 无法找到符号 `{symbolName}` 的基本信息");
                    }
                }
                catch (Exception ex)
                {
                    results.AppendLine($"❌ 获取基本信息失败: {ex.Message}");
                }
                results.AppendLine();

                // 2. 源代码（如果请求）
                if (includeSourceCode)
                {
                    results.AppendLine("## 📄 源代码");
                    try
                    {
                        var sourceCode = await queryService.GetSourceCodeAsync(symbolName);
                        if (!string.IsNullOrEmpty(sourceCode))
                        {
                            results.AppendLine("```csharp");
                            results.AppendLine(sourceCode);
                            results.AppendLine("```");
                        }
                        else
                        {
                            results.AppendLine("⚠️ 源代码不可用（可能是外部引用或编译时生成）");
                        }
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"❌ 获取源代码失败: {ex.Message}");
                    }
                    results.AppendLine();
                }

                // 3. 继承层次（如果请求且分析服务可用）
                if (includeInheritanceHierarchy && analysisService != null)
                {
                    results.AppendLine("## 🏗️ 继承层次");
                    try
                    {
                        var hierarchy = await analysisService.GetInheritanceHierarchyAsync(symbolName);
                        if (hierarchy != null)
                        {
                            results.AppendLine("**基类链**:");
                            if (hierarchy.BaseNode != null)
                            {
                                var current = hierarchy.BaseNode;
                                var indent = "";
                                while (current != null)
                                {
                                    var line = $"{indent}- {current.SymbolName}";
                                    if (current.SymbolName == symbolName)
                                    {
                                        line += " *(当前类)*";
                                    }
                                    results.AppendLine(line);
                                    current = current.Children.FirstOrDefault();
                                    indent += "  ";
                                }
                            }
                            else
                            {
                                results.AppendLine("- 无基类");
                            }

                            results.AppendLine();
                            results.AppendLine("**派生类**:");
                            if (hierarchy.DerivedNodes.Any())
                            {
                                foreach (var derived in hierarchy.DerivedNodes)
                                {
                                    results.AppendLine($"- {derived.SymbolName}");
                                }
                            }
                            else
                            {
                                results.AppendLine("- 无派生类");
                            }
                        }
                        else
                        {
                            results.AppendLine("⚠️ 继承层次信息不可用");
                        }
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"❌ 获取继承层次失败: {ex.Message}");
                    }
                    results.AppendLine();
                }

                // 4. 引用（如果请求）
                if (includeReferences)
                {
                    results.AppendLine("## 🔗 引用分析");
                    try
                    {
                        var references = await queryService.FindReferencesAsync(symbolName);
                        var allFilteredReferences = references.Where(r => !IsGeneratedFile(r.DocumentPath)).ToList();
                        var filteredReferences = allFilteredReferences.Take(maxReferences).ToList();

                        if (allFilteredReferences.Any())
                        {
                            var totalCount = allFilteredReferences.Count;
                            results.AppendLine($"找到 **{totalCount}** 个引用");
                            var refHint = ParameterUtils.GenerateTruncationHint(totalCount, maxReferences, "maxReferences", "use FindReferences tool");
                            if (!string.IsNullOrEmpty(refHint))
                            {
                                results.AppendLine(refHint);
                            }
                            results.AppendLine();

                            var groupedByFile = filteredReferences.GroupBy(r => r.DocumentPath).OrderBy(g => Path.GetFileName(g.Key));
                            foreach (var fileGroup in groupedByFile)
                            {
                                var fileName = Path.GetFileName(fileGroup.Key);
                                results.AppendLine($"**{fileName}**:");
                                foreach (var reference in fileGroup.OrderBy(r => r.LineNumber))
                                {
                                    var kindIndicator = reference.IsDefinition ? "🔷" : "🔸";
                                    results.AppendLine($"  {kindIndicator} Line {reference.LineNumber} ({reference.ReferenceKind})");
                                }
                                results.AppendLine();
                            }
                        }
                        else
                        {
                            results.AppendLine("⚠️ 未找到引用");
                        }
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"❌ 获取引用失败: {ex.Message}");
                    }
                    results.AppendLine();
                }

                results.AppendLine("---");
                results.AppendLine("## 💡 Additional Tools");
                results.AppendLine();
                results.AppendLine("**For focused analysis**, use:");
                results.AppendLine("- `GetFileContent` - Complete file content with context");
                results.AppendLine("- `FindReferences` - Complete reference analysis");

                logger?.LogInformation("符号详细信息获取完成: {SymbolName}", symbolName);
                return results.ToString();
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "获取符号详细信息失败: {SymbolName}", symbolName);
                return $"Error: An unexpected error occurred while getting symbol details: {ex.Message}";
            }
        }


    }
}
