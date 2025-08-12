using System;
using System.Diagnostics;

namespace RoslynMCP.Core.Utils
{
    /// <summary>
    /// 用于测量和报告操作耗时的计时器工具类
    /// </summary>
    public class Timer : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly string _operationName;
        private readonly bool _autoReport;

        public Timer(string operationName, bool autoReport = true)
        {
            _operationName = operationName;
            _autoReport = autoReport;
            _stopwatch = Stopwatch.StartNew();
            
            if (_autoReport)
            {
                Console.WriteLine($"开始执行: {_operationName}");
            }
        }

        /// <summary>
        /// 获取当前已经过的时间
        /// </summary>
        public TimeSpan Elapsed => _stopwatch.Elapsed;

        /// <summary>
        /// 停止计时并报告结果
        /// </summary>
        public void Stop()
        {
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
                if (_autoReport)
                {
                    ReportTime();
                }
            }
        }

        /// <summary>
        /// 手动报告时间（不停止计时器）
        /// </summary>
        public void ReportTime()
        {
            var elapsed = _stopwatch.Elapsed;
            Console.WriteLine($"完成: {_operationName} - 耗时: {FormatElapsed(elapsed)}");
        }

        /// <summary>
        /// 创建一个子计时器，用于测量嵌套操作
        /// </summary>
        public Timer CreateSubTimer(string subOperationName)
        {
            return new Timer($"{_operationName} > {subOperationName}", true);
        }

        /// <summary>
        /// 格式化时间显示
        /// </summary>
        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalMinutes >= 1)
            {
                return $"{elapsed.TotalMinutes:F1} 分钟";
            }
            else if (elapsed.TotalSeconds >= 1)
            {
                return $"{elapsed.TotalSeconds:F2} 秒";
            }
            else
            {
                return $"{elapsed.TotalMilliseconds:F0} 毫秒";
            }
        }

        /// <summary>
        /// IDisposable 实现，支持 using 语句自动计时
        /// </summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// 静态方法，用于快速创建计时器
        /// </summary>
        public static Timer Start(string operationName)
        {
            return new Timer(operationName);
        }
    }
}