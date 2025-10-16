using Microsoft.Extensions.Logging;
using RoslynMCP.Core.Interfaces;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis;
using RoslynMCP.SymbolCache.Security;

namespace RoslynMCP.SymbolCache
{
    /// <summary>
    /// Solution state manager - manages solution loading and symbol caching
    /// </summary>
    public interface ISolutionStateManager
    {
        /// <summary>
        /// Current solution path
        /// </summary>
        string? CurrentSolutionPath { get; }

        /// <summary>
        /// Whether the solution is loaded
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// Get symbol cache service
        /// </summary>
        ISymbolCacheService? SymbolCache { get; }

        /// <summary>
        /// Load solution and create symbol cache
        /// </summary>
        /// <param name="solutionPath">Solution path</param>
        /// <param name="namespacePrefixes">List of namespace prefixes to include</param>
        /// <returns>Loading result message</returns>
        Task<string> LoadSolutionAsync(string solutionPath, List<string>? namespacePrefixes = null);

        /// <summary>
        /// Get current solution status
        /// </summary>
        /// <returns>Status information</returns>
        string GetStatus();
    }

    /// <summary>
    /// Solution state manager implementation
    /// </summary>
    public class SolutionStateManager : ISolutionStateManager
    {
        private readonly ILogger<SolutionStateManager> _logger;
        private readonly SecurityValidator? _securityValidator;
        private readonly MSBuildWorkspace _workspace;
        private string? _currentSolutionPath;
        private List<string>? _currentNamespacePrefixes;
        private bool _isLoaded;
        private ISymbolCacheService? _symbolCache;

        public SolutionStateManager(
            ILogger<SolutionStateManager> logger,
            SecurityValidator? securityValidator = null,
            MSBuildWorkspace? workspace = null)
        {
            _logger = logger;
            _securityValidator = securityValidator;
            _workspace = workspace ?? MSBuildWorkspace.Create();
        }

        public string? CurrentSolutionPath => _currentSolutionPath;
        public bool IsLoaded => _isLoaded;
        public ISymbolCacheService? SymbolCache => _symbolCache;

        public async Task<string> LoadSolutionAsync(string solutionPath, List<string>? namespacePrefixes = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(solutionPath))
                {
                    return "❌ Solution path cannot be empty";
                }

                // Validate path security (if security validator is provided)
                if (_securityValidator != null && !_securityValidator.ValidateSolutionPath(solutionPath))
                {
                    _logger.LogWarning("Invalid solution path attempted: {Path}", solutionPath);
                    return "❌ Invalid solution path, path must be within allowed directories";
                }

                // Check if file exists
                if (!File.Exists(solutionPath))
                {
                    return $"❌ Solution file does not exist: {solutionPath}";
                }

                // Validate if it's a solution file
                if (!Path.GetExtension(solutionPath).Equals(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    return "❌ File must be a .sln solution file";
                }

                var fullPath = Path.GetFullPath(solutionPath);

                // If it's the same path and same prefixes, return success directly
                if (_currentSolutionPath == fullPath && _isLoaded && 
                    AreNamespacePrefixesEqual(_currentNamespacePrefixes, namespacePrefixes))
                {
                    _logger.LogDebug("Solution already loaded with the same namespace prefixes: {Path}", fullPath);
                    return $"✅ Solution already loaded: {Path.GetFileName(fullPath)}";
                }

                _currentSolutionPath = fullPath;
                _currentNamespacePrefixes = namespacePrefixes?.ToList();
                _isLoaded = false;

                _logger.LogInformation("Loading solution: {Path}, Prefixes: {Prefixes}", 
                    _currentSolutionPath, 
                    _currentNamespacePrefixes?.Any() == true ? string.Join(", ", _currentNamespacePrefixes) : "None");

                // 1. Use singleton MSBuildWorkspace instance to improve performance and resource management
                // Clean up old solution state before opening new solution
                _workspace.CloseSolution();
                var solution = await _workspace.OpenSolutionAsync(_currentSolutionPath);
                
                // 2. Create symbol cache service
                var symbolCacheLogger = _logger as ILogger<SymbolCacheService>;
                _symbolCache = new SymbolCacheService(solution, _currentNamespacePrefixes, symbolCacheLogger);
                await _symbolCache.InitializeAsync();

                _isLoaded = true;
                _logger.LogInformation("Solution loaded and symbol cache initialized successfully: {Path}", _currentSolutionPath);

                var status = $"✅ Solution loaded successfully: {Path.GetFileName(_currentSolutionPath)}\n" +
                             $"📁 Path: {_currentSolutionPath}\n" +
                             $"🏷️ Namespace filters: {(_currentNamespacePrefixes?.Any() == true ? string.Join(", ", _currentNamespacePrefixes) : "None")}\n" +
                             $"💾 Symbol cache: Initialized ✅";
                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load solution: {Path}", solutionPath);
                _isLoaded = false;
                return $"❌ Error occurred while loading solution: {ex.Message}";
            }
        }

        public string GetStatus()
        {
            if (!_isLoaded || string.IsNullOrEmpty(_currentSolutionPath))
            {
                return "📋 **Solution Status**: Not loaded\n" +
                       "💡 Use solution manager to load solution file";
            }

            var fileName = Path.GetFileName(_currentSolutionPath);
            var directory = Path.GetDirectoryName(_currentSolutionPath);

            var status = $"📋 **Solution Status**: Loaded ✅\n" +
                        $"📁 **File Name**: {fileName}\n" +
                        $"📂 **Directory**: {directory}\n" +
                        $"🏷️ **Namespace Filters**: {(_currentNamespacePrefixes?.Any() == true ? string.Join(", ", _currentNamespacePrefixes) : "None")}\n" +
                        $"🔧 **Full Path**: {_currentSolutionPath}\n" +
                        $"💾 **Symbol Cache**: {(_symbolCache?.IsInitialized == true ? "Initialized ✅" : "Not initialized ❌")}";

            return status;
        }

        private static bool AreNamespacePrefixesEqual(List<string>? prefixes1, List<string>? prefixes2)
        {
            if (prefixes1 == null && prefixes2 == null) return true;
            if (prefixes1 == null || prefixes2 == null) return false;
            
            var set1 = new HashSet<string>(prefixes1);
            var set2 = new HashSet<string>(prefixes2);
            return set1.SetEquals(set2);
        }
    }
}
