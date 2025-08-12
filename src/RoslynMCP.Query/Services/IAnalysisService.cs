

namespace RoslynMCP.Analysis.Services
{
    /// <summary>
    /// 高级分析服务接口 - 基于基础Query服务提供复杂分析功能
    /// </summary>
    public interface IAnalysisService
    {
        /// <summary>
        /// 初始化分析服务（依赖于已初始化的 QueryService）
        /// </summary>
        Task<bool> InitializeAsync(CancellationToken cancellationToken = default);
        

        /// <summary>
        /// 检查服务是否已初始化
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 获取当前解决方案路径
        /// </summary>
        string? SolutionPath { get; }

        /// <summary>
        /// 释放资源
        /// </summary>
        void Dispose();

        /// <summary>
        /// 获取符号的继承链
        /// </summary>
        Task<InheritanceHierarchy?> GetInheritanceHierarchyAsync(string symbolName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 获取方法体内的调用信息
        /// </summary>
        Task<IEnumerable<MethodInvocation>> GetMethodBodyInvocationsAsync(string methodName, string? projectName = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 方法调用信息
    /// </summary>
    public class MethodInvocation
    {
        public string CalledMethodName { get; set; } = string.Empty;
        public string ContainingType { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int LineNumber { get; set; }
    }


    /// <summary>
    /// 表示继承层次结构的节点
    /// </summary>
    public class HierarchyNode
    {
        public string SymbolName { get; set; } = string.Empty;
        public List<HierarchyNode> Children { get; set; } = new List<HierarchyNode>();
    }

    /// <summary>
    /// 继承链数据模型
    /// </summary>
    public class InheritanceHierarchy
    {
        public HierarchyNode? BaseNode { get; set; }
        public List<HierarchyNode> DerivedNodes { get; set; } = new List<HierarchyNode>();
    }
    
    
}