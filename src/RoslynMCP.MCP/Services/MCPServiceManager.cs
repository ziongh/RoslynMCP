using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using RoslynMCP.SymbolCache.Security;
using RoslynMCP.Query.Services;
using RoslynMCP.Analysis.Services;
using RoslynMCP.SymbolCache;
using System.Threading;
using RoslynMCP.MCP.Utils;
using System.Text;

namespace RoslynMCP.MCP.Services
{
    /// <summary>
    /// MCP应用级服务管理器 - 协调解决方案状态管理器和应用服务
    /// </summary>
    public interface IMCPServiceManager
    {
        /// <summary>
        /// 当前解决方案路径
        /// </summary>
        string? CurrentSolutionPath { get; }

        /// <summary>
        /// 解决方案是否已加载并且服务已初始化
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// 服务是否已初始化
        /// </summary>
        bool IsServicesInitialized { get; }

        /// <summary>
        /// 获取符号缓存服务
        /// </summary>
        Core.Interfaces.ISymbolCacheService? SymbolCache { get; }

        /// <summary>
        /// 加载解决方案并初始化所有相关服务
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
        
        /// <summary>
        /// 获取当前解决方案状态，如果正在加载则等待加载完成
        /// </summary>
        /// <param name="waitForLoading">是否等待正在进行的加载操作</param>
        /// <param name="maxWaitMs">最大等待时间（毫秒）</param>
        /// <returns>状态信息</returns>
        Task<string> GetStatusAsync(bool waitForLoading = true, int maxWaitMs = 30000);
    }

    /// <summary>
    /// MCP应用级服务管理器实现
    /// </summary>
    public class MCPServiceManager : IMCPServiceManager
    {
        private readonly ILogger<MCPServiceManager> _logger;
        private readonly ISolutionStateManager _solutionStateManager;
        private readonly IQueryService _queryService;
        private readonly IAnalysisService _analysisService;
        private readonly AnalyzerOptions _options;
        private bool _isServicesInitialized;
        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);


        public MCPServiceManager(
            ILogger<MCPServiceManager> logger,
            ISolutionStateManager solutionStateManager,
            IQueryService queryService,
            IAnalysisService analysisService,
            IOptions<AnalyzerOptions> options)
        {
            _logger = logger;
            _solutionStateManager = solutionStateManager;
            _queryService = queryService;
            _analysisService = analysisService;
            _options = options.Value;

            // 启动时尝试加载默认解决方案
            _ = Task.Run(async () => await TryLoadDefaultSolutionAsync());
        }

        public string? CurrentSolutionPath => _solutionStateManager.CurrentSolutionPath;
        public bool IsLoaded => _solutionStateManager.IsLoaded && _isServicesInitialized;
        public bool IsServicesInitialized => _isServicesInitialized;
        public Core.Interfaces.ISymbolCacheService? SymbolCache => _solutionStateManager.SymbolCache;

        public async Task<string> LoadSolutionAsync(string solutionPath, List<string>? namespacePrefixes = null)
        {
            if (!await _loadLock.WaitAsync(0))
            {
                return "⚠️ 另一个解决方案加载操作正在进行中，请稍后再试。";
            }

            try
            {
                _logger.LogInformation("MCP服务管理器开始加载解决方案: {Path}", solutionPath);

                // 1. 使用解决方案状态管理器加载解决方案和符号缓存
                var loadResult = await _solutionStateManager.LoadSolutionAsync(solutionPath, namespacePrefixes);
                if (!_solutionStateManager.IsLoaded || _solutionStateManager.SymbolCache == null)
                {
                    _isServicesInitialized = false;
                    return loadResult;
                }

                // 2. 初始化查询服务
                var queryInitialized = await _queryService.InitializeAsync(_solutionStateManager.SymbolCache);
                if (!queryInitialized)
                {
                    _isServicesInitialized = false;
                    return "❌ 查询服务初始化失败";
                }

                // 3. 初始化分析服务
                var analysisInitialized = await _analysisService.InitializeAsync();
                if (!analysisInitialized)
                {
                    _isServicesInitialized = false;
                    return "❌ 分析服务初始化失败";
                }

                _isServicesInitialized = true;
                _logger.LogInformation("MCP服务管理器已完成所有服务初始化");

                // 增强状态信息
                var enhancedStatus = loadResult + "\n" +
                                   "🔧 查询服务: 已初始化 ✅\n" +
                                   "📊 分析服务: 已初始化 ✅\n" +
                                   "💡 所有服务已就绪，可以使用分析工具";
                
                return enhancedStatus;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP服务管理器加载失败: {Path}", solutionPath);
                _isServicesInitialized = false;
                return $"❌ MCP服务管理器加载失败: {ex.Message}";
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public string GetStatus()
        {
            return GetStatusInternal();
        }
        
        public async Task<string> GetStatusAsync(bool waitForLoading = true, int maxWaitMs = 30000)
        {
            // 如果正在加载且用户希望等待，则等待加载完成
            if (waitForLoading && _loadLock.CurrentCount == 0)
            {
                _logger.LogDebug("检测到正在加载，等待加载完成...");
                
                try
                {
                    // 使用CancellationToken控制最大等待时间
                    using var cts = new CancellationTokenSource(maxWaitMs);
                    
                    // 等待获取锁（即等待加载完成）
                    await _loadLock.WaitAsync(cts.Token);
                    
                    // 立即释放锁，因为我们只是想等待加载完成
                    _loadLock.Release();
                    
                    _logger.LogDebug("加载完成，返回最新状态");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("等待加载完成超时（{MaxWaitMs}ms），返回当前状态", maxWaitMs);
                    return GetStatusInternal() + "\n\n⚠️ 等待加载完成超时，状态可能不是最新的。";
                }
            }
            
            return GetStatusInternal();
        }
        
        private string GetStatusInternal()
        {
            var baseStatus = new StringBuilder(_solutionStateManager.GetStatus());
            
            if (_solutionStateManager.IsLoaded)
            {
                baseStatus.AppendLine("\n\n**MCP服务状态**:");
                baseStatus.AppendLine($"🔧 查询服务: {(_queryService.IsInitialized ? "已初始化 ✅" : "未初始化 ❌")}");
                baseStatus.AppendLine($"📊 分析服务: {(_analysisService.IsInitialized ? "已初始化 ✅" : "未初始化 ❌")}");
                
                if (_isServicesInitialized)
                {
                    baseStatus.AppendLine("💡 所有服务已就绪，可以使用分析工具");

                    // 添加已加载的项目列表
                    try
                    {
                        var projects = _queryService.GetProjectsAsync().Result.ToList();
                        if (projects.Any())
                        {
                            baseStatus.AppendLine("\n**已加载项目**:");
                            const int maxProjectsToShow = 10;
                            foreach (var project in projects.Take(maxProjectsToShow))
                            {
                                baseStatus.AppendLine($"- {project.Name}");
                            }
                            if (projects.Count > maxProjectsToShow)
                            {
                                baseStatus.AppendLine($"... 还有 {projects.Count - maxProjectsToShow} 个项目。使用 'ListProjects' 工具查看全部。");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "获取项目列表以显示状态时出错");
                        baseStatus.AppendLine("\n⚠️ 无法获取已加载的项目列表。");
                    }
                }
                else if (_loadLock.CurrentCount == 0)
                {
                    baseStatus.AppendLine("⏳ 服务正在初始化中...");
                }
                else
                {
                    baseStatus.AppendLine("⚠️ 服务初始化失败或未开始。");
                }
            }

            return baseStatus.ToString();
        }

        private async Task TryLoadDefaultSolutionAsync()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_options.DefaultSolutionPath))
                {
                    // 解析默认命名空间前缀
                    List<string>? namespacePrefixes = null;
                    if (!string.IsNullOrWhiteSpace(_options.DefaultNamespacePrefixes))
                    {
                        namespacePrefixes = _options.DefaultNamespacePrefixes
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(prefix => prefix.Trim())
                            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                            .ToList();
                        
                        _logger.LogInformation("使用默认命名空间前缀: {Prefixes}", string.Join(", ", namespacePrefixes));
                    }

                    var result = await LoadSolutionAsync(_options.DefaultSolutionPath, namespacePrefixes);
                    _logger.LogInformation("Default solution load result: {Result}", result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load default solution");
            }
        }
    }
}
