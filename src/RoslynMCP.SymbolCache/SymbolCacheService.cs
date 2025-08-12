using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMCP.Core.Interfaces;
using static RoslynMCP.SymbolCache.RoslynUtils;
using System.Collections.Concurrent;
using Timer = RoslynMCP.Core.Utils.Timer;

namespace RoslynMCP.SymbolCache
{
    /// <summary>
    /// Symbol 缓存服务实现 - 精简版，专注增量更新
    /// </summary>
    public class SymbolCacheService : ISymbolCacheService
    {
        private readonly Solution _solution;
        private readonly ILogger<SymbolCacheService>? _logger;
        private readonly List<string> _namespacePrefixes;
        private readonly ConcurrentDictionary<string, ISymbol> _allSymbols = new();
        private readonly ConcurrentDictionary<string, INamedTypeSymbol> _protoSymbols = new();
        private bool _isInitialized;
        private DateTime _lastInitialized = DateTime.MinValue;

        public SymbolCacheService(Solution solution, List<string>? namespacePrefixes = null, ILogger<SymbolCacheService>? logger = null)
        {
            _solution = solution;
            _logger = logger;
            _namespacePrefixes = namespacePrefixes ?? new List<string>();
        }

        public bool IsInitialized => _isInitialized;
        public Solution? Solution => _solution;
        public IReadOnlyDictionary<string, ISymbol> AllSymbols => _allSymbols;
        public IReadOnlyDictionary<string, INamedTypeSymbol> ProtoSymbols => _protoSymbols;

        /// <summary>
        /// 全量初始化符号缓存 - 精简版
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            using var timer = Timer.Start("符号缓存初始化");
            
            await LoadSymbolsFromSolution();
            _isInitialized = true;
            _lastInitialized = DateTime.Now;

            _logger?.LogInformation($"符号缓存初始化完成：{_allSymbols.Count} 个符号，其中 {_protoSymbols.Count} 个 Proto 符号");
        }

        /// <summary>
        /// 从解决方案加载符号数据
        /// </summary>
        private async Task LoadSymbolsFromSolution()
        {
            _allSymbols.Clear();
            _protoSymbols.Clear();

            var projects = _solution.Projects.ToList();
            _logger?.LogInformation($"并行分析 {projects.Count} 个项目...");
            
            var projectTasks = projects.Select(async project =>
            {
                try 
                {
                    var compilation = await project.GetCompilationAsync();
                    return (project, compilation);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, $"项目 {project.Name} 加载失败");
                    return (project, (Compilation?)null);
                }
            }).ToArray();

            var projectResults = await Task.WhenAll(projectTasks);

            // 全量分析所有符号
            foreach (var (project, compilation) in projectResults)
            {
                if (compilation == null) continue;

                var projectSymbols = GetAllSymbols(compilation.GlobalNamespace)
                    .Where(MatchesNamespaceFilter); // 先过滤命名空间，提高性能

                foreach (var symbol in projectSymbols)
                {
                    var id = symbol.ToDisplayString();
                    // 使用TryAdd进行线程安全的添加操作
                    _allSymbols.TryAdd(id, symbol);
                }
            }
        }

        /// <summary>
        /// 增量更新指定文件的符号
        /// </summary>
        public async Task UpdateSymbolsAsync(string[] changedFiles)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("必须先调用 InitializeAsync() 进行初始化");
            }

            using var timer = Timer.Start("符号增量更新");
            
            var changedFileSet = new HashSet<string>(changedFiles);

            // 移除变化文件中的旧符号
            var symbolsToRemove = _allSymbols.Where(kv => 
            {
                var location = kv.Value.Locations.FirstOrDefault()?.SourceTree?.FilePath;
                return location != null && changedFileSet.Contains(location);
            }).ToList();

            foreach (var (id, _) in symbolsToRemove)
            {
                _allSymbols.TryRemove(id, out _);
                _protoSymbols.TryRemove(id, out _);
            }

            // 重新分析变化的文件
            var projects = _solution.Projects;
            foreach (var project in projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                var projectSymbols = GetAllSymbols(compilation.GlobalNamespace)
                    .Where(symbol => 
                    {
                        var location = symbol.Locations.FirstOrDefault()?.SourceTree?.FilePath;
                        return location != null && changedFileSet.Contains(location);
                    })
                    .Where(MatchesNamespaceFilter) // 同样先过滤命名空间，提高性能
                    .ToList();

                foreach (var symbol in projectSymbols)
                {
                    var id = symbol.ToDisplayString();
                    _allSymbols[id] = symbol;
                }
            }

            Console.WriteLine($"增量更新了 {changedFileSet.Count} 个文件中的符号");
        }

        public void ClearCache()
        {
            _allSymbols.Clear();
            _protoSymbols.Clear();
            _isInitialized = false;
            _lastInitialized = DateTime.MinValue;
            
            _logger?.LogInformation("符号缓存已清空");
        }

        public CacheStatistics GetStatistics()
        {
            return new CacheStatistics
            {
                TotalSymbols = _allSymbols.Count,
                ProtoSymbols = _protoSymbols.Count,
                LastUpdated = _lastInitialized,
                InitializationTime = TimeSpan.Zero, // TODO: 记录初始化时间
                MemoryUsageBytes = EstimateMemoryUsage()
            };
        }

        private long EstimateMemoryUsage()
        {
            return (_allSymbols.Count + _protoSymbols.Count) * 1024;
        }

        /// <summary>
        /// 检查符号是否匹配命名空间过滤器
        /// </summary>
        private bool MatchesNamespaceFilter(ISymbol symbol)
        {
            if (!_namespacePrefixes.Any()) return true; // 没有过滤器，接受所有符号
            
            var ns = symbol.ContainingNamespace?.ToDisplayString() ?? "";
            return _namespacePrefixes.Any(prefix => ns.StartsWith(prefix));
        }

    }
}
