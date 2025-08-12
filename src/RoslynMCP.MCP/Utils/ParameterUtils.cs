using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace RoslynMCP.MCP.Utils
{
    public static class ParameterUtils
    {
        /// <summary>
        /// 获取用户传入的最大显示数量，或从配置中获取，或使用备用默认值
        /// </summary>
        public static int GetMaxDisplayResultsDefault(int userValue, IServiceProvider? serviceProvider, int fallbackDefaultValue = 20)
        {
            if (userValue > 0) return userValue;

            var options = serviceProvider?.GetService<IOptions<AnalyzerOptions>>()?.Value;
            // 使用 MaxQueryResults 作为所有显示限制的通用配置
            return options?.MaxQueryResults ?? fallbackDefaultValue;
        }



        /// <summary>
        /// 生成结果截断的标准提示信息
        /// </summary>
        /// <param name="totalCount">总数量</param>
        /// <param name="displayCount">显示数量</param>
        /// <param name="parameterName">参数名称</param>
        /// <param name="additionalAction">额外建议操作</param>
        /// <returns>提示信息</returns>
        public static string GenerateTruncationHint(int totalCount, int displayCount, string parameterName, string? additionalAction = null)
        {
            if (totalCount <= displayCount) return string.Empty;

            var hint = $"*(displaying first {displayCount} of {totalCount} total - increase {parameterName} to see more";
            if (!string.IsNullOrEmpty(additionalAction))
            {
                hint += $" or {additionalAction}";
            }
            hint += ")*";
            
            return hint;
        }

        /// <summary>
        /// 生成搜索结果截断的提示信息（包含额外的搜索建议）
        /// </summary>
        /// <param name="totalCount">总数量</param>
        /// <param name="displayCount">显示数量</param>
        /// <param name="parameterName">参数名称</param>
        /// <returns>提示信息</returns>
        public static string GenerateSearchTruncationHint(int totalCount, int displayCount, string parameterName)
        {
            return GenerateTruncationHint(totalCount, displayCount, parameterName, "use a more specific pattern");
        }
    }
}
