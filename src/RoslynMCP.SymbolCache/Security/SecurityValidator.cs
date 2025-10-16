using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace RoslynMCP.SymbolCache.Security
{
    /// <summary>
    /// Security validator for validating path and input security
    /// </summary>
    public class SecurityValidator
    {
        private readonly HashSet<string> _allowedExtensions = new() { ".sln", ".csproj", ".cs" };
        private readonly Regex _windowsRootedPath = new(@"^[a-zA-Z]:[\\/][^<>:|?*]+$");
        private readonly Regex _unixRootedPath = new(@"^/[^\0<>:|?*]+$");
        private readonly ILogger<SecurityValidator> _logger;
        
        public SecurityValidator(ILogger<SecurityValidator> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Validate the security of solution file paths
        /// </summary>
        public bool ValidateSolutionPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.LogWarning("Solution path is empty");
                return false;
            }
            
            // Check for path traversal attacks
            if (path.Contains("..") || path.Contains("~"))
            {
                _logger.LogWarning("Potential path traversal attack detected: {Path}", path);
                return false;
            }
            
            // Validate path format
            if (!_windowsRootedPath.IsMatch(path) && !_unixRootedPath.IsMatch(path))
            {
                _logger.LogWarning("Unsafe path format: {Path}", path);
                return false;
            }
            
            // Check file extension
            var extension = Path.GetExtension(path);
            if (!_allowedExtensions.Contains(extension))
            {
                _logger.LogWarning("Disallowed file extension: {Extension} in {Path}", extension, path);
                return false;
            }
            
            // Validate file exists and is accessible
            try
            {
                var exists = File.Exists(path);
                if (!exists)
                {
                    _logger.LogWarning("File does not exist: {Path}", path);
                }
                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to access file: {Path}", path);
                return false;
            }
        }
        
        /// <summary>
        /// Clean search pattern, remove potentially dangerous characters
        /// </summary>
        public string SanitizeSearchPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return "*";
            
            // 移除潜在危险字符，只保留字母、数字、通配符和点
            return Regex.Replace(pattern, @"[^\w*?.]", "");
        }

        /// <summary>
        /// Validate the security of cache keys
        /// </summary>
        public bool ValidateCacheKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            // Cache key should only contain safe characters
            return Regex.IsMatch(key, @"^[a-zA-Z0-9._-]+$");
        }
    }
}