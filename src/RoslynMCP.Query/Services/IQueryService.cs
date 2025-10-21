using Microsoft.CodeAnalysis;
using RoslynMCP.Core.Interfaces;

namespace RoslynMCP.Query.Services
{
    /// <summary>
    /// Basic query service interface - provides only basic Roslyn query functionality
    /// </summary>
    public interface IQueryService
    {
        /// <summary>
        /// Initialize query service with pre-built symbol cache
        /// </summary>
        Task<bool> InitializeAsync(ISymbolCacheService symbolCache, CancellationToken cancellationToken = default);

        /// <summary>
        /// Search symbols
        /// </summary>
        Task<IEnumerable<SymbolSearchResult>> SearchSymbolsAsync(SymbolSearchRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Find references
        /// </summary>
        Task<IEnumerable<ReferenceLocation>> FindReferencesAsync(string symbolName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get symbol details
        /// </summary>
        Task<SymbolDetails?> GetSymbolDetailsAsync(string symbolName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get all projects in the solution
        /// </summary>
        Task<IEnumerable<ProjectInfo>> GetProjectsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get all symbols in a project
        /// </summary>
        Task<IEnumerable<INamedTypeSymbol>> GetProjectSymbolsAsync(string projectName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get project dependencies
        /// </summary>
        Task<ProjectInfo?> GetProjectDependenciesAsync(string projectName, CancellationToken cancellationToken = default);


        /// <summary>
        /// Check if service is initialized
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Get current solution path
        /// </summary>
        string? SolutionPath { get; }

        /// <summary>
        /// Get solution object (for upper layer use)
        /// </summary>
        Solution? Solution { get; }

        /// <summary>
        /// Get source code of symbol
        /// </summary>
        Task<string?> GetSourceCodeAsync(string symbolName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get source code of symbol, with decompilation fallback for metadata symbols
        /// </summary>
        Task<string?> GetSourceCodeAsync(string symbolName, bool allowDecompilation, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get file source code
        /// </summary>
        Task<string?> GetFileContentAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get read-only dictionary of all symbols (for graph analyzer use)
        /// </summary>
        IReadOnlyDictionary<string, ISymbol>? AllSymbols { get; }

        /// <summary>
        /// Get read-only dictionary of Proto symbols (for graph analyzer use)
        /// </summary>
        IReadOnlyDictionary<string, INamedTypeSymbol>? ProtoSymbols { get; }

        /// <summary>
        /// Get symbol cache service (for scenarios requiring direct access)
        /// </summary>
        ISymbolCacheService? SymbolCacheService { get; }

        /// <summary>
        /// Find method symbols by name (may return multiple overloads)
        /// </summary>
        /// <param name="methodName">Method name to search</param>
        /// <param name="projectName">Optional. Project name to limit search scope to</param>
        /// <param name="cancellationToken">Cancellation token</param>
        Task<IEnumerable<IMethodSymbol>> FindMethodSymbolsAsync(string methodName, string? projectName = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Release resources
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// Project information
    /// </summary>
    public class ProjectInfo
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public IEnumerable<string> DocumentPaths { get; set; } = Enumerable.Empty<string>();
        public IEnumerable<string> ProjectReferences { get; set; } = Enumerable.Empty<string>();
        public IEnumerable<string> PackageReferences { get; set; } = Enumerable.Empty<string>();
    }

    /// <summary>
    /// Symbol details
    /// </summary>
    public class SymbolDetails
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string SymbolKind { get; set; } = string.Empty;
        public string Accessibility { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string AssemblyName { get; set; } = string.Empty;
        public IEnumerable<string> Members { get; set; } = Enumerable.Empty<string>();
        public IEnumerable<string> BaseTypes { get; set; } = Enumerable.Empty<string>();
        public IEnumerable<string> Interfaces { get; set; } = Enumerable.Empty<string>();
        public string Documentation { get; set; } = string.Empty;
        public string SourceLocation { get; set; } = string.Empty;
        public bool IsFromMetadata { get; set; } = false;
    }

    /// <summary>
    /// Symbol search result
    /// </summary>
    public class SymbolSearchResult
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string Accessibility { get; set; } = string.Empty;
        public string SymbolKind { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
    }

    /// <summary>
    /// Symbol search request
    /// </summary>
    public class SymbolSearchRequest
    {
        public string Pattern { get; set; } = string.Empty;
        public IEnumerable<string>? SymbolKinds { get; set; }
        public string? Namespace { get; set; }
        public bool IncludeReferences { get; set; } = false;
        public int MaxResults { get; set; } = 100;
        public bool CaseSensitive { get; set; } = false;
    }

    /// <summary>
    /// Reference location
    /// </summary>
    public class ReferenceLocation
    {
        public string SymbolName { get; set; } = string.Empty;
        public string DocumentPath { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public int ColumnNumber { get; set; }
        public string LineText { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public bool IsDefinition { get; set; }
        public string ReferenceKind { get; set; } = string.Empty;
    }
}
