using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis;
using RoslynMCP.Query.Services;
using RoslynMCP.Core.Interfaces;

namespace RoslynMCP.Analysis.Services
{
    /// <summary>
    /// 高级分析服务实现 - 基于Query服务构建复杂分析功能
    /// </summary>
    public class AnalysisService : IAnalysisService, IDisposable
    {
        private readonly ILogger<AnalysisService> _logger;
        private readonly IQueryService _queryService;
        private bool _isDisposed;

        public bool IsInitialized => _queryService.IsInitialized;
        public string? SolutionPath => _queryService.SolutionPath;

        public AnalysisService(
            ILogger<AnalysisService> logger,
            IQueryService queryService)
        {
            _logger = logger;
            _queryService = queryService;
        }

        public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("初始化分析服务");

                // 检查 QueryService 是否已经初始化
                if (!_queryService.IsInitialized)
                {
                    _logger.LogError("查询服务未初始化，请先初始化 QueryService");
                    return false;
                }

                if (_queryService.SymbolCacheService == null)
                {
                    _logger.LogError("查询服务中的符号缓存服务不可用");
                    return false;
                }

                _logger.LogInformation("分析服务初始化完成");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分析服务初始化失败");
                return false;
            }
        }
        

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("分析服务未初始化，请先调用 InitializeAsync");
            }
        }

        private string GetNodeNamespace(string nodeId)
        {
            var lastDotIndex = nodeId.LastIndexOf('.');
            return lastDotIndex > 0 ? nodeId.Substring(0, lastDotIndex) : "";
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _queryService?.Dispose();
                _isDisposed = true;
            }
            GC.SuppressFinalize(this);
        }

        public async Task<InheritanceHierarchy?> GetInheritanceHierarchyAsync(string symbolName, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            _logger.LogInformation("Getting inheritance hierarchy for: {SymbolName}", symbolName);

            var symbolDetails = await _queryService.GetSymbolDetailsAsync(symbolName, cancellationToken);
            if (symbolDetails == null || _queryService.Solution == null)
            {
                _logger.LogWarning("Symbol not found or solution not loaded: {SymbolName}", symbolName);
                return null;
            }

            var compilationTasks = _queryService.Solution.Projects.Select(p => p.GetCompilationAsync(cancellationToken));
            var compilations = await Task.WhenAll(compilationTasks);
            var symbol = compilations.Select(c => c?.GetTypeByMetadataName(symbolDetails.FullName)).FirstOrDefault(s => s != null);

            if (symbol == null)
            {
                _logger.LogWarning("Could not find INamedTypeSymbol for: {SymbolName}", symbolName);
                return null;
            }

            var hierarchy = new InheritanceHierarchy
            {
                BaseNode = new HierarchyNode { SymbolName = symbol.ToDisplayString() }
            };

            if (symbol.BaseType != null)
            {
                var current = symbol.BaseType;
                var parentNode = hierarchy.BaseNode;
                while (current != null && current.SpecialType != SpecialType.System_Object)
                {
                    var childNode = new HierarchyNode { SymbolName = current.ToDisplayString() };
                    if(parentNode != null)
                    {
                        childNode.Children.Add(parentNode);
                    }
                    parentNode = childNode;
                    current = current.BaseType;
                }
                hierarchy.BaseNode = parentNode;
            }

            var derivedSymbols = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindDerivedClassesAsync(symbol, _queryService.Solution, cancellationToken: cancellationToken);
            foreach(var derived in derivedSymbols)
            {
                hierarchy.DerivedNodes.Add(new HierarchyNode { SymbolName = derived.ToDisplayString() });
            }

            return hierarchy;
        }

        public async Task<IEnumerable<MethodInvocation>> GetMethodBodyInvocationsAsync(string methodName, string? projectName = null, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            _logger.LogInformation("Getting method body invocations for: {MethodName}", methodName);

            var methodSymbols = await _queryService.FindMethodSymbolsAsync(methodName, projectName, cancellationToken);
            var methodSymbol = methodSymbols.FirstOrDefault();

            if (methodSymbol == null)
            {
                _logger.LogWarning("Could not find any method symbol for: {MethodName}", methodName);
                return Enumerable.Empty<MethodInvocation>();
            }

            if (_queryService.Solution == null)
            {
                _logger.LogWarning("Solution is not loaded.");
                return Enumerable.Empty<MethodInvocation>();
            }
            
            // 如果找到多个重载，记录下来
            var symbolList = methodSymbols.ToList();
            if (symbolList.Count > 1)
            {
                _logger.LogInformation("Found {Count} overloads for '{MethodName}'. Analyzing the first one: {FirstSymbol}", 
                    symbolList.Count, methodName, methodSymbol.ToDisplayString());
            }

            var syntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
            {
                _logger.LogWarning("Method symbol '{SymbolName}' has no declaring syntax reference.", methodSymbol.ToDisplayString());
                return Enumerable.Empty<MethodInvocation>();
            }

            var methodNode = await syntaxRef.GetSyntaxAsync(cancellationToken);
            // 通过语法树查找项目，这比通过程序集名称匹配更可靠
            var document = _queryService.Solution.GetDocument(syntaxRef.SyntaxTree);
            if (document == null)
            {
                _logger.LogWarning("Could not find document for syntax tree of method '{SymbolName}'.", methodSymbol.ToDisplayString());
                return Enumerable.Empty<MethodInvocation>();
            }
            var project = document.Project;
            
            var compilation = await project.GetCompilationAsync(cancellationToken);
            var semanticModel = compilation?.GetSemanticModel(syntaxRef.SyntaxTree);

            if (methodNode == null || semanticModel == null)
            {
                _logger.LogWarning("Could not get syntax node or semantic model for '{SymbolName}'.", methodSymbol.ToDisplayString());
                return Enumerable.Empty<MethodInvocation>();
            }

            var invocations = methodNode.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>();
            var results = new List<MethodInvocation>();

            foreach (var invocation in invocations)
            {
                var invokedSymbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
                var invokedSymbol = invokedSymbolInfo.Symbol as IMethodSymbol;
                
                // 也处理候选符号
                if (invokedSymbol == null && invokedSymbolInfo.CandidateSymbols.Any())
                {
                    invokedSymbol = invokedSymbolInfo.CandidateSymbols.FirstOrDefault() as IMethodSymbol;
                }

                if (invokedSymbol != null)
                {
                    var location = invocation.GetLocation().GetLineSpan();
                    results.Add(new MethodInvocation
                    {
                        CalledMethodName = invokedSymbol.ToDisplayString(),
                        ContainingType = invokedSymbol.ContainingType.ToDisplayString(),
                        FilePath = location.Path,
                        LineNumber = location.StartLinePosition.Line + 1
                    });
                }
            }

            return results;
        }
    }
}
