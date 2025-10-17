using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynMCP.Core.Interfaces;
using static RoslynMCP.SymbolCache.RoslynUtils;
using System.Collections.Concurrent;
using Timer = RoslynMCP.Core.Utils.Timer;

namespace RoslynMCP.SymbolCache
{
    /// <summary>
    /// Symbol cache service implementation - streamlined version, focused on incremental updates
    /// </summary>
    public class SymbolCacheService : ISymbolCacheService
    {
        private readonly MSBuildWorkspace _workspace;
        private Solution _solution;
        private readonly ILogger<SymbolCacheService>? _logger;
        private readonly List<string> _namespacePrefixes;
        private readonly ConcurrentDictionary<string, ISymbol> _allSymbols = new();
        private readonly ConcurrentDictionary<string, INamedTypeSymbol> _protoSymbols = new();
        private bool _isInitialized;
        private DateTime _lastInitialized = DateTime.MinValue;

        public SymbolCacheService(MSBuildWorkspace workspace, List<string>? namespacePrefixes = null, ILogger<SymbolCacheService>? logger = null)
        {
            _workspace = workspace;
            _solution = workspace.CurrentSolution;
            _logger = logger;
            _namespacePrefixes = namespacePrefixes ?? new List<string>();
        }

        public bool IsInitialized => _isInitialized;
        public Solution? Solution => _solution;
        public IReadOnlyDictionary<string, ISymbol> AllSymbols => _allSymbols;
        public IReadOnlyDictionary<string, INamedTypeSymbol> ProtoSymbols => _protoSymbols;

        /// <summary>
        /// Full initialization of symbol cache - streamlined version
        /// </summary>
        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            using var timer = Timer.Start("Symbol cache initialization");
            
            await LoadSymbolsFromSolution();
            _isInitialized = true;
            _lastInitialized = DateTime.Now;

            _logger?.LogInformation($"Symbol cache initialization completed: {_allSymbols.Count} symbols, including {_protoSymbols.Count} Proto symbols");
        }

        /// <summary>
        /// Load symbol data from solution
        /// </summary>
        private async Task LoadSymbolsFromSolution()
        {
            _allSymbols.Clear();
            _protoSymbols.Clear();

            var projects = _solution.Projects.ToList();
            _logger?.LogInformation($"Analyzing {projects.Count} projects in parallel...");
            
            var projectTasks = projects.Select(async project =>
            {
                try 
                {
                    var compilation = await project.GetCompilationAsync();
                    return (project, compilation);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, $"Failed to load project {project.Name}");
                    return (project, (Compilation?)null);
                }
            }).ToArray();

            var projectResults = await Task.WhenAll(projectTasks);

            // Analyze all symbols comprehensively
            foreach (var (project, compilation) in projectResults)
            {
                if (compilation == null) continue;

                var projectSymbols = GetAllSymbols(compilation.GlobalNamespace)
                    .Where(MatchesNamespaceFilter); // Filter namespaces first to improve performance

                foreach (var symbol in projectSymbols)
                {
                    var id = symbol.ToDisplayString();
                    // Use TryAdd for thread-safe addition operations
                    _allSymbols.TryAdd(id, symbol);
                }
            }
        }

        /// <summary>
        /// Incrementally update symbols for specified files
        /// </summary>
        public async Task UpdateSymbolsAsync(string[] changedFiles)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Must call InitializeAsync() first for initialization");
            }

            using var timer = Timer.Start("Symbol incremental update");
            
            var changedFileSet = new HashSet<string>(changedFiles, StringComparer.OrdinalIgnoreCase);

            _logger?.LogInformation("Starting incremental update for {Count} files", changedFileSet.Count);

            // Step 1: Update the solution with fresh file contents from disk
            var currentSolution = _workspace.CurrentSolution;
            var newSolution = currentSolution;
            
            foreach (var filePath in changedFiles)
            {
                try
                {
                    // Find the document in the solution
                    var documentIds = currentSolution.GetDocumentIdsWithFilePath(filePath);
                    if (!documentIds.Any())
                    {
                        _logger?.LogWarning("File not found in solution: {FilePath}", filePath);
                        continue;
                    }

                    foreach (var documentId in documentIds)
                    {
                        var document = newSolution.GetDocument(documentId);
                        if (document == null) continue;

                        // Read fresh content from disk
                        if (!File.Exists(filePath))
                        {
                            _logger?.LogWarning("File no longer exists on disk: {FilePath}", filePath);
                            continue;
                        }

                        var newText = SourceText.From(await File.ReadAllTextAsync(filePath));
                        var updatedDocument = document.WithText(newText);
                        newSolution = updatedDocument.Project.Solution;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error updating file in solution: {FilePath}", filePath);
                }
            }

            // Step 2: Apply changes to workspace (makes it "current")
            if (!_workspace.TryApplyChanges(newSolution))
            {
                _logger?.LogError("Failed to apply solution changes to workspace");
                throw new InvalidOperationException("Failed to apply solution changes to workspace");
            }

            // Step 3: Update the stored solution reference
            _solution = _workspace.CurrentSolution;

            // Step 4: Remove old symbols from changed files
            var symbolsToRemove = _allSymbols.Where(kv => 
            {
                var location = kv.Value.Locations.FirstOrDefault()?.SourceTree?.FilePath;
                return location != null && changedFileSet.Contains(location);
            }).ToList();

            _logger?.LogDebug("Removing {Count} old symbols from changed files", symbolsToRemove.Count);

            foreach (var (id, _) in symbolsToRemove)
            {
                _allSymbols.TryRemove(id, out _);
                _protoSymbols.TryRemove(id, out _);
            }

            // Step 5: Re-analyze changed files with fresh compilations
            var affectedProjects = _solution.Projects
                .Where(p => p.Documents.Any(d => d.FilePath != null && changedFileSet.Contains(d.FilePath)))
                .ToList();

            _logger?.LogDebug("Recompiling {Count} affected projects", affectedProjects.Count);

            foreach (var project in affectedProjects)
            {
                try
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null) continue;

                    var projectSymbols = GetAllSymbols(compilation.GlobalNamespace)
                        .Where(symbol => 
                        {
                            var location = symbol.Locations.FirstOrDefault()?.SourceTree?.FilePath;
                            return location != null && changedFileSet.Contains(location);
                        })
                        .Where(MatchesNamespaceFilter)
                        .ToList();

                    _logger?.LogDebug("Found {Count} new symbols in project {ProjectName}", projectSymbols.Count, project.Name);

                    foreach (var symbol in projectSymbols)
                    {
                        var id = symbol.ToDisplayString();
                        _allSymbols[id] = symbol;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error recompiling project: {ProjectName}", project.Name);
                }
            }

            _logger?.LogInformation("Incrementally updated symbols in {Count} files. Total symbols: {TotalSymbols}", 
                changedFileSet.Count, _allSymbols.Count);
        }

        public void ClearCache()
        {
            _allSymbols.Clear();
            _protoSymbols.Clear();
            _isInitialized = false;
            _lastInitialized = DateTime.MinValue;
            
            _logger?.LogInformation("Symbol cache has been cleared");
        }

        public CacheStatistics GetStatistics()
        {
            return new CacheStatistics
            {
                TotalSymbols = _allSymbols.Count,
                ProtoSymbols = _protoSymbols.Count,
                LastUpdated = _lastInitialized,
                InitializationTime = TimeSpan.Zero, // TODO: Record initialization time
                MemoryUsageBytes = EstimateMemoryUsage()
            };
        }

        private long EstimateMemoryUsage()
        {
            return (_allSymbols.Count + _protoSymbols.Count) * 1024;
        }

        /// <summary>
        /// Check if symbol matches namespace filter
        /// </summary>
        private bool MatchesNamespaceFilter(ISymbol symbol)
        {
            if (!_namespacePrefixes.Any()) return true; // No filter, accept all symbols
            
            var ns = symbol.ContainingNamespace?.ToDisplayString() ?? "";
            return _namespacePrefixes.Any(prefix => ns.StartsWith(prefix));
        }

    }
}
