using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RoslynMCP.MCP.Utils
{
    /// <summary>
    /// 列表格式化工具类，提供智能摘要和分组功能
    /// </summary>
    public static class ListFormattingUtils
    {
        /// <summary>
        /// 对包引用进行智能分组和摘要
        /// </summary>
        /// <param name="packageReferences">包引用列表</param>
        /// <param name="maxDisplay">最大显示数量</param>
        /// <param name="showSystemPackages">是否显示系统包</param>
        /// <returns>格式化的包引用信息</returns>
        public static (string formattedOutput, bool wasTruncated) FormatPackageReferences(
            IEnumerable<string> packageReferences, 
            int maxDisplay = 10, 
            bool showSystemPackages = false)
        {
            var packages = packageReferences.ToList();
            if (!packages.Any())
            {
                return ("  - 无包引用", false);
            }

            var grouped = GroupPackageReferences(packages);
            var results = new StringBuilder();
            var totalCount = packages.Count;
            var displayCount = 0;
            var wasTruncated = false;

            // 显示解决方案内项目引用
            if (grouped.SolutionProjects.Any())
            {
                results.AppendLine("  **解决方案内项目**:");
                foreach (var package in grouped.SolutionProjects.Take(maxDisplay - displayCount))
                {
                    results.AppendLine($"    - {package}");
                    displayCount++;
                }
                if (grouped.SolutionProjects.Count > maxDisplay - displayCount)
                {
                    var remaining = grouped.SolutionProjects.Count - (maxDisplay - displayCount);
                    results.AppendLine($"    - ... 还有 {remaining} 个项目引用");
                    wasTruncated = true;
                }
            }

            // 显示第三方库
            if (grouped.ThirdPartyLibraries.Any() && displayCount < maxDisplay)
            {
                results.AppendLine("  **第三方库**:");
                var remainingSlots = maxDisplay - displayCount;
                foreach (var package in grouped.ThirdPartyLibraries.Take(remainingSlots))
                {
                    results.AppendLine($"    - {package}");
                    displayCount++;
                }
                if (grouped.ThirdPartyLibraries.Count > remainingSlots)
                {
                    var remaining = grouped.ThirdPartyLibraries.Count - remainingSlots;
                    results.AppendLine($"    - ... 还有 {remaining} 个第三方库");
                    wasTruncated = true;
                }
            }

            // 显示系统包（如果请求且有剩余空间）
            if (showSystemPackages && grouped.SystemAssemblies.Any() && displayCount < maxDisplay)
            {
                results.AppendLine("  **系统程序集**:");
                var remainingSlots = maxDisplay - displayCount;
                foreach (var package in grouped.SystemAssemblies.Take(remainingSlots))
                {
                    results.AppendLine($"    - {package}");
                    displayCount++;
                }
                if (grouped.SystemAssemblies.Count > remainingSlots)
                {
                    var remaining = grouped.SystemAssemblies.Count - remainingSlots;
                    results.AppendLine($"    - ... 还有 {remaining} 个系统程序集");
                    wasTruncated = true;
                }
            }
            else if (grouped.SystemAssemblies.Any())
            {
                results.AppendLine($"  **系统程序集**: {grouped.SystemAssemblies.Count} 个 (使用 showSystemPackages=true 显示)");
                wasTruncated = true;
            }

            // 添加摘要信息
            if (wasTruncated || !showSystemPackages)
            {
                results.AppendLine();
                results.AppendLine($"  **包引用摘要**: 共 {totalCount} 个包 ({grouped.SolutionProjects.Count} 项目, {grouped.ThirdPartyLibraries.Count} 第三方, {grouped.SystemAssemblies.Count} 系统)");
            }

            return (results.ToString(), wasTruncated);
        }

        public static string FormatAllPackageReferences(PackageReferenceGroups grouped)
        {
            var results = new StringBuilder();
            if (grouped.SolutionProjects.Any())
            {
                results.AppendLine("  **解决方案内项目**:");
                foreach (var package in grouped.SolutionProjects)
                {
                    results.AppendLine($"    - {package}");
                }
            }
            if (grouped.ThirdPartyLibraries.Any())
            {
                results.AppendLine("  **第三方库**:");
                foreach (var package in grouped.ThirdPartyLibraries)
                {
                    results.AppendLine($"    - {package}");
                }
            }
            if (grouped.SystemAssemblies.Any())
            {
                results.AppendLine("  **系统程序集**:");
                foreach (var package in grouped.SystemAssemblies)
                {
                    results.AppendLine($"    - {package}");
                }
            }
            return results.ToString();
        }

        /// <summary>
        /// 将包引用分组为不同类别
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
        /// 检查是否为系统程序集
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
        /// 检查是否为解决方案内项目
        /// </summary>
        private static bool IsSolutionProject(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;

            // 这里可以根据实际情况调整判断逻辑
            // 通常解决方案内项目不会有版本号，且可能包含特定的命名模式
            return !packageName.Contains(",") && !packageName.Contains("Version=");
        }

        /// <summary>
        /// 生成带分组信息的截断提示
        /// </summary>
        public static string GenerateGroupedTruncationHint(
            int totalCount, 
            int displayCount, 
            PackageReferenceGroups groups,
            string parameterName = "maxPackages")
        {
            if (totalCount <= displayCount) return string.Empty;

            var hint = new StringBuilder();
            hint.AppendLine($"*(显示前 {displayCount} 个，共 {totalCount} 个包引用)*");
            hint.AppendLine($"**完整分组统计**: {groups.SolutionProjects.Count} 项目, {groups.ThirdPartyLibraries.Count} 第三方, {groups.SystemAssemblies.Count} 系统");
            hint.AppendLine($"*使用 {parameterName}=-1 显示全部，或 GetProjectDependencies 获取详细信息*");
            
            return hint.ToString();
        }
    }

    /// <summary>
    /// 包引用分组结果
    /// </summary>
    public class PackageReferenceGroups
    {
        /// <summary>
        /// 解决方案内项目
        /// </summary>
        public List<string> SolutionProjects { get; set; } = new();

        /// <summary>
        /// 第三方库
        /// </summary>
        public List<string> ThirdPartyLibraries { get; set; } = new();

        /// <summary>
        /// 系统程序集
        /// </summary>
        public List<string> SystemAssemblies { get; set; } = new();
    }
}