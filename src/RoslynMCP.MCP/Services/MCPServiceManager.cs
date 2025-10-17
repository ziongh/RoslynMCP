using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using RoslynMCP.SymbolCache.Security;
using RoslynMCP.Query.Services;
using RoslynMCP.Analysis.Services;
using RoslynMCP.SymbolCache;
using System.Threading;
using RoslynMCP.MCP.Utils;
using System.Text;

namespace RoslynMCP.MCP.Services
{
    /// <summary>
    /// MCP application-level service manager - coordinates solution state manager and application services
    /// </summary>
    public interface IMCPServiceManager
    {
        /// <summary>
        /// Current solution path
        /// </summary>
        string? CurrentSolutionPath { get; }

        /// <summary>
        /// Whether the solution is loaded and services are initialized
        /// </summary>
        bool IsLoaded { get; }

        /// <summary>
        /// Whether services are initialized
        /// </summary>
        bool IsServicesInitialized { get; }

        /// <summary>
        /// Get symbol cache service
        /// </summary>
        Core.Interfaces.ISymbolCacheService? SymbolCache { get; }

        /// <summary>
        /// Load solution and initialize all related services
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
        
        /// <summary>
        /// Get current solution status, wait for loading to complete if in progress
        /// </summary>
        /// <param name="waitForLoading">Whether to wait for ongoing loading operations</param>
        /// <param name="maxWaitMs">Maximum wait time (milliseconds)</param>
        /// <returns>Status information</returns>
        Task<string> GetStatusAsync(bool waitForLoading = true, int maxWaitMs = 30000);

        /// <summary>
        /// Reload the current solution from disk, refreshing all cached symbols
        /// </summary>
        /// <returns>Reload result message</returns>
        Task<string> ReloadSolutionAsync();

        /// <summary>
        /// Notify server that code files have been modified, triggering incremental cache update
        /// </summary>
        /// <param name="changedFiles">Array of absolute file paths that have been modified</param>
        /// <returns>Update result message</returns>
        Task<string> NotifyCodeChangesAsync(string[] changedFiles);
    }

    /// <summary>
    /// MCP application-level service manager implementation
    /// </summary>
    public class MCPServiceManager : IMCPServiceManager
    {
        private readonly ILogger<MCPServiceManager> _logger;
        private readonly ISolutionStateManager _solutionStateManager;
        private readonly IQueryService _queryService;
        private readonly IAnalysisService _analysisService;
        private readonly AnalyzerOptions _options;
        private bool _isServicesInitialized;
        private readonly SemaphoreSlim _loadLock = new SemaphoreSlim(1, 1);
        private List<string>? _currentNamespacePrefixes;


        public MCPServiceManager(
            ILogger<MCPServiceManager> logger,
            ISolutionStateManager solutionStateManager,
            IQueryService queryService,
            IAnalysisService analysisService,
            IOptions<AnalyzerOptions> options)
        {
            _logger = logger;
            _solutionStateManager = solutionStateManager;
            _queryService = queryService;
            _analysisService = analysisService;
            _options = options.Value;

            // Try to load default solution on startup
            _ = Task.Run(async () => await TryLoadDefaultSolutionAsync());
        }

        public string? CurrentSolutionPath => _solutionStateManager.CurrentSolutionPath;
        public bool IsLoaded => _solutionStateManager.IsLoaded && _isServicesInitialized;
        public bool IsServicesInitialized => _isServicesInitialized;
        public Core.Interfaces.ISymbolCacheService? SymbolCache => _solutionStateManager.SymbolCache;

        public async Task<string> LoadSolutionAsync(string solutionPath, List<string>? namespacePrefixes = null)
        {
            if (!await _loadLock.WaitAsync(0))
            {
                return "⚠️ Another solution loading operation is in progress, please try again later.";
            }

            try
            {
                _logger.LogInformation("MCP service manager starting to load solution: {Path}", solutionPath);

                // Store namespace prefixes for potential reload
                _currentNamespacePrefixes = namespacePrefixes;

                // 1. Use solution state manager to load solution and symbol cache
                var loadResult = await _solutionStateManager.LoadSolutionAsync(solutionPath, namespacePrefixes);
                if (!_solutionStateManager.IsLoaded || _solutionStateManager.SymbolCache == null)
                {
                    _isServicesInitialized = false;
                    return loadResult;
                }

                // 2. Initialize query service
                var queryInitialized = await _queryService.InitializeAsync(_solutionStateManager.SymbolCache);
                if (!queryInitialized)
                {
                    _isServicesInitialized = false;
                    return "❌ Query service initialization failed";
                }

                // 3. Initialize analysis service
                var analysisInitialized = await _analysisService.InitializeAsync();
                if (!analysisInitialized)
                {
                    _isServicesInitialized = false;
                    return "❌ Analysis service initialization failed";
                }

                _isServicesInitialized = true;
                _logger.LogInformation("MCP service manager has completed all service initialization");

                // Enhanced status information
                var enhancedStatus = loadResult + "\n" +
                                   "🔧 Query service: Initialized ✅\n" +
                                   "📊 Analysis service: Initialized ✅\n" +
                                   "💡 All services are ready, analysis tools can be used";
                
                return enhancedStatus;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MCP service manager loading failed: {Path}", solutionPath);
                _isServicesInitialized = false;
                return $"❌ MCP service manager loading failed: {ex.Message}";
            }
            finally
            {
                _loadLock.Release();
            }
        }

        public string GetStatus()
        {
            return GetStatusInternal();
        }
        
        public async Task<string> GetStatusAsync(bool waitForLoading = true, int maxWaitMs = 30000)
        {
            // If loading is in progress and user wants to wait, wait for loading to complete
            if (waitForLoading && _loadLock.CurrentCount == 0)
            {
                    _logger.LogDebug("Detected loading in progress, waiting for completion...");                try
                {
                    // Use CancellationToken to control maximum wait time
                    using var cts = new CancellationTokenSource(maxWaitMs);
                    
                    // Wait to acquire lock (i.e., wait for loading to complete)
                    await _loadLock.WaitAsync(cts.Token);
                    
                    // Immediately release the lock because we just want to wait for loading to complete
                    _loadLock.Release();
                    
                    _logger.LogDebug("Loading completed, returning latest status");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Waiting for loading completion timed out ({MaxWaitMs}ms), returning current status", maxWaitMs);
                    return GetStatusInternal() + "\n\n⚠️ Waiting for loading completion timed out, status may not be the latest.";
                }
            }
            
            return GetStatusInternal();
        }
        
        private string GetStatusInternal()
        {
            var baseStatus = new StringBuilder(_solutionStateManager.GetStatus());
            
            if (_solutionStateManager.IsLoaded)
            {
                baseStatus.AppendLine("\n\n**MCP Service Status**:");
                baseStatus.AppendLine($"🔧 Query service: {(_queryService.IsInitialized ? "Initialized ✅" : "Not initialized ❌")}");
                baseStatus.AppendLine($"📊 Analysis service: {(_analysisService.IsInitialized ? "Initialized ✅" : "Not initialized ❌")}");
                
                if (_isServicesInitialized)
                {
                    baseStatus.AppendLine("💡 All services are ready, analysis tools can be used");

                    // Add the list of loaded projects
                    try
                    {
                        var projects = _queryService.GetProjectsAsync().Result.ToList();
                        if (projects.Any())
                        {
                            baseStatus.AppendLine("\n**Loaded Projects**:");
                            const int maxProjectsToShow = 10;
                            foreach (var project in projects.Take(maxProjectsToShow))
                            {
                                baseStatus.AppendLine($"- {project.Name}");
                            }
                            if (projects.Count > maxProjectsToShow)
                            {
                                baseStatus.AppendLine($"... and {projects.Count - maxProjectsToShow} more projects. Use 'ListProjects' tool to view all.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error occurred when getting project list to display status");
                        baseStatus.AppendLine("\n⚠️ Unable to get the list of loaded projects.");
                    }
                }
                else if (_loadLock.CurrentCount == 0)
                {
                    baseStatus.AppendLine("⏳ Services are initializing...");
                }
                else
                {
                    baseStatus.AppendLine("⚠️ Service initialization failed or not started.");
                }
            }

            return baseStatus.ToString();
        }

        private async Task TryLoadDefaultSolutionAsync()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_options.DefaultSolutionPath))
                {
                    // Parse default namespace prefixes
                    List<string>? namespacePrefixes = null;
                    if (!string.IsNullOrWhiteSpace(_options.DefaultNamespacePrefixes))
                    {
                        namespacePrefixes = _options.DefaultNamespacePrefixes
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(prefix => prefix.Trim())
                            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
                            .ToList();
                        
                        _logger.LogInformation("Using default namespace prefixes: {Prefixes}", string.Join(", ", namespacePrefixes));
                    }

                    var result = await LoadSolutionAsync(_options.DefaultSolutionPath, namespacePrefixes);
                    _logger.LogInformation("Default solution load result: {Result}", result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load default solution");
            }
        }

        public async Task<string> ReloadSolutionAsync()
        {
            if (string.IsNullOrEmpty(CurrentSolutionPath))
            {
                return "❌ No solution is currently loaded. Use LoadSolutionAsync first.";
            }

            _logger.LogInformation("Reloading solution from disk: {Path}", CurrentSolutionPath);
            
            return await LoadSolutionAsync(CurrentSolutionPath, _currentNamespacePrefixes);
        }

        public async Task<string> NotifyCodeChangesAsync(string[] changedFiles)
        {
            if (!IsLoaded || SymbolCache == null)
            {
                return "❌ No solution is currently loaded. Cannot update symbols.";
            }

            if (changedFiles == null || changedFiles.Length == 0)
            {
                return "⚠️ No files specified for update.";
            }

            try
            {
                _logger.LogInformation("Updating symbols for {Count} changed files", changedFiles.Length);
                
                await SymbolCache.UpdateSymbolsAsync(changedFiles);
                
                return $"✅ Successfully updated symbols for {changedFiles.Length} file(s).\n" +
                       $"💡 Cache has been refreshed incrementally.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update symbols for changed files");
                return $"❌ Error updating symbols: {ex.Message}\n" +
                       $"💡 Try using ReloadSolution for a full refresh.";
            }
        }
    }
}
