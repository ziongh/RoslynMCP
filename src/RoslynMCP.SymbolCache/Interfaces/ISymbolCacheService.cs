using Microsoft.CodeAnalysis;

namespace RoslynMCP.Core.Interfaces
{
    /// <summary>
    /// Symbol cache service interface
    /// </summary>
    public interface ISymbolCacheService
    {
        /// <summary>
        /// Whether the cache is initialized
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Associated solution
        /// </summary>
        Solution? Solution { get; }

        /// <summary>
        /// Dictionary of all symbols
        /// </summary>
        IReadOnlyDictionary<string, ISymbol> AllSymbols { get; }

        /// <summary>
        /// Dictionary of Proto-related symbols
        /// </summary>
        IReadOnlyDictionary<string, INamedTypeSymbol> ProtoSymbols { get; }

        /// <summary>
        /// Asynchronously initialize cache
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Update symbol information for specified files
        /// </summary>
        /// <param name="changedFiles">Changed file paths</param>
        Task UpdateSymbolsAsync(string[] changedFiles);

        /// <summary>
        /// Clear cache
        /// </summary>
        void ClearCache();

        /// <summary>
        /// Get cache statistics
        /// </summary>
        CacheStatistics GetStatistics();
    }

    /// <summary>
    /// Cache statistics
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
