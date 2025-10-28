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
using RoslynMCP.MCP.Models;
using static RoslynMCP.Analysis.Services.IAnalysisService;

namespace RoslynMCP.MCP.Tools
{
    public static partial class RoslynAnalysisTools
    {
        /// <summary>
        /// Search symbols (supports wildcards)
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
            [Description("Return response as JSON instead of formatted text (default: false)")]
            bool outputAsJson = false,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var options = serviceProvider?.GetService<IOptions<AnalyzerOptions>>()?.Value;
                var solutionmcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                // Check if solution is loaded
                if (solutionmcpServiceManager == null || !solutionmcpServiceManager.IsLoaded)
                {
                    var errorMsg = "❌ No solution loaded. Solution not loaded. Please use --solution parameter or set ROSLYN_MCP_SOLUTION_PATH environment variable to specify solution path.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new SearchSymbolsResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }

                var solutionPath = solutionmcpServiceManager.CurrentSolutionPath!;

                // Set default values
                maxResults = ParameterUtils.GetMaxDisplayResultsDefault(maxResults, serviceProvider);

                // Validate input
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    var errorMsg = "❌ Search pattern cannot be empty.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new SearchSymbolsResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }

                // Get query service
                var queryService = serviceProvider?.GetService<IQueryService>();
                if (queryService == null)
                {
                    var errorMsg = "Error: Query service not available.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new SearchSymbolsResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }

                logger?.LogInformation("Searching symbols: {Pattern} in solution: {SolutionPath}", pattern, solutionPath);

                // Ensure solution is loaded and service is initialized
                var (success, error) = await EnsureSolutionLoadedAsync(serviceProvider, logger);
                if (!success)
                {
                    var errorMsg = $"Error: {error}";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new SearchSymbolsResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }

                // Build search request
                var symbolTypesList = symbolTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var searchRequest = new SymbolSearchRequest
                {
                    Pattern = pattern,
                    SymbolKinds = symbolTypesList,
                    MaxResults = maxResults,
                    CaseSensitive = caseSensitive
                };

                // Execute search
                var searchResults = await queryService.SearchSymbolsAsync(searchRequest);

                // Apply filters
                var filteredResults = searchResults.AsEnumerable();
                
                if (excludeGeneratedFiles)
                {
                    filteredResults = filteredResults.Where(r => !IsGeneratedFile(r.FilePath));
                }
                
                if (excludeSystemTypes)
                {
                    filteredResults = filteredResults.Where(r => !IsSystemType(r.Name, r.SymbolKind));
                }

                // Format results
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
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new SearchSymbolsResponse
                        {
                            Success = true,
                            Pattern = pattern,
                            SolutionFileName = Path.GetFileName(solutionPath),
                            SymbolTypes = symbolTypes,
                            CaseSensitive = caseSensitive,
                            ExcludeGeneratedFiles = excludeGeneratedFiles,
                            ExcludeSystemTypes = excludeSystemTypes,
                            TotalCount = 0,
                            DisplayedCount = 0,
                            IsTruncated = false,
                            Results = new List<SymbolSearchResult>()
                        });
                    }
                    
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
                results.AppendLine("💡 **Tip**: To get more information about a symbol, including its solution-relative path, use the `get_symbol_details` tool with the symbol's `FullName`.");
                results.AppendLine();

                logger?.LogInformation("Search completed, found {Count} symbols", resultList.Count);
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new SearchSymbolsResponse
                    {
                        Success = true,
                        Pattern = pattern,
                        SolutionFileName = Path.GetFileName(solutionPath),
                        SymbolTypes = symbolTypes,
                        CaseSensitive = caseSensitive,
                        ExcludeGeneratedFiles = excludeGeneratedFiles,
                        ExcludeSystemTypes = excludeSystemTypes,
                        TotalCount = totalCount,
                        DisplayedCount = displayResults.Count,
                        IsTruncated = totalCount > maxResults,
                        Results = displayResults
                    });
                }
                
                return results.ToString();
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Symbol search failed: {Pattern}", pattern);
                var errorMsg = $"Error: An unexpected error occurred during symbol search: {ex.Message}";
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new SearchSymbolsResponse
                    {
                        Success = false,
                        Error = errorMsg
                    });
                }
                return errorMsg;
            }
        }

        /// <summary>
        /// Get detailed aggregated information about a symbol (including basic information, source code, references, inheritance relationships, etc.)
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
            [Description("Return response as JSON instead of formatted text (default: false)")]
            bool outputAsJson = false,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var solutionmcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                // Set default values
                var maxReferences = ParameterUtils.GetMaxDisplayResultsDefault(0, serviceProvider);

                // Check if solution is loaded
                if (solutionmcpServiceManager == null || !solutionmcpServiceManager.IsLoaded)
                {
                    var errorMsg = "❌ Solution not loaded. Please use --solution parameter or set ROSLYN_MCP_SOLUTION_PATH environment variable to specify solution path.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new GetSymbolDetailsResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }

                var solutionPath = solutionmcpServiceManager.CurrentSolutionPath!;

                if (string.IsNullOrWhiteSpace(symbolName))
                {
                    var errorMsg = "❌ Symbol name cannot be empty.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new GetSymbolDetailsResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }

                // Get services
                var queryService = serviceProvider?.GetService<IQueryService>();
                var analysisService = serviceProvider?.GetService<IAnalysisService>();
                if (queryService == null)
                {
                    var errorMsg = "❌ Query service is not available.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new GetSymbolDetailsResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }

                logger?.LogInformation("Getting symbol details: {SymbolName}", symbolName);

                // Ensure solution is loaded and services are initialized
                var (success, error) = await EnsureSolutionLoadedAsync(serviceProvider, logger);
                if (!success)
                {
                    var errorMsg = $"Error: {error}";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new GetSymbolDetailsResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }
                
                // Data to collect for JSON response
                SymbolDetails? symbolDetails = null;
                string? sourceCode = null;
                InheritanceHierarchy? hierarchy = null;
                List<ReferenceLocation> allReferences = new();

                // Build aggregated results
                var results = new StringBuilder();
                results.AppendLine($"# Complete Symbol Analysis: `{symbolName}`");
                results.AppendLine();
                results.AppendLine($"**Solution**: {Path.GetFileName(solutionPath)}");
                results.AppendLine($"**Analysis Date**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                results.AppendLine();

                // 1. Basic symbol information
                results.AppendLine("## 📋 Basic Information");
                try
                {
                    symbolDetails = await queryService.GetSymbolDetailsAsync(symbolName);
                    if (symbolDetails != null)
                    {
                        results.AppendLine($"- **Name**: `{symbolDetails.Name}`");
                        results.AppendLine($"- **Full Name**: `{symbolDetails.FullName}`");
                        results.AppendLine($"- **Type**: {symbolDetails.SymbolKind}");
                        results.AppendLine($"- **Namespace**: {symbolDetails.Namespace}");
                        results.AppendLine($"- **Assembly**: {symbolDetails.AssemblyName}");
                        results.AppendLine($"- **Accessibility**: {symbolDetails.Accessibility}");
                        results.AppendLine($"- **Source Location**: `{symbolDetails.SourceLocation}`");
                        
                        if (symbolDetails.IsFromMetadata)
                        {
                            results.AppendLine($"- **Source Type**: 🔷 **Metadata** (from NuGet/third-party assembly - decompiled)");
                        }
                        else
                        {
                            results.AppendLine($"- **Source Type**: 📄 **Source Code** (from solution)");
                        }

                        if (!string.IsNullOrEmpty(symbolDetails.Documentation))
                        {
                            results.AppendLine();
                            results.AppendLine("**Documentation**:");
                            results.AppendLine(symbolDetails.Documentation);
                        }
                    }
                    else
                    {
                        results.AppendLine($"⚠️ Unable to find basic information for symbol `{symbolName}`");
                    }
                }
                catch (Exception ex)
                {
                    results.AppendLine($"❌ Failed to get basic information: {ex.Message}");
                }
                results.AppendLine();

                // 2. Source code (if requested)
                if (includeSourceCode)
                {
                    results.AppendLine("## 📄 Source Code");
                    try
                    {
                        sourceCode = await queryService.GetSourceCodeAsync(symbolName);
                        if (!string.IsNullOrEmpty(sourceCode))
                        {
                            results.AppendLine("```csharp");
                            results.AppendLine(sourceCode);
                            results.AppendLine("```");
                        }
                        else
                        {
                            results.AppendLine("⚠️ Source code not available (may be external reference or generated at compile time)");
                        }
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"❌ Failed to get source code: {ex.Message}");
                    }
                    results.AppendLine();
                }

                // 3. Inheritance hierarchy (if requested and analysis service is available)
                if (includeInheritanceHierarchy && analysisService != null)
                {
                    results.AppendLine("## 🏗️ Inheritance Hierarchy");
                    try
                    {
                        hierarchy = await analysisService.GetInheritanceHierarchyAsync(symbolName);
                        if (hierarchy != null)
                        {
                            results.AppendLine("**Base class chain**:");
                            if (hierarchy.BaseNode != null)
                            {
                                var current = hierarchy.BaseNode;
                                var indent = "";
                                while (current != null)
                                {
                                    var line = $"{indent}- {current.SymbolName}";
                                    if (current.SymbolName == symbolName)
                                    {
                                        line += " *(current class)*";
                                    }
                                    results.AppendLine(line);
                                    current = current.Children.FirstOrDefault();
                                    indent += "  ";
                                }
                            }
                            else
                            {
                                results.AppendLine("- No base class");
                            }

                            results.AppendLine();
                            results.AppendLine("**Derived classes**:");
                            if (hierarchy.DerivedNodes.Any())
                            {
                                foreach (var derived in hierarchy.DerivedNodes)
                                {
                                    results.AppendLine($"- {derived.SymbolName}");
                                }
                            }
                            else
                            {
                                results.AppendLine("- No derived classes");
                            }
                        }
                        else
                        {
                            results.AppendLine("⚠️ Inheritance hierarchy information not available");
                        }
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"❌ Failed to get inheritance hierarchy: {ex.Message}");
                    }
                    results.AppendLine();
                }

                // 4. References (if requested)
                if (includeReferences)
                {
                    results.AppendLine("## 🔗 Reference Analysis");
                    try
                    {
                        var references = await queryService.FindReferencesAsync(symbolName);
                        allReferences = references.Where(r => !IsGeneratedFile(r.DocumentPath)).ToList();
                        var filteredReferences = allReferences.Take(maxReferences).ToList();

                        if (allReferences.Any())
                        {
                            var totalCount = allReferences.Count;
                            results.AppendLine($"Found **{totalCount}** references");
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
                            results.AppendLine("⚠️ No references found");
                        }
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"❌ Failed to get references: {ex.Message}");
                    }
                    results.AppendLine();
                }

                results.AppendLine("---");
                results.AppendLine("## 💡 Additional Tools");
                results.AppendLine();
                results.AppendLine("**For focused analysis**, use:");
                results.AppendLine("- `GetFileContent` - Complete file content with context");
                results.AppendLine("- `FindReferences` - Complete reference analysis");

                logger?.LogInformation("Symbol details retrieval completed: {SymbolName}", symbolName);
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new GetSymbolDetailsResponse
                    {
                        Success = true,
                        SymbolName = symbolName,
                        SolutionFileName = Path.GetFileName(solutionPath),
                        AnalysisDate = DateTime.Now,
                        BasicInfo = symbolDetails,
                        SourceCode = includeSourceCode ? sourceCode : null,
                        References = includeReferences ? allReferences : new List<ReferenceLocation>(),
                        InheritanceHierarchy = includeInheritanceHierarchy ? hierarchy : null
                    });
                }
                
                return results.ToString();
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Failed to get symbol details: {SymbolName}", symbolName);
                var errorMsg = $"Error: An unexpected error occurred while getting symbol details: {ex.Message}";
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new GetSymbolDetailsResponse
                    {
                        Success = false,
                        Error = errorMsg
                    });
                }
                return errorMsg;
            }
        }


    }
}
