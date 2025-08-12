using Microsoft.CodeAnalysis;

namespace RoslynMCP.Core.Interfaces
{
    /// <summary>
    /// Symbol 缓存服务接口
    /// </summary>
    public interface ISymbolCacheService
    {
        /// <summary>
        /// 缓存是否已初始化
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 关联的解决方案
        /// </summary>
        Solution? Solution { get; }

        /// <summary>
        /// 所有符号的字典
        /// </summary>
        IReadOnlyDictionary<string, ISymbol> AllSymbols { get; }

        /// <summary>
        /// Proto相关符号的字典
        /// </summary>
        IReadOnlyDictionary<string, INamedTypeSymbol> ProtoSymbols { get; }

        /// <summary>
        /// 异步初始化缓存
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// 更新指定文件的符号信息
        /// </summary>
        /// <param name="changedFiles">变更的文件路径</param>
        Task UpdateSymbolsAsync(string[] changedFiles);

        /// <summary>
        /// 清除缓存
        /// </summary>
        void ClearCache();

        /// <summary>
        /// 获取缓存统计信息
        /// </summary>
        CacheStatistics GetStatistics();
    }

    /// <summary>
    /// 缓存统计信息
    /// </summary>
    public class CacheStatistics
    {
        public int TotalSymbols { get; set; }
        public int ProtoSymbols { get; set; }
        public DateTime LastUpdated { get; set; }
        public TimeSpan InitializationTime { get; set; }
        public long MemoryUsageBytes { get; set; }
    }
}
