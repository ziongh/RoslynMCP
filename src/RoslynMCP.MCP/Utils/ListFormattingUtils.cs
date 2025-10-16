using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RoslynMCP.MCP.Utils
{
    /// <summary>
    /// List formatting utility class, providing intelligent summarization and grouping functionality
    /// </summary>
    public static class ListFormattingUtils
    {
        /// <summary>
        /// Intelligently group and summarize package references
        /// </summary>
        /// <param name="packageReferences">Package reference list</param>
        /// <param name="maxDisplay">Maximum display count</param>
        /// <param name="showSystemPackages">Whether to show system packages</param>
        /// <returns>Formatted package reference information</returns>
        public static (string formattedOutput, bool wasTruncated) FormatPackageReferences(
            IEnumerable<string> packageReferences, 
            int maxDisplay = 10, 
            bool showSystemPackages = false)
        {
            var packages = packageReferences.ToList();
            if (!packages.Any())
            {
                return ("  - No package references", false);
            }

            var grouped = GroupPackageReferences(packages);
            var results = new StringBuilder();
            var totalCount = packages.Count;
            var displayCount = 0;
            var wasTruncated = false;

            // Display solution-internal project references
            if (grouped.SolutionProjects.Any())
            {
                results.AppendLine("  **Solution Projects**:");
                foreach (var package in grouped.SolutionProjects.Take(maxDisplay - displayCount))
                {
                    results.AppendLine($"    - {package}");
                    displayCount++;
                }
                if (grouped.SolutionProjects.Count > maxDisplay - displayCount)
                {
                    var remaining = grouped.SolutionProjects.Count - (maxDisplay - displayCount);
                    results.AppendLine($"    - ... and {remaining} more project references");
                    wasTruncated = true;
                }
            }

            // Display third-party libraries
            if (grouped.ThirdPartyLibraries.Any() && displayCount < maxDisplay)
            {
                results.AppendLine("  **Third-party Libraries**:");
                var remainingSlots = maxDisplay - displayCount;
                foreach (var package in grouped.ThirdPartyLibraries.Take(remainingSlots))
                {
                    results.AppendLine($"    - {package}");
                    displayCount++;
                }
                if (grouped.ThirdPartyLibraries.Count > remainingSlots)
                {
                    var remaining = grouped.ThirdPartyLibraries.Count - remainingSlots;
                    results.AppendLine($"    - ... and {remaining} more third-party libraries");
                    wasTruncated = true;
                }
            }

            // Display system packages (if requested and space remains)
            if (showSystemPackages && grouped.SystemAssemblies.Any() && displayCount < maxDisplay)
            {
                results.AppendLine("  **System Assemblies**:");
                var remainingSlots = maxDisplay - displayCount;
                foreach (var package in grouped.SystemAssemblies.Take(remainingSlots))
                {
                    results.AppendLine($"    - {package}");
                    displayCount++;
                }
                if (grouped.SystemAssemblies.Count > remainingSlots)
                {
                    var remaining = grouped.SystemAssemblies.Count - remainingSlots;
                    results.AppendLine($"    - ... and {remaining} more system assemblies");
                    wasTruncated = true;
                }
            }
            else if (grouped.SystemAssemblies.Any())
            {
                results.AppendLine($"  **System Assemblies**: {grouped.SystemAssemblies.Count} total (use showSystemPackages=true to display)");
                wasTruncated = true;
            }

            // Add summary information
            if (wasTruncated || !showSystemPackages)
            {
                results.AppendLine();
                results.AppendLine($"  **Package Reference Summary**: Total {totalCount} packages ({grouped.SolutionProjects.Count} projects, {grouped.ThirdPartyLibraries.Count} third-party, {grouped.SystemAssemblies.Count} system)");
            }

            return (results.ToString(), wasTruncated);
        }

        public static string FormatAllPackageReferences(PackageReferenceGroups grouped)
        {
            var results = new StringBuilder();
            if (grouped.SolutionProjects.Any())
            {
                results.AppendLine("  **Solution Projects**:");
                foreach (var package in grouped.SolutionProjects)
                {
                    results.AppendLine($"    - {package}");
                }
            }
            if (grouped.ThirdPartyLibraries.Any())
            {
                results.AppendLine("  **Third-party Libraries**:");
                foreach (var package in grouped.ThirdPartyLibraries)
                {
                    results.AppendLine($"    - {package}");
                }
            }
            if (grouped.SystemAssemblies.Any())
            {
                results.AppendLine("  **System Assemblies**:");
                foreach (var package in grouped.SystemAssemblies)
                {
                    results.AppendLine($"    - {package}");
                }
            }
            return results.ToString();
        }

        /// <summary>
        /// Group package references into different categories
        /// </summary>
        public static PackageReferenceGroups GroupPackageReferences(IList<string> packageReferences)
        {
            var groups = new PackageReferenceGroups();

            foreach (var package in packageReferences)
            {
                if (IsSystemAssembly(package))
                {
                    groups.SystemAssemblies.Add(package);
                }
                else if (IsSolutionProject(package))
                {
                    groups.SolutionProjects.Add(package);
                }
                else
                {
                    groups.ThirdPartyLibraries.Add(package);
                }
            }

            return groups;
        }

        /// <summary>
        /// Check if it's a system assembly
        /// </summary>
        private static bool IsSystemAssembly(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;

            var systemPrefixes = new[]
            {
                "System.", "Microsoft.", "Windows.", "netstandard", "mscorlib",
                "WindowsBase", "PresentationCore", "PresentationFramework"
            };

            return systemPrefixes.Any(prefix => 
                packageName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Check if it's a solution project
        /// </summary>
        private static bool IsSolutionProject(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;

            // Logic can be adjusted based on actual situation
            // Usually solution projects don't have version numbers and may contain specific naming patterns
            return !packageName.Contains(",") && !packageName.Contains("Version=");
        }

        /// <summary>
        /// Generate truncation hint with grouping information
        /// </summary>
        public static string GenerateGroupedTruncationHint(
            int totalCount, 
            int displayCount, 
            PackageReferenceGroups groups,
            string parameterName = "maxPackages")
        {
            if (totalCount <= displayCount) return string.Empty;

            var hint = new StringBuilder();
            hint.AppendLine($"*(Showing first {displayCount} of {totalCount} package references)*");
            hint.AppendLine($"**Complete grouping statistics**: {groups.SolutionProjects.Count} projects, {groups.ThirdPartyLibraries.Count} third-party, {groups.SystemAssemblies.Count} system");
            hint.AppendLine($"*Use {parameterName}=-1 to show all, or GetProjectDependencies for detailed information*");
            
            return hint.ToString();
        }
    }

    /// <summary>
    /// Package reference grouping results
    /// </summary>
    public class PackageReferenceGroups
    {
        /// <summary>
        /// Solution projects
        /// </summary>
        public List<string> SolutionProjects { get; set; } = new();

        /// <summary>
        /// Third-party libraries
        /// </summary>
        public List<string> ThirdPartyLibraries { get; set; } = new();

        /// <summary>
        /// System assemblies
        /// </summary>
        public List<string> SystemAssemblies { get; set; } = new();
    }
}