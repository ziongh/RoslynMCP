using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace RoslynMCP.SymbolCache.Diagnostics
{
    /// <summary>
    /// 诊断日志器，用于性能监控和错误追踪
    /// </summary>
    public class DiagnosticLogger
    {
        private readonly ILogger<DiagnosticLogger> _logger;
        
        public DiagnosticLogger(ILogger<DiagnosticLogger> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// 带性能监控的操作执行
        /// </summary>
        public async Task<T> LoggedExecutionAsync<T>(
            string operationName, 
            Func<Task<T>> operation,
            object? parameters = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var operationId = Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogInformation(
                "开始执行 {OperationName} [{OperationId}] 参数: {Parameters}",
                operationName, operationId, JsonSerializer.Serialize(parameters));
            
            try
            {
                var result = await operation();
                
                _logger.LogInformation(
                    "执行完成 {OperationName} [{OperationId}] 耗时: {ElapsedMs}ms",
                    operationName, operationId, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "执行失败 {OperationName} [{OperationId}] 耗时: {ElapsedMs}ms",
                    operationName, operationId, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// 同步版本的带性能监控的操作执行
        /// </summary>
        public T LoggedExecution<T>(
            string operationName, 
            Func<T> operation,
            object? parameters = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var operationId = Guid.NewGuid().ToString("N")[..8];
            
            _logger.LogInformation(
                "开始执行 {OperationName} [{OperationId}] 参数: {Parameters}",
                operationName, operationId, JsonSerializer.Serialize(parameters));
            
            try
            {
                var result = operation();
                
                _logger.LogInformation(
                    "执行完成 {OperationName} [{OperationId}] 耗时: {ElapsedMs}ms",
                    operationName, operationId, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "执行失败 {OperationName} [{OperationId}] 耗时: {ElapsedMs}ms",
                    operationName, operationId, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// 记录缓存统计信息
        /// </summary>
        public void LogCacheStatistics(string cacheName, int hitCount, int missCount, double hitRatio)
        {
            _logger.LogInformation(
                "缓存统计 {CacheName}: 命中={HitCount}, 未命中={MissCount}, 命中率={HitRatio:P2}",
                cacheName, hitCount, missCount, hitRatio);
        }

        /// <summary>
        /// 记录内存使用情况
        /// </summary>
        public void LogMemoryUsage()
        {
            var process = Process.GetCurrentProcess();
            var workingSet = process.WorkingSet64;
            var privateMemory = process.PrivateMemorySize64;
            
            _logger.LogInformation(
                "内存使用: 工作集={WorkingSetMB}MB, 私有内存={PrivateMemoryMB}MB",
                workingSet / 1024 / 1024, privateMemory / 1024 / 1024);
        }
    }
}