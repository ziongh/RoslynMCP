using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace RoslynMCP.MCP.Utils
{
    public static class ParameterUtils
    {
        /// <summary>
        /// Get user-provided maximum display count, or from configuration, or use fallback default value
        /// </summary>
        public static int GetMaxDisplayResultsDefault(int userValue, IServiceProvider? serviceProvider, int fallbackDefaultValue = 20)
        {
            if (userValue > 0) return userValue;

            var options = serviceProvider?.GetService<IOptions<AnalyzerOptions>>()?.Value;
            // Use MaxQueryResults as universal configuration for all display limits
            return options?.MaxQueryResults ?? fallbackDefaultValue;
        }



        /// <summary>
        /// Generate standard prompt information for result truncation
        /// </summary>
        /// <param name="totalCount">Total count</param>
        /// <param name="displayCount">Display count</param>
        /// <param name="parameterName">Parameter name</param>
        /// <param name="additionalAction">Additional suggested action</param>
        /// <returns>Prompt information</returns>
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
        /// Generate prompt information for search result truncation (including additional search suggestions)
        /// </summary>
        /// <param name="totalCount">Total count</param>
        /// <param name="displayCount">Display count</param>
        /// <param name="parameterName">Parameter name</param>
        /// <returns>Prompt information</returns>
        public static string GenerateSearchTruncationHint(int totalCount, int displayCount, string parameterName)
        {
            return GenerateTruncationHint(totalCount, displayCount, parameterName, "use a more specific pattern");
        }
    }
}
