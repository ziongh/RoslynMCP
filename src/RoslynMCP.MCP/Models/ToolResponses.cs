using RoslynMCP.Query.Services;
using RoslynMCP.Analysis.Services;

namespace RoslynMCP.MCP.Models;

/// <summary>
/// Base response for all MCP tools
/// </summary>
public abstract record ToolResponseBase
{
    public bool Success { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Response for GetSolutionStatus tool
/// </summary>
public record SolutionStatusResponse : ToolResponseBase
{
    public bool IsLoaded { get; init; }
    public string? SolutionPath { get; init; }
    public string? SolutionFileName { get; init; }
    public int ProjectCount { get; init; }
    public List<string> ProjectNames { get; init; } = new();
    public bool IsLoading { get; init; }
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// Response for ReloadSolution tool
/// </summary>
public record ReloadSolutionResponse : ToolResponseBase
{
    public string Message { get; init; } = string.Empty;
    public TimeSpan? Duration { get; init; }
}

/// <summary>
/// Response for NotifyCodeChanges tool
/// </summary>
public record NotifyCodeChangesResponse : ToolResponseBase
{
    public string Message { get; init; } = string.Empty;
    public int FilesUpdated { get; init; }
    public List<string> UpdatedFiles { get; init; } = new();
    public TimeSpan? Duration { get; init; }
}

/// <summary>
/// Response for SearchSymbols tool
/// </summary>
public record SearchSymbolsResponse : ToolResponseBase
{
    public string Pattern { get; init; } = string.Empty;
    public string SolutionFileName { get; init; } = string.Empty;
    public string SymbolTypes { get; init; } = string.Empty;
    public bool CaseSensitive { get; init; }
    public bool ExcludeGeneratedFiles { get; init; }
    public bool ExcludeSystemTypes { get; init; }
    public int TotalCount { get; init; }
    public int DisplayedCount { get; init; }
    public bool IsTruncated { get; init; }
    public List<SymbolSearchResult> Results { get; init; } = new();
}

/// <summary>
/// Response for GetSymbolDetails tool
/// </summary>
public record GetSymbolDetailsResponse : ToolResponseBase
{
    public string SymbolName { get; init; } = string.Empty;
    public string SolutionFileName { get; init; } = string.Empty;
    public DateTime AnalysisDate { get; init; }
    public SymbolDetails? BasicInfo { get; init; }
    public string? SourceCode { get; init; }
    public List<ReferenceLocation> References { get; init; } = new();
    public InheritanceHierarchy? InheritanceHierarchy { get; init; }
}

/// <summary>
/// Response for GetSourceCode tool
/// </summary>
public record GetSourceCodeResponse : ToolResponseBase
{
    public string SymbolName { get; init; } = string.Empty;
    public string? SourceCode { get; init; }
    public bool IsFromMetadata { get; init; }
}

/// <summary>
/// Response for GetFileContent tool
/// </summary>
public record GetFileContentResponse : ToolResponseBase
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
    public string? Content { get; init; }
    public int LineCount { get; init; }
}

/// <summary>
/// Response for FindReferences tool
/// </summary>
public record FindReferencesResponse : ToolResponseBase
{
    public string SymbolName { get; init; } = string.Empty;
    public string SolutionFileName { get; init; } = string.Empty;
    public bool IncludeDefinition { get; init; }
    public bool ExcludeGeneratedFiles { get; init; }
    public int TotalCount { get; init; }
    public int DisplayedCount { get; init; }
    public bool IsTruncated { get; init; }
    public List<ReferenceLocation> References { get; init; } = new();
    public Dictionary<string, List<ReferenceLocation>> GroupedByFile { get; init; } = new();
}

/// <summary>
/// Response for GetInheritanceHierarchy tool
/// </summary>
public record GetInheritanceHierarchyResponse : ToolResponseBase
{
    public string SymbolName { get; init; } = string.Empty;
    public InheritanceHierarchy? Hierarchy { get; init; }
}

/// <summary>
/// Response for GetMethodBodyInvocations tool
/// </summary>
public record GetMethodBodyInvocationsResponse : ToolResponseBase
{
    public string MethodName { get; init; } = string.Empty;
    public string? ProjectName { get; init; }
    public int TotalCount { get; init; }
    public int DisplayedCount { get; init; }
    public bool IsTruncated { get; init; }
    public List<MethodInvocation> Invocations { get; init; } = new();
    public Dictionary<string, List<MethodInvocation>> GroupedByType { get; init; } = new();
}

/// <summary>
/// Response for ListProjects tool
/// </summary>
public record ListProjectsResponse : ToolResponseBase
{
    public string SolutionFileName { get; init; } = string.Empty;
    public int TotalProjectCount { get; init; }
    public int DisplayedProjectCount { get; init; }
    public bool IsTruncated { get; init; }
    public List<ProjectInfo> Projects { get; init; } = new();
}

/// <summary>
/// Response for GetProjectDependencies tool
/// </summary>
public record GetProjectDependenciesResponse : ToolResponseBase
{
    public string ProjectName { get; init; } = string.Empty;
    public ProjectInfo? Project { get; init; }
}
