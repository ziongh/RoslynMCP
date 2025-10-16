

namespace RoslynMCP.Analysis.Services
{
    /// <summary>
    /// Advanced analysis service interface - provides complex analysis functions based on basic Query service
    /// </summary>
    public interface IAnalysisService
    {
        /// <summary>
        /// Initialize analysis service (depends on initialized QueryService)
        /// </summary>
        Task<bool> InitializeAsync(CancellationToken cancellationToken = default);
        

        /// <summary>
        /// Check if service is initialized
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Get current solution path
        /// </summary>
        string? SolutionPath { get; }

        /// <summary>
        /// Release resources
        /// </summary>
        void Dispose();

        /// <summary>
        /// Get symbol's inheritance chain
        /// </summary>
        Task<InheritanceHierarchy?> GetInheritanceHierarchyAsync(string symbolName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get invocation information within method body
        /// </summary>
        Task<IEnumerable<MethodInvocation>> GetMethodBodyInvocationsAsync(string methodName, string? projectName = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Method invocation information
    /// </summary>
    public class MethodInvocation
    {
        public string CalledMethodName { get; set; } = string.Empty;
        public string ContainingType { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
    }


    /// <summary>
    /// Represents a node in inheritance hierarchy
    /// </summary>
    public class HierarchyNode
    {
        public string SymbolName { get; set; } = string.Empty;
        public List<HierarchyNode> Children { get; set; } = new List<HierarchyNode>();
    }

    /// <summary>
    /// Inheritance chain data model
    /// </summary>
    public class InheritanceHierarchy
    {
        public HierarchyNode? BaseNode { get; set; }
        public List<HierarchyNode> DerivedNodes { get; set; } = new List<HierarchyNode>();
    }
    
    
}