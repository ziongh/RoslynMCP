using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMCP.Query.Services;
using RoslynMCP.Analysis.Services;
using RoslynMCP.MCP.Services;
using RoslynMCP.MCP.Utils;
using RoslynMCP.MCP.Models;

namespace RoslynMCP.MCP.Tools
{
    public static partial class RoslynAnalysisTools
    {
        /// <summary>
        /// Find symbol references
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
            [Description("Return response as JSON instead of formatted text (default: false)")]
            bool outputAsJson = false,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var solutionmcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                // Check if solution is loaded
                if (solutionmcpServiceManager == null || !solutionmcpServiceManager.IsLoaded)
                {
                    return "❌ No solution is loaded. Please use the `SwitchSolution` tool first to load a solution file.";
                }

                var solutionPath = solutionmcpServiceManager.CurrentSolutionPath!;

                // Apply default configuration values
                maxResults = ParameterUtils.GetMaxDisplayResultsDefault(maxResults, serviceProvider);

                if (string.IsNullOrWhiteSpace(symbolName))
                {
                    return "❌ Symbol name cannot be empty.";
                }

                // Get query service
                var queryService = serviceProvider?.GetService<IQueryService>();
                if (queryService == null)
                {
                    return "❌ Query service is not available.";
                }

                logger?.LogInformation("Finding references: {SymbolName} in solution: {SolutionPath}", symbolName, solutionPath);

                // Ensure solution is loaded and services are initialized
                var (success, error) = await EnsureSolutionLoadedAsync(serviceProvider, logger);
                if (!success)
                {
                    return $"Error: {error}";
                }

                // Find references
                var references = await queryService.FindReferencesAsync(symbolName);

                // Apply filters
                var filteredReferences = references.AsEnumerable();
                
                if (excludeGeneratedFiles)
                {
                    filteredReferences = filteredReferences.Where(r => !IsGeneratedFile(r.DocumentPath));
                }
                
                if (!includeDefinition)
                {
                    filteredReferences = filteredReferences.Where(r => !r.IsDefinition);
                }

                // Format results
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

                // Display grouped by file
                var groupedByFile = referenceList.GroupBy(r => r.DocumentPath).OrderBy(g => Path.GetFileName(g.Key));
                
                foreach (var fileGroup in groupedByFile)
                {
                    var fileName = Path.GetFileName(fileGroup.Key);
                    var normalizedPath = GetNormalizedPath(fileGroup.Key, solutionPath);
                    
                    results.AppendLine($"## {fileName}");
                    results.AppendLine($"**Path**: `{normalizedPath}`");
                    results.AppendLine();

                    foreach (var reference in fileGroup.OrderBy(r => r.LineNumber))
                    {
                        var kindIndicator = reference.IsDefinition ? "🔷 **Definition**" : "🔸 **Reference**";
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

                logger?.LogInformation("Reference search completed, found {Count} references", referenceList.Count);
                
                if (outputAsJson)
                {
                    var groupedDict = referenceList
                        .GroupBy(r => r.DocumentPath)
                        .ToDictionary(
                            g => GetNormalizedPath(g.Key, solutionPath),
                            g => g.OrderBy(r => r.LineNumber).ToList()
                        );
                    
                    return JsonResponseFormatter.ToJson(new FindReferencesResponse
                    {
                        Success = true,
                        SymbolName = symbolName,
                        SolutionFileName = Path.GetFileName(solutionPath),
                        IncludeDefinition = includeDefinition,
                        ExcludeGeneratedFiles = excludeGeneratedFiles,
                        TotalCount = totalReferences,
                        DisplayedCount = referenceList.Count,
                        IsTruncated = totalReferences > maxResults,
                        References = referenceList,
                        GroupedByFile = groupedDict
                    });
                }
                
                return results.ToString();
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Reference search failed: {SymbolName}", symbolName);
                var errorMsg = $"Error: An unexpected error occurred while finding references: {ex.Message}";
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new FindReferencesResponse
                    {
                        Success = false,
                        Error = errorMsg
                    });
                }
                return errorMsg;
            }
        }
        
        /// <summary>
        /// Get the inheritance hierarchy for a symbol
        /// </summary>
        [McpServerTool, Description("Get the inheritance hierarchy for a class or interface")]
        public static async Task<string> GetInheritanceHierarchy(
            [Description("The name of the symbol to analyze")]
            string symbolName,
            [Description("Return response as JSON instead of formatted text (default: false)")]
            bool outputAsJson = false,
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
                    var errorMsg = $"## Inheritance Hierarchy Not Found\n\nSymbol `{symbolName}` could not be analyzed.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new GetInheritanceHierarchyResponse
                        {
                            Success = false,
                            Error = errorMsg,
                            SymbolName = symbolName
                        });
                    }
                    return errorMsg;
                }

                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new GetInheritanceHierarchyResponse
                    {
                        Success = true,
                        SymbolName = symbolName,
                        Hierarchy = hierarchy
                    });
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
                var errorMsg = $"Error: An unexpected error occurred: {ex.Message}";
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new GetInheritanceHierarchyResponse
                    {
                        Success = false,
                        Error = errorMsg,
                        SymbolName = symbolName
                    });
                }
                return errorMsg;
            }
        }

        /// <summary>
        /// Get method calls within a method body
        /// </summary>
        [McpServerTool, Description("Get all method calls within a specific method body. Analyzes method invocations and provides detailed call information.")]
        public static async Task<string> GetMethodBodyInvocations(
            [Description("The name of the method to analyze (e.g., 'MyMethod' or 'MyClass.MyMethod'). Can be a partial or fully qualified name.")]
            string methodName,
            [Description("Optional. The name of the project to limit the search to. Use 'ListProjects' to find project names.")]
            string? projectName = null,
            [Description("Return response as JSON instead of formatted text (default: false)")]
            bool outputAsJson = false,
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
                    results.AppendLine("## 📝 Analysis Results");
                    results.AppendLine();
                    results.AppendLine("**Unable to find method calls**. This may be due to the following reasons:");
                    results.AppendLine("- The method body is empty or contains only simple statements.");
                    results.AppendLine("- The method name is incorrect or does not exist in the specified project scope.");
                    results.AppendLine("- The method is too complex or contains special syntax structures that the current tool cannot analyze.");
                    results.AppendLine();
                    results.AppendLine("**Suggestions to try**:");
                    results.AppendLine("- Check if the method name and project name are correct.");
                    results.AppendLine("- Use `GetSourceCode` to directly view the method's source code.");
                    results.AppendLine("- Use `SearchSymbols` to find similar method names in a broader scope.");
                    return results.ToString();
                }

                // Add a hint when multiple overloads are found
                var methodSymbol = (await (serviceProvider?.GetService<IQueryService>()!)
                    .FindMethodSymbolsAsync(methodName, projectName))
                    .FirstOrDefault();

                if (methodSymbol != null)
                {
                    results.AppendLine($"*Target method for analysis: `{methodSymbol.ToDisplayString()}`*");
                    results.AppendLine();
                }

                foreach (var invocation in invocationList)
                {
                    results.AppendLine($"- **Call**: `{invocation.CalledMethodName}`");
                    results.AppendLine($"  - **Containing Type**: `{invocation.ContainingType}`");
                    results.AppendLine($"  - **Location**: {Path.GetFileName(invocation.FilePath)}({invocation.LineNumber})");
                    results.AppendLine();
                }

                if (outputAsJson)
                {
                    var groupedDict = invocationList
                        .GroupBy(i => i.ContainingType)
                        .ToDictionary(
                            g => g.Key,
                            g => g.ToList()
                        );
                    
                    return JsonResponseFormatter.ToJson(new GetMethodBodyInvocationsResponse
                    {
                        Success = true,
                        MethodName = methodName,
                        ProjectName = projectName,
                        TotalCount = invocationList.Count,
                        DisplayedCount = invocationList.Count,
                        IsTruncated = false,
                        Invocations = invocationList,
                        GroupedByType = groupedDict
                    });
                }

                return results.ToString();
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Failed to get method body invocations for {MethodName}", methodName);
                var errorMsg = $"Error: An unexpected error occurred: {ex.Message}";
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new GetMethodBodyInvocationsResponse
                    {
                        Success = false,
                        Error = errorMsg,
                        MethodName = methodName,
                        ProjectName = projectName
                    });
                }
                return errorMsg;
            }
        }


    }
}
