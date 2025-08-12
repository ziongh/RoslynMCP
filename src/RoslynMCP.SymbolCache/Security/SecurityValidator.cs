using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace RoslynMCP.SymbolCache.Security
{
    /// <summary>
    /// 安全验证器，用于验证路径和输入的安全性
    /// </summary>
    public class SecurityValidator
    {
        private readonly HashSet<string> _allowedExtensions = new() { ".sln", ".csproj", ".cs" };
        private readonly Regex _safePath = new(@"^[a-zA-Z]:[\\/][^<>:|?*]+$");
        private readonly ILogger<SecurityValidator> _logger;
        
        public SecurityValidator(ILogger<SecurityValidator> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// 验证解决方案文件路径的安全性
        /// </summary>
        public bool ValidateSolutionPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.LogWarning("解决方案路径为空");
                return false;
            }
            
            // 检查路径遍历攻击
            if (path.Contains("..") || path.Contains("~"))
            {
                _logger.LogWarning("检测到潜在的路径遍历攻击: {Path}", path);
                return false;
            }
            
            // 验证路径格式
            if (!_safePath.IsMatch(path))
            {
                _logger.LogWarning("不安全的路径格式: {Path}", path);
                return false;
            }
            
            // 检查文件扩展名
            var extension = Path.GetExtension(path);
            if (!_allowedExtensions.Contains(extension))
            {
                _logger.LogWarning("不允许的文件扩展名: {Extension} in {Path}", extension, path);
                return false;
            }
            
            // 验证文件存在且可访问
            try
            {
                var exists = File.Exists(path);
                if (!exists)
                {
                    _logger.LogWarning("文件不存在: {Path}", path);
                }
                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "访问文件失败: {Path}", path);
                return false;
            }
        }
        
        /// <summary>
        /// 清理搜索模式，移除潜在危险字符
        /// </summary>
        public string SanitizeSearchPattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return "*";
            
            // 移除潜在危险字符，只保留字母、数字、通配符和点
            return Regex.Replace(pattern, @"[^\w*?.]", "");
        }

        /// <summary>
        /// 验证缓存键的安全性
        /// </summary>
        public bool ValidateCacheKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            // 缓存键应该只包含安全字符
            return Regex.IsMatch(key, @"^[a-zA-Z0-9._-]+$");
        }
    }
}