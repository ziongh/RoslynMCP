using Microsoft.CodeAnalysis;
using RoslynMCP.Core.Interfaces;

namespace RoslynMCP.Query.Services
{
    /// <summary>
    /// 基础查询服务接口 - 仅提供基础的Roslyn查询功能
    /// </summary>
    public interface IQueryService
    {
        /// <summary>
        /// 使用预构建的符号缓存初始化查询服务
        /// </summary>
        Task<bool> InitializeAsync(ISymbolCacheService symbolCache, CancellationToken cancellationToken = default);

        /// <summary>
        /// 搜索符号
        /// </summary>
        Task<IEnumerable<SymbolSearchResult>> SearchSymbolsAsync(SymbolSearchRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// 查找引用
        /// </summary>
        Task<IEnumerable<ReferenceLocation>> FindReferencesAsync(string symbolName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取符号详细信息
        /// </summary>
        Task<SymbolDetails?> GetSymbolDetailsAsync(string symbolName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取解决方案中的所有项目
        /// </summary>
        Task<IEnumerable<ProjectInfo>> GetProjectsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取项目中的所有符号
        /// </summary>
        Task<IEnumerable<INamedTypeSymbol>> GetProjectSymbolsAsync(string projectName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取项目的依赖关系
        /// </summary>
        Task<ProjectInfo?> GetProjectDependenciesAsync(string projectName, CancellationToken cancellationToken = default);


        /// <summary>
        /// 检查服务是否已初始化
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 获取当前解决方案路径
        /// </summary>
        string? SolutionPath { get; }

        /// <summary>
        /// 获取解决方案对象（供上层使用）
        /// </summary>
        Solution? Solution { get; }

        /// <summary>
        /// 获取符号的源代码
        /// </summary>
        Task<string?> GetSourceCodeAsync(string symbolName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取文件的源代码
        /// </summary>
        Task<string?> GetFileContentAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取所有符号的只读字典（供图分析器使用）
        /// </summary>
        IReadOnlyDictionary<string, ISymbol>? AllSymbols { get; }

        /// <summary>
        /// 获取Proto符号的只读字典（供图分析器使用）
        /// </summary>
        IReadOnlyDictionary<string, INamedTypeSymbol>? ProtoSymbols { get; }

        /// <summary>
        /// 获取符号缓存服务（供需要直接访问的场景使用）
        /// </summary>
        ISymbolCacheService? SymbolCacheService { get; }

        /// <summary>
        /// 通过名称查找方法符号（可能返回多个重载）
        /// </summary>
        /// <param name="methodName">要搜索的方法名</param>
        /// <param name="projectName">可选。要将搜索范围限定到的项目名称</param>
        /// <param name="cancellationToken">取消令牌</param>
        Task<IEnumerable<IMethodSymbol>> FindMethodSymbolsAsync(string methodName, string? projectName = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// 释放资源
        /// </summary>
        void Dispose();
    }

    /// <summary>
    /// 项目信息
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
    /// 符号详细信息
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
    }

    /// <summary>
    /// 符号搜索结果
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
    /// 符号搜索请求
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
    /// 引用位置
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
