using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMCP.Core.Interfaces;
using System.Text.RegularExpressions;

namespace RoslynMCP.Query.Services
{
    /// <summary>
    /// 基础查询服务实现 - 仅提供Roslyn基础查询功能
    /// </summary>
    public class QueryService : IQueryService, IDisposable
    {
        private readonly ILogger<QueryService> _logger;
        
        private ISymbolCacheService? _symbolCache;
        private bool _isDisposed;

        public bool IsInitialized { get; private set; }
        public string? SolutionPath { get; private set; }
        public Solution? Solution => _symbolCache?.Solution;
        public IReadOnlyDictionary<string, ISymbol>? AllSymbols => _symbolCache?.AllSymbols;
        public IReadOnlyDictionary<string, INamedTypeSymbol>? ProtoSymbols => _symbolCache?.ProtoSymbols;
        public ISymbolCacheService? SymbolCacheService => _symbolCache;

        public QueryService(ILogger<QueryService> logger)
        {
            _logger = logger;
        }

        public Task<bool> InitializeAsync(ISymbolCacheService symbolCache, CancellationToken cancellationToken = default)
        {
            try
            {
                if (symbolCache == null)
                {
                    _logger.LogError("符号缓存服务不能为空");
                    return Task.FromResult(false);
                }

                _logger.LogInformation("使用预构建的符号缓存初始化查询服务");

                _symbolCache = symbolCache;
                SolutionPath = symbolCache.Solution?.FilePath;
                IsInitialized = true;

                _logger.LogInformation("查询服务初始化完成");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查询服务初始化失败");
                return Task.FromResult(false);
            }
        }

        public async Task<SymbolDetails?> GetSymbolDetailsAsync(string symbolName, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            _logger.LogDebug("Getting symbol details for: {SymbolName}", symbolName);

            var symbol = await FindSymbolAsync(symbolName, cancellationToken);

            if (symbol == null)
            {
                _logger.LogWarning("Could not find symbol for details: {SymbolName}", symbolName);
                return null;
            }

            return ConvertToSymbolDetails(symbol);
        }

        private ProjectInfo ConvertToProjectInfo(Project project)
        {
            return new ProjectInfo
            {
                Name = project.Name,
                FilePath = project.FilePath ?? "",
                Language = project.Language,
                DocumentPaths = project.Documents.Select(d => d.FilePath ?? "").Where(p => !string.IsNullOrEmpty(p)),
                ProjectReferences = project.ProjectReferences.Select(p => _symbolCache!.Solution!.GetProject(p.ProjectId)?.Name ?? "Unknown"),
                PackageReferences = project.MetadataReferences
                    .Select(m => m.Display)
                    .Where(d => !string.IsNullOrEmpty(d))
                    .Select(d => d!)
            };
        }

        public Task<IEnumerable<ProjectInfo>> GetProjectsAsync(CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var projects = _symbolCache!.Solution!.Projects
                .Select(ConvertToProjectInfo)
                .ToList();

            return Task.FromResult<IEnumerable<ProjectInfo>>(projects);
        }

        public Task<IEnumerable<INamedTypeSymbol>> GetProjectSymbolsAsync(string projectName, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var project = _symbolCache!.Solution!.Projects.FirstOrDefault(p => p.Name == projectName);
            if (project == null)
            {
                _logger.LogWarning("未找到项目: {ProjectName}", projectName);
                return Task.FromResult(Enumerable.Empty<INamedTypeSymbol>());
            }

            var symbols = _symbolCache!.AllSymbols.Values
                .Where(s => s.ContainingAssembly?.Name == project.AssemblyName)
                .OfType<INamedTypeSymbol>();
            
            return Task.FromResult(symbols);
        }

        public Task<ProjectInfo?> GetProjectDependenciesAsync(string projectName, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var project = _symbolCache!.Solution!.Projects.FirstOrDefault(p => p.Name == projectName);
            if (project == null)
            {
                _logger.LogWarning("未找到项目: {ProjectName}", projectName);
                return Task.FromResult<ProjectInfo?>(null);
            }

            var projectInfo = ConvertToProjectInfo(project);
            return Task.FromResult<ProjectInfo?>(projectInfo);
        }

        public async Task<IEnumerable<SymbolSearchResult>> SearchSymbolsAsync(SymbolSearchRequest request, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            _logger.LogDebug("搜索符号: {Pattern}", request.Pattern);

            var results = new List<SymbolSearchResult>();
            var regexOptions = request.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            var regex = new Regex(WildcardToRegex(request.Pattern), regexOptions);

            IEnumerable<ISymbol> symbolsToSearch = _symbolCache!.AllSymbols.Values;
            
            // 特殊处理 proto 符号的简写
            if (request.SymbolKinds?.Any(k => k.Equals("proto", StringComparison.OrdinalIgnoreCase)) == true)
            {
                symbolsToSearch = _symbolCache!.ProtoSymbols.Values;
            }

            // 先过滤符合条件的符号，再限制数量 - 这样才能保证返回期望数量的结果
            var matchingSymbols = symbolsToSearch
                .Where(symbol => MatchesSearchCriteria(symbol, request, regex));

            foreach (var symbol in matchingSymbols)
            {
                if (cancellationToken.IsCancellationRequested) break;
                results.Add(ConvertToSearchResult(symbol));
            }

            _logger.LogDebug("找到 {ResultCount} 个匹配的符号", results.Count);
            return results;
        }

        public async Task<IEnumerable<ReferenceLocation>> FindReferencesAsync(string symbolName, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            _logger.LogDebug("查找引用: {SymbolName}", symbolName);

            var results = new List<ReferenceLocation>();

            // 查找符号
            var targetSymbol = await FindSymbolAsync(symbolName, cancellationToken);

            if (targetSymbol == null)
            {
                _logger.LogWarning("未找到符号: {SymbolName}", symbolName);
                return results;
            }

            try
            {
                // 使用 SymbolFinder 查找所有引用
                var referencedSymbols = await SymbolFinder.FindReferencesAsync(
                    targetSymbol, 
                    _symbolCache!.Solution!, 
                    cancellationToken);

                foreach (var referencedSymbol in referencedSymbols)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    foreach (var referenceLocation in referencedSymbol.Locations)
                    {
                        if (referenceLocation.Location.IsInSource)
                        {
                            var location = referenceLocation.Location;
                            var lineSpan = location.GetLineSpan();
                            var document = referenceLocation.Document;
                            
                            // 获取行文本作为上下文
                            var sourceText = await document.GetTextAsync(cancellationToken);
                            var lineText = sourceText.Lines[lineSpan.StartLinePosition.Line].ToString();

                            // 判断是否为定义：比较位置和符号的定义位置
                            var isDefinition = referencedSymbol.Definition.Locations.Any(defLoc => 
                                defLoc.SourceTree == location.SourceTree && 
                                defLoc.SourceSpan == location.SourceSpan);

                            results.Add(new ReferenceLocation
                            {
                                SymbolName = targetSymbol.ToDisplayString(),
                                DocumentPath = document.FilePath ?? "",
                                ProjectName = document.Project.Name,
                                LineNumber = lineSpan.StartLinePosition.Line + 1,
                                ColumnNumber = lineSpan.StartLinePosition.Character + 1,
                                LineText = lineText,
                                Context = lineText.Trim(),
                                IsDefinition = isDefinition,
                                ReferenceKind = isDefinition ? "Definition" : "Reference"
                            });
                        }
                    }
                }

                _logger.LogDebug("找到 {ReferenceCount} 个引用", results.Count);
                return results.OrderBy(r => r.DocumentPath).ThenBy(r => r.LineNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "查找引用时发生错误: {SymbolName}", symbolName);
                return results;
            }
        }
        
        private SymbolDetails ConvertToSymbolDetails(ISymbol symbol)
        {
            var details = new SymbolDetails
            {
                Name = symbol.Name,
                FullName = symbol.ToDisplayString(),
                SymbolKind = GetSymbolKindString(symbol),
                Accessibility = symbol.DeclaredAccessibility.ToString(),
                Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
                AssemblyName = symbol.ContainingAssembly?.Name ?? "",
                Documentation = GetDocumentation(symbol),
                SourceLocation = GetSourceLocation(symbol)
            };

            if (symbol is INamedTypeSymbol namedTypeSymbol)
            {
                details.Members = namedTypeSymbol.GetMembers().Select(m => m.ToDisplayString());
                details.BaseTypes = GetBaseTypes(namedTypeSymbol);
                details.Interfaces = namedTypeSymbol.AllInterfaces.Select(i => i.ToDisplayString());
            }
            else
            {
                // For non-type symbols, members/basetypes/interfaces are not applicable
                details.Members = Enumerable.Empty<string>();
                details.BaseTypes = Enumerable.Empty<string>();
                details.Interfaces = Enumerable.Empty<string>();
            }
            
            return details;
        }

        private IEnumerable<string> GetBaseTypes(INamedTypeSymbol symbol)
        {
            var baseTypes = new List<string>();
            var baseType = symbol.BaseType;
            
            while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
            {
                baseTypes.Add(baseType.ToDisplayString());
                baseType = baseType.BaseType;
            }
            
            return baseTypes;
        }

        private string GetDocumentation(ISymbol symbol)
        {
            var xmlDoc = symbol.GetDocumentationCommentXml();
            return string.IsNullOrEmpty(xmlDoc) ? "" : xmlDoc;
        }

        private string GetSourceLocation(ISymbol symbol)
        {
            var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (location != null)
            {
                var lineSpan = location.GetLineSpan();
                return $"{location.SourceTree?.FilePath}:{lineSpan.StartLinePosition.Line + 1}";
            }
            return "";
        }

        private void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("分析服务未初始化，请先调用 InitializeAsync");
            }
        }
        
        private bool MatchesSearchCriteria(ISymbol symbol, SymbolSearchRequest request, Regex regex)
        {
            // 名称匹配 - 优先匹配符号名称，避免误匹配完整名称
            if (!regex.IsMatch(symbol.Name))
            {
                return false;
            }

            // 命名空间过滤
            if (!string.IsNullOrEmpty(request.Namespace) && 
                (symbol.ContainingNamespace == null || !symbol.ContainingNamespace.ToDisplayString().Contains(request.Namespace)))
            {
                return false;
            }
            
            // 符号类型过滤
            if (request.SymbolKinds?.Any() == true)
            {
                var kindStr = GetSymbolKindString(symbol);
                if (!request.SymbolKinds.Contains(kindStr, StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private SymbolSearchResult ConvertToSearchResult(ISymbol symbol)
        {
            var location = symbol.Locations.FirstOrDefault();
            var lineSpan = location?.GetLineSpan();

            return new SymbolSearchResult
            {
                Name = symbol.Name,
                FullName = symbol.ToDisplayString(),
                Category = GetSymbolKindString(symbol),
                Location = location?.ToString() ?? "",
                ProjectName = symbol.ContainingAssembly?.Name ?? "",
                FilePath = location?.SourceTree?.FilePath ?? "",
                LineNumber = lineSpan?.StartLinePosition.Line + 1 ?? 0,
                Summary = GetSymbolSummary(symbol),
                Accessibility = symbol.DeclaredAccessibility.ToString(),
                SymbolKind = GetSymbolKindString(symbol),
                Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? ""
            };
        }

        private string GetSymbolKindString(ISymbol symbol)
        {
            return symbol.Kind switch
            {
                SymbolKind.NamedType => (symbol as INamedTypeSymbol)?.TypeKind.ToString().ToLower() ?? "type",
                SymbolKind.Method => "method",
                SymbolKind.Property => "property",
                SymbolKind.Field => "field",
                SymbolKind.Event => "event",
                SymbolKind.Parameter => "parameter",
                _ => symbol.Kind.ToString().ToLower()
            };
        }

        private string GetSymbolSummary(ISymbol symbol)
        {
            var parts = new List<string>();
            
            if (symbol.IsAbstract) parts.Add("abstract");
            if (symbol.IsSealed) parts.Add("sealed");
            if (symbol.IsStatic) parts.Add("static");

            if (symbol is INamedTypeSymbol typeSymbol)
            {
                 parts.Add(typeSymbol.TypeKind.ToString().ToLower());
            }
            else
            {
                parts.Add(symbol.Kind.ToString().ToLower());
            }
           
            parts.Add(symbol.Name);

            return string.Join(" ", parts);
        }

        private string GetLineText(Location location)
        {
            try
            {
                var sourceText = location.SourceTree?.GetText();
                if (sourceText != null)
                {
                    var lineSpan = location.GetLineSpan();
                    var line = sourceText.Lines[lineSpan.StartLinePosition.Line];
                    return line.ToString().Trim();
                }
            }
            catch
            {
                // 忽略错误
            }
            return "";
        }

        private string WildcardToRegex(string pattern)
        {
            return "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        }

        private void DisposeInternal()
        {
            // 符号缓存由 SolutionStateManager 管理，这里不释放
            _symbolCache = null;
            IsInitialized = false;
            SolutionPath = null;
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                DisposeInternal();
                _isDisposed = true;
            }
            GC.SuppressFinalize(this);
        }

        private async Task<ISymbol?> FindSymbolAsync(string symbolName, CancellationToken cancellationToken)
        {
            // Fast path for exact matches
            var exactMatch = _symbolCache?.AllSymbols.Values.FirstOrDefault(s => s.ToDisplayString() == symbolName);
            if (exactMatch != null)
            {
                _logger.LogInformation("Found exact symbol match for '{SymbolName}'", symbolName);
                return exactMatch;
            }

            // Robust path using SymbolFinder
            var searchName = symbolName;
            if (symbolName.Contains("."))
            {
                searchName = symbolName.Substring(symbolName.LastIndexOf('.') + 1);
                var genericTickIndex = searchName.IndexOf('<');
                if (genericTickIndex != -1) searchName = searchName.Substring(0, genericTickIndex);
                var parenthesisIndex = searchName.IndexOf('(');
                if (parenthesisIndex != -1) searchName = searchName.Substring(0, parenthesisIndex);
            }

            var allDeclarations = new List<ISymbol>();
            _logger.LogInformation("Searching for declarations with name '{SearchName}'", searchName);
            foreach (var project in _symbolCache!.Solution!.Projects)
            {
                var declarationsInProject = await SymbolFinder.FindDeclarationsAsync(project, searchName, ignoreCase: true, SymbolFilter.All, cancellationToken);
                allDeclarations.AddRange(declarationsInProject);
            }

            if (!allDeclarations.Any())
            {
                _logger.LogWarning("No declarations found for '{SearchName}'", searchName);
                return null;
            }

            if (symbolName.Contains("."))
            {
                var filteredSymbols = allDeclarations
                    .Where(s => s.ToDisplayString().Contains(symbolName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                
                if (filteredSymbols.Any())
                {
                    _logger.LogInformation("Found {Count} potential matches for '{SymbolName}', returning first one.", filteredSymbols.Count, symbolName);
                    return filteredSymbols.FirstOrDefault(); // Return first of the filtered list
                }
            }

            _logger.LogInformation("No specific FQN match found, returning first declaration found for '{SearchName}'", searchName);
            return allDeclarations.FirstOrDefault(); // Return first of the unfiltered list if no qualified match
        }

        public async Task<string?> GetSourceCodeAsync(string symbolName, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            _logger.LogDebug("Getting source code for symbol: {SymbolName}", symbolName);

            var symbol = await FindSymbolAsync(symbolName, cancellationToken);

            if (symbol == null)
            {
                _logger.LogWarning("Could not find symbol: {SymbolName}", symbolName);
                return null;
            }

            var syntaxRefs = symbol.DeclaringSyntaxReferences;
            if (!syntaxRefs.Any())
            {
                _logger.LogWarning("Symbol has no declaring syntax reference: {SymbolName}", symbolName);
                return null;
            }

            // 对于partial类或多个声明，需要获取所有部分的源代码
            if (syntaxRefs.Length == 1)
            {
                // 单个声明的情况
                var syntaxNode = await syntaxRefs[0].GetSyntaxAsync(cancellationToken);
                return syntaxNode.ToFullString();
            }
            else
            {
                // 多个声明的情况（如partial类）
                var sourceParts = new List<string>();
                var processedFiles = new HashSet<string>();

                foreach (var syntaxRef in syntaxRefs)
                {
                    var syntaxNode = await syntaxRef.GetSyntaxAsync(cancellationToken);
                    var filePath = syntaxRef.SyntaxTree.FilePath;
                    
                    // 为每个文件添加注释说明
                    if (!string.IsNullOrEmpty(filePath) && processedFiles.Add(filePath))
                    {
                        sourceParts.Add($"// ===== 文件: {Path.GetFileName(filePath)} =====");
                    }
                    sourceParts.Add(syntaxNode.ToFullString().Trim());
                }

                return string.Join("\n\n", sourceParts);
            }
        }

        public async Task<string?> GetFileContentAsync(string filePath, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            _logger.LogDebug("Getting content for file: {FilePath}", filePath);

            var solutionDir = Path.GetDirectoryName(this.SolutionPath);
            if (solutionDir == null)
            {
                _logger.LogError("Solution directory is null");
                return null;
            }

            // 只支持相对路径，不支持绝对路径
            if (Path.IsPathRooted(filePath))
            {
                _logger.LogError("Only relative paths are supported. Absolute path provided: {FilePath}", filePath);
                return null;
            }

            // 标准化相对路径（移除前导的./或..\等）
            var normalizedPath = filePath.Replace('\\', '/').TrimStart('.', '/');
            
            // 构建基于解决方案目录的完整路径
            var fullPath = Path.Combine(solutionDir, normalizedPath);
            fullPath = Path.GetFullPath(fullPath);

            // 确保最终路径仍在解决方案目录内（防止../ 攻击）
            if (!fullPath.StartsWith(solutionDir, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("Resolved file path is outside the solution directory: {FilePath} -> {FullPath}", filePath, fullPath);
                return null;
            }

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("File does not exist: {FilePath} -> {FullPath}", filePath, fullPath);
                return null;
            }

            return await File.ReadAllTextAsync(fullPath, cancellationToken);
        }

        public async Task<IEnumerable<IMethodSymbol>> FindMethodSymbolsAsync(string methodName, string? projectName = null, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();
            _logger.LogDebug("Finding method symbols for: {MethodName} in project scope: {ProjectName}", methodName, projectName ?? "All");

            if (_symbolCache?.Solution == null)
            {
                _logger.LogWarning("Solution is not loaded, cannot find method symbols.");
                return Enumerable.Empty<IMethodSymbol>();
            }

            try
            {
                // We use FindSymbolAsync and then filter for methods. 
                // This is less efficient than a direct method search but ensures logic consistency.
                var foundSymbol = await FindSymbolAsync(methodName, cancellationToken);
                if(foundSymbol == null)
                {
                    return Enumerable.Empty<IMethodSymbol>();
                }
                
                // If the found symbol is a method, we can assume it's the one we want.
                if (foundSymbol is IMethodSymbol method)
                {
                    return new[] { method };
                }
                
                // If it's a type, we find all methods within that type. This is a common use case.
                if (foundSymbol is INamedTypeSymbol typeSymbol)
                {
                    return typeSymbol.GetMembers().OfType<IMethodSymbol>();
                }

                _logger.LogWarning("FindMethodSymbolsAsync was called with a symbol that is not a method or a type: {SymbolName}", methodName);
                return Enumerable.Empty<IMethodSymbol>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while finding method symbols for '{MethodName}'.", methodName);
                return Enumerable.Empty<IMethodSymbol>();
            }
        }
    }
}
