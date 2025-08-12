using Microsoft.Extensions.Logging;
using RoslynMCP.Core.Interfaces;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis;
using RoslynMCP.SymbolCache.Security;

namespace RoslynMCP.SymbolCache
{
    /// <summary>
    /// 解决方案状态管理器 - 管理解决方案加载和符号缓存
    /// </summary>
    public interface ISolutionStateManager
    {
        /// <summary>
        /// 当前解决方案路径
        /// </summary>
        string? CurrentSolutionPath { get; }

        /// <summary>
        /// 解决方案是否已加载
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// 获取符号缓存服务
        /// </summary>
        ISymbolCacheService? SymbolCache { get; }

        /// <summary>
        /// 加载解决方案并创建符号缓存
        /// </summary>
        /// <param name="solutionPath">解决方案路径</param>
        /// <param name="namespacePrefixes">要包含的命名空间前缀列表</param>
        /// <returns>加载结果消息</returns>
        Task<string> LoadSolutionAsync(string solutionPath, List<string>? namespacePrefixes = null);

        /// <summary>
        /// 获取当前解决方案状态
        /// </summary>
        /// <returns>状态信息</returns>
        string GetStatus();
    }

    /// <summary>
    /// 解决方案状态管理器实现
    /// </summary>
    public class SolutionStateManager : ISolutionStateManager
    {
        private readonly ILogger<SolutionStateManager> _logger;
        private readonly SecurityValidator? _securityValidator;
        private readonly MSBuildWorkspace _workspace;
        private string? _currentSolutionPath;
        private List<string>? _currentNamespacePrefixes;
        private bool _isLoaded;
        private ISymbolCacheService? _symbolCache;

        public SolutionStateManager(
            ILogger<SolutionStateManager> logger,
            SecurityValidator? securityValidator = null,
            MSBuildWorkspace? workspace = null)
        {
            _logger = logger;
            _securityValidator = securityValidator;
            _workspace = workspace ?? MSBuildWorkspace.Create();
        }

        public string? CurrentSolutionPath => _currentSolutionPath;
        public bool IsLoaded => _isLoaded;
        public ISymbolCacheService? SymbolCache => _symbolCache;

        public async Task<string> LoadSolutionAsync(string solutionPath, List<string>? namespacePrefixes = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(solutionPath))
                {
                    return "❌ 解决方案路径不能为空";
                }

                // 验证路径安全性（如果提供了安全验证器）
                if (_securityValidator != null && !_securityValidator.ValidateSolutionPath(solutionPath))
                {
                    _logger.LogWarning("Invalid solution path attempted: {Path}", solutionPath);
                    return "❌ 无效的解决方案路径，路径必须在允许的目录范围内";
                }

                // 检查文件是否存在
                if (!File.Exists(solutionPath))
                {
                    return $"❌ 解决方案文件不存在: {solutionPath}";
                }

                // 验证是否为解决方案文件
                if (!Path.GetExtension(solutionPath).Equals(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    return "❌ 文件必须是 .sln 解决方案文件";
                }

                var fullPath = Path.GetFullPath(solutionPath);

                // 如果是相同路径和相同的前缀，直接返回成功
                if (_currentSolutionPath == fullPath && _isLoaded && 
                    AreNamespacePrefixesEqual(_currentNamespacePrefixes, namespacePrefixes))
                {
                    _logger.LogDebug("Solution already loaded with the same namespace prefixes: {Path}", fullPath);
                    return $"✅ 解决方案已加载: {Path.GetFileName(fullPath)}";
                }

                _currentSolutionPath = fullPath;
                _currentNamespacePrefixes = namespacePrefixes?.ToList();
                _isLoaded = false;

                _logger.LogInformation("Loading solution: {Path}, Prefixes: {Prefixes}", 
                    _currentSolutionPath, 
                    _currentNamespacePrefixes?.Any() == true ? string.Join(", ", _currentNamespacePrefixes) : "None");

                // 1. 使用单例 MSBuildWorkspace 实例提高性能和资源管理
                // 清理旧解决方案状态再打开新方案
                _workspace.CloseSolution();
                var solution = await _workspace.OpenSolutionAsync(_currentSolutionPath);
                
                // 2. 创建符号缓存服务
                var symbolCacheLogger = _logger as ILogger<SymbolCacheService>;
                _symbolCache = new SymbolCacheService(solution, _currentNamespacePrefixes, symbolCacheLogger);
                await _symbolCache.InitializeAsync();

                _isLoaded = true;
                _logger.LogInformation("Solution loaded and symbol cache initialized successfully: {Path}", _currentSolutionPath);

                var status = $"✅ 解决方案已成功加载: {Path.GetFileName(_currentSolutionPath)}\n" +
                             $"📁 路径: {_currentSolutionPath}\n" +
                             $"🏷️ 命名空间过滤器: {(_currentNamespacePrefixes?.Any() == true ? string.Join(", ", _currentNamespacePrefixes) : "无")}\n" +
                             $"💾 符号缓存: 已初始化 ✅";
                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load solution: {Path}", solutionPath);
                _isLoaded = false;
                return $"❌ 加载解决方案时发生错误: {ex.Message}";
            }
        }

        public string GetStatus()
        {
            if (!_isLoaded || string.IsNullOrEmpty(_currentSolutionPath))
            {
                return "📋 **解决方案状态**: 未加载\n" +
                       "💡 使用解决方案管理器加载解决方案文件";
            }

            var fileName = Path.GetFileName(_currentSolutionPath);
            var directory = Path.GetDirectoryName(_currentSolutionPath);

            var status = $"📋 **解决方案状态**: 已加载 ✅\n" +
                        $"📁 **文件名**: {fileName}\n" +
                        $"📂 **目录**: {directory}\n" +
                        $"🏷️ **命名空间过滤器**: {(_currentNamespacePrefixes?.Any() == true ? string.Join(", ", _currentNamespacePrefixes) : "无")}\n" +
                        $"🔧 **完整路径**: {_currentSolutionPath}\n" +
                        $"💾 **符号缓存**: {(_symbolCache?.IsInitialized == true ? "已初始化 ✅" : "未初始化 ❌")}";

            return status;
        }

        private static bool AreNamespacePrefixesEqual(List<string>? prefixes1, List<string>? prefixes2)
        {
            if (prefixes1 == null && prefixes2 == null) return true;
            if (prefixes1 == null || prefixes2 == null) return false;
            
            var set1 = new HashSet<string>(prefixes1);
            var set2 = new HashSet<string>(prefixes2);
            return set1.SetEquals(set2);
        }
    }
}
