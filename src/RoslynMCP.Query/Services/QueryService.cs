using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynMCP.Core.Interfaces;
using System.Text.RegularExpressions;

namespace RoslynMCP.Query.Services
{
    /// <summary>
    /// Basic query service implementation - provides only Roslyn basic query functionality
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
                    _logger.LogError("Symbol cache service cannot be null");
                    return Task.FromResult(false);
                }

                _logger.LogInformation("Initializing query service with pre-built symbol cache");

                _symbolCache = symbolCache;
                SolutionPath = symbolCache.Solution?.FilePath;
                IsInitialized = true;

                _logger.LogInformation("Query service initialization completed");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Query service initialization failed");
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
                _logger.LogWarning("Project not found: {ProjectName}", projectName);
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
                _logger.LogWarning("Project not found: {ProjectName}", projectName);
                return Task.FromResult<ProjectInfo?>(null);
            }

            var projectInfo = ConvertToProjectInfo(project);
            return Task.FromResult<ProjectInfo?>(projectInfo);
        }

        public async Task<IEnumerable<SymbolSearchResult>> SearchSymbolsAsync(SymbolSearchRequest request, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            _logger.LogDebug("Searching symbols: {Pattern}", request.Pattern);

            var results = new List<SymbolSearchResult>();
            var regexOptions = request.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            var regex = new Regex(WildcardToRegex(request.Pattern), regexOptions);

            IEnumerable<ISymbol> symbolsToSearch = _symbolCache!.AllSymbols.Values;
            
            // Special handling for proto symbol abbreviations
            if (request.SymbolKinds?.Any(k => k.Equals("proto", StringComparison.OrdinalIgnoreCase)) == true)
            {
                symbolsToSearch = _symbolCache!.ProtoSymbols.Values;
            }

            // Filter matching symbols first, then limit count - this ensures expected number of results
            var matchingSymbols = symbolsToSearch
                .Where(symbol => MatchesSearchCriteria(symbol, request, regex));

            foreach (var symbol in matchingSymbols)
            {
                if (cancellationToken.IsCancellationRequested) break;
                results.Add(ConvertToSearchResult(symbol));
            }

            _logger.LogDebug("Found {ResultCount} matching symbols", results.Count);
            return results;
        }

        public async Task<IEnumerable<ReferenceLocation>> FindReferencesAsync(string symbolName, CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            _logger.LogDebug("Finding references: {SymbolName}", symbolName);

            var results = new List<ReferenceLocation>();

            // Find symbol
            var targetSymbol = await FindSymbolAsync(symbolName, cancellationToken);

            if (targetSymbol == null)
            {
                _logger.LogWarning("Symbol not found: {SymbolName}", symbolName);
                return results;
            }

            try
            {
                // Use SymbolFinder to find all references
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
                            
                            // Get line text as context
                            var sourceText = await document.GetTextAsync(cancellationToken);
                            var lineText = sourceText.Lines[lineSpan.StartLinePosition.Line].ToString();

                            // Determine if it's a definition: compare location with symbol's definition location
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

                _logger.LogDebug("Found {ReferenceCount} references", results.Count);
                return results.OrderBy(r => r.DocumentPath).ThenBy(r => r.LineNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while finding references: {SymbolName}", symbolName);
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
                throw new InvalidOperationException("Analysis service not initialized, please call InitializeAsync first");
            }
        }
        
        private bool MatchesSearchCriteria(ISymbol symbol, SymbolSearchRequest request, Regex regex)
        {
            // Name matching - prioritize matching symbol name to avoid false matches with full name
            if (!regex.IsMatch(symbol.Name))
            {
                return false;
            }

            // Namespace filtering
            if (!string.IsNullOrEmpty(request.Namespace) && 
                (symbol.ContainingNamespace == null || !symbol.ContainingNamespace.ToDisplayString().Contains(request.Namespace)))
            {
                return false;
            }
            
            // Symbol type filtering
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
                // Ignore errors
            }
            return "";
        }

        private string WildcardToRegex(string pattern)
        {
            return "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        }

        private void DisposeInternal()
        {
            // Symbol cache is managed by SolutionStateManager, not released here
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

            // For partial classes or multiple declarations, need to get source code for all parts
            if (syntaxRefs.Length == 1)
            {
                // Single declaration case
                var syntaxNode = await syntaxRefs[0].GetSyntaxAsync(cancellationToken);
                return syntaxNode.ToFullString();
            }
            else
            {
                // Multiple declarations case (such as partial classes)
                var sourceParts = new List<string>();
                var processedFiles = new HashSet<string>();

                foreach (var syntaxRef in syntaxRefs)
                {
                    var syntaxNode = await syntaxRef.GetSyntaxAsync(cancellationToken);
                    var filePath = syntaxRef.SyntaxTree.FilePath;
                    
                    // Add comment for each file
                    if (!string.IsNullOrEmpty(filePath) && processedFiles.Add(filePath))
                    {
                        sourceParts.Add($"// ===== File: {Path.GetFileName(filePath)} =====");
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

            // Only relative paths are supported, absolute paths are not supported
            if (Path.IsPathRooted(filePath))
            {
                _logger.LogError("Only relative paths are supported. Absolute path provided: {FilePath}", filePath);
                return null;
            }

            // Normalize relative path (remove leading ./ or ../ etc.)
            var normalizedPath = filePath.Replace('\\', '/').TrimStart('.', '/');
            
            // Build full path based on solution directory
            var fullPath = Path.Combine(solutionDir, normalizedPath);
            fullPath = Path.GetFullPath(fullPath);

            // Ensure final path is still within solution directory (prevent ../ attacks)
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
