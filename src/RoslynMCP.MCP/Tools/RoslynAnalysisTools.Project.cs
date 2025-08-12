using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        /// 列出解决方案中的所有项目及其依赖
        /// </summary>
        [McpServerTool, Description("List all projects in the current solution and their dependencies.")]
        public static async Task<string> ListProjects(
            [Description("Maximum number of projects to display, or 0/-1 to show all.")]
            int maxProjects = 5,
            [Description("Maximum number of package references to display per project, or 0/-1 to show all.")]
            int maxPackages = 5,
            [Description("Whether to show system packages in the detailed view.")]
            bool showSystemPackages = false,
            IServiceProvider? serviceProvider = null)
        {
            try
            {
                var logger = serviceProvider?.GetService<ILogger>();
                var solutionmcpServiceManager = serviceProvider?.GetService<IMCPServiceManager>();

                // 设置默认值
                maxPackages = ParameterUtils.GetMaxDisplayResultsDefault(maxPackages, serviceProvider);

                if (solutionmcpServiceManager == null || !solutionmcpServiceManager.IsLoaded)
                {
                    return "❌ No solution is loaded. Please use the `SwitchSolution` tool first.";
                }

                var queryService = serviceProvider?.GetService<IQueryService>();
                if (queryService == null)
                {
                    return "❌ Query service is not available.";
                }

                logger?.LogInformation("Listing projects for solution: {SolutionPath}", solutionmcpServiceManager.CurrentSolutionPath);
                var projects = (await queryService.GetProjectsAsync()).ToList();
                var totalProjects = projects.Count;

                var results = new StringBuilder();
                results.AppendLine($"# Projects in Solution: {Path.GetFileName(solutionmcpServiceManager.CurrentSolutionPath!)} ({totalProjects} total)");
                results.AppendLine();

                var projectsToShow = maxProjects <= 0 ? projects : projects.Take(maxProjects).ToList();

                foreach (var project in projectsToShow)
                {
                    results.AppendLine($"## {project.Name}");
                    results.AppendLine($"- **Path**: {project.FilePath}");
                    if (project.ProjectReferences.Any())
                    {
                        results.AppendLine("- **Project References**:");
                        foreach (var pref in project.ProjectReferences)
                        {
                            results.AppendLine($"  - {pref}");
                        }
                    }
                    if (project.PackageReferences.Any())
                    {
                        var totalPackages = project.PackageReferences.Count();
                        results.AppendLine($"- **Package References** ({totalPackages} total):");
                        
                        if (maxPackages == -1)
                        {
                            var grouped = ListFormattingUtils.GroupPackageReferences(project.PackageReferences.ToList());
                            results.Append(ListFormattingUtils.FormatAllPackageReferences(grouped));
                        }
                        else
                        {
                            var (formattedOutput, wasTruncated) = ListFormattingUtils.FormatPackageReferences(
                                project.PackageReferences, maxPackages, showSystemPackages);
                            results.Append(formattedOutput);
                            
                            if (wasTruncated)
                            {
                                var groups = ListFormattingUtils.GroupPackageReferences(project.PackageReferences.ToList());
                                var groupedHint = ListFormattingUtils.GenerateGroupedTruncationHint(
                                    totalPackages, maxPackages, groups, "maxPackages");
                                if (!string.IsNullOrEmpty(groupedHint))
                                {
                                    results.AppendLine(groupedHint);
                                }
                            }
                        }
                    }
                    results.AppendLine();
                }

                if (totalProjects > maxProjects && maxProjects > 0)
                {
                    results.AppendLine("---");
                    results.AppendLine($"*Showing first {maxProjects} of {totalProjects} projects. Use `maxProjects: -1` to show all.*");
                }

                return results.ToString();
            }
            catch (Exception ex)
            {
                var logger = serviceProvider?.GetService<ILogger>();
                logger?.LogError(ex, "Failed to list projects");
                return $"Error: An unexpected error occurred: {ex.Message}";
            }
        }

        /// <summary>
        /// 获取单个项目的详细依赖
        /// </summary>
        [McpServerTool, Description("Get detailed dependencies for a single project")]
        public static async Task<string> GetProjectDependencies(
            [Description("The name of the project to analyze")]
            string projectName,
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

                logger?.LogInformation("Getting dependencies for project: {ProjectName}", projectName);
                var project = await queryService.GetProjectDependenciesAsync(projectName);

                if (project == null)
                {
                    return $"## Project Not Found\n\nProject `{projectName}` could not be found in the solution.";
                }

                var results = new StringBuilder();
                results.AppendLine($"# Dependencies for Project: {project.Name}");
                results.AppendLine();
                results.AppendLine($"**Path**: {project.FilePath}");
                results.AppendLine();

                results.AppendLine("## Project References");
                if (project.ProjectReferences.Any())
                {
                    foreach (var pref in project.ProjectReferences)
                    {
                        results.AppendLine($"- {pref}");
                    }
                }
                else
                {
                    results.AppendLine("None");
                }
                results.AppendLine();

                results.AppendLine("## Package References");
                if (project.PackageReferences.Any())
                {
                    foreach (var pkgref in project.PackageReferences)
                    {
                        results.AppendLine($"- {pkgref}");
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
                logger?.LogError(ex, "Failed to get project dependencies for {ProjectName}", projectName);
                return $"Error: An unexpected error occurred: {ex.Message}";
            }
        }
    }
}
