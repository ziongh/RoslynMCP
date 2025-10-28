using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMCP.Query.Services;
using RoslynMCP.MCP.Services;
using RoslynMCP.MCP.Models;
using RoslynMCP.MCP.Utils;

namespace RoslynMCP.MCP.Tools
{
    public static partial class RoslynAnalysisTools
    {
        /// <summary>
        /// Get the complete source code of a symbol
        /// </summary>
        [McpServerTool, Description("Get the full source code of any specific symbol, including classes, methods, properties, fields, etc.")]
        public static async Task<string> GetSourceCode(
            [Description("Exact symbol name or fully qualified name of any symbol (e.g., 'MyClass', 'MyClass.MyMethod')")]
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
                    var errorMsg = "❌ No solution is loaded. Please use the `SwitchSolution` tool first.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new GetSourceCodeResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }

                var queryService = serviceProvider?.GetService<IQueryService>();
                if (queryService == null)
                {
                    var errorMsg = "❌ Query service is not available.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new GetSourceCodeResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }

                logger?.LogInformation("Getting source code for symbol: {SymbolName}", symbolName);
                var sourceCode = await queryService.GetSourceCodeAsync(symbolName);
                
                // Get symbol details to check if it's from metadata
                var symbolDetails = await queryService.GetSymbolDetailsAsync(symbolName);

                if (sourceCode == null)
                {
                    var errorMsg = $"## Source Code Not Found\n\nSymbol `{symbolName}` was not found or has no source code.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new GetSourceCodeResponse
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
                    return JsonResponseFormatter.ToJson(new GetSourceCodeResponse
                    {
                        Success = true,
                        SymbolName = symbolName,
                        SourceCode = sourceCode,
                        IsFromMetadata = symbolDetails?.IsFromMetadata ?? false
                    });
                }
                
                return $"## Source Code for `{symbolName}`\n\n```csharp\n{sourceCode}\n```";
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Failed to get source code for symbol: {SymbolName}", symbolName);
                var errorMsg = $"Error: An unexpected error occurred: {ex.Message}";
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new GetSourceCodeResponse
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
        /// Get the full content of a file
        /// </summary>
        [McpServerTool, Description("Get the full content of a source file within the solution")]
        public static async Task<string> GetFileContent(
            [Description("Path to the file relative to the solution root directory (e.g., 'src/MyProject/MyFile.cs'). Absolute paths are not supported for security reasons.")]
            string filePath,
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
                    var errorMsg = "❌ No solution is loaded. Please use the `SwitchSolution` tool first.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new GetFileContentResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }

                var queryService = serviceProvider?.GetService<IQueryService>();
                if (queryService == null)
                {
                    var errorMsg = "❌ Query service is not available.";
                    
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new GetFileContentResponse
                        {
                            Success = false,
                            Error = errorMsg
                        });
                    }
                    return errorMsg;
                }

                logger?.LogInformation("Getting content for file: {FilePath}", filePath);
                var fileContent = await queryService.GetFileContentAsync(filePath);

                if (fileContent == null)
                {
                    if (outputAsJson)
                    {
                        return JsonResponseFormatter.ToJson(new GetFileContentResponse
                        {
                            Success = false,
                            Error = "File content not found",
                            FilePath = filePath
                        });
                    }
                    
                    var results = new StringBuilder();
                    results.AppendLine($"## 📁 File content not found");
                    results.AppendLine();
                    results.AppendLine($"**File path**: `{filePath}`");
                    results.AppendLine();
                    results.AppendLine("**Possible reasons**:");
                    results.AppendLine("- File path is incorrect or file does not exist");
                    results.AppendLine("- File is not within the current solution scope");
                    results.AppendLine("- Insufficient file permissions or file is occupied by another program");
                    results.AppendLine();
                    results.AppendLine("**Suggestions to try**:");
                    results.AppendLine("- Check if the path is correct (must be relative to the solution root directory)");
                    results.AppendLine("- Use `ListProjects` to view projects and files in the solution");
                    results.AppendLine("- Confirm that the file actually exists in the solution");
                    results.AppendLine("- Ensure using relative paths instead of absolute paths");
                    results.AppendLine();
                    results.AppendLine("**Path format examples**:");
                    results.AppendLine("- Correct: `src/MyProject/MyFile.cs`");
                    results.AppendLine("- Incorrect: `C:\\Solution\\src\\MyProject\\MyFile.cs` (absolute paths not supported)");
                    return results.ToString();
                }
                
                var fileName = Path.GetFileName(filePath);
                var fileExtension = Path.GetExtension(filePath).TrimStart('.');
                var lineCount = fileContent.Split('\n').Length;
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new GetFileContentResponse
                    {
                        Success = true,
                        FilePath = filePath,
                        FileName = fileName,
                        FileExtension = fileExtension,
                        Content = fileContent,
                        LineCount = lineCount
                    });
                }
                
                return $"## Content of `{fileName}`\n\n```{fileExtension}\n{fileContent}\n```";
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Failed to get file content: {FilePath}", filePath);
                var errorMsg = $"Error: An unexpected error occurred: {ex.Message}";
                
                if (outputAsJson)
                {
                    return JsonResponseFormatter.ToJson(new GetFileContentResponse
                    {
                        Success = false,
                        Error = errorMsg,
                        FilePath = filePath
                    });
                }
                return errorMsg;
            }
        }
    }
}
