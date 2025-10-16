using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace RoslynMCP.SymbolCache.Diagnostics
{
    /// <summary>
    /// Diagnostic logger for performance monitoring and error tracking
    /// </summary>
    public class DiagnosticLogger
    {
        private readonly ILogger<DiagnosticLogger> _logger;
        
        public DiagnosticLogger(ILogger<DiagnosticLogger> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Operation execution with performance monitoring
        /// </summary>
        public async Task<T> LoggedExecutionAsync<T>(
            string operationName, 
            Func<Task<T>> operation,
            object? parameters = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var operationId = Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogInformation(
                "Starting execution {OperationName} [{OperationId}] Parameters: {Parameters}",
                operationName, operationId, JsonSerializer.Serialize(parameters));
            
            try
            {
                var result = await operation();
                
                _logger.LogInformation(
                    "Execution completed {OperationName} [{OperationId}] Elapsed: {ElapsedMs}ms",
                    operationName, operationId, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Execution failed {OperationName} [{OperationId}] Elapsed: {ElapsedMs}ms",
                    operationName, operationId, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// Synchronous version of operation execution with performance monitoring
        /// </summary>
        public T LoggedExecution<T>(
            string operationName, 
            Func<T> operation,
            object? parameters = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var operationId = Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogInformation(
                "Starting execution {OperationName} [{OperationId}] Parameters: {Parameters}",
                operationName, operationId, JsonSerializer.Serialize(parameters));
            
            try
            {
                var result = operation();
                
                _logger.LogInformation(
                    "Execution completed {OperationName} [{OperationId}] Elapsed: {ElapsedMs}ms",
                    operationName, operationId, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Execution failed {OperationName} [{OperationId}] Elapsed: {ElapsedMs}ms",
                    operationName, operationId, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// Log cache statistics information
        /// </summary>
        public void LogCacheStatistics(string cacheName, int hitCount, int missCount, double hitRatio)
        {
            _logger.LogInformation(
                "Cache statistics {CacheName}: Hits={HitCount}, Misses={MissCount}, Hit ratio={HitRatio:P2}",
                cacheName, hitCount, missCount, hitRatio);
        }

        /// <summary>
        /// Log memory usage information
        /// </summary>
        public void LogMemoryUsage()
        {
            var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;
            var privateMemory = process.PrivateMemorySize64;
            
            _logger.LogInformation(
                "Memory usage: Working set={WorkingSetMB}MB, Private memory={PrivateMemoryMB}MB",
                workingSet / 1024 / 1024, privateMemory / 1024 / 1024);
        }
    }
}