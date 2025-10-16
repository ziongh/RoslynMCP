using System;
using System.Diagnostics;

namespace RoslynMCP.Core.Utils
{
    /// <summary>
    /// Timer utility class for measuring and reporting operation duration
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
                Console.WriteLine($"Starting execution: {_operationName}");
            }
        }

        /// <summary>
        /// Get the current elapsed time
        /// </summary>
        public TimeSpan Elapsed => _stopwatch.Elapsed;

        /// <summary>
        /// Stop timing and report results
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
        /// Manually report time (without stopping the timer)
        /// </summary>
        public void ReportTime()
        {
            var elapsed = _stopwatch.Elapsed;
            Console.WriteLine($"Completed: {_operationName} - Duration: {FormatElapsed(elapsed)}");
        }

        /// <summary>
        /// Create a sub-timer for measuring nested operations
        /// </summary>
        public Timer CreateSubTimer(string subOperationName)
        {
            return new Timer($"{_operationName} > {subOperationName}", true);
        }

        /// <summary>
        /// Format time display
        /// </summary>
        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalMinutes >= 1)
            {
                return $"{elapsed.TotalMinutes:F1} minutes";
            }
            else if (elapsed.TotalSeconds >= 1)
            {
                return $"{elapsed.TotalSeconds:F2} seconds";
            }
            else
            {
                return $"{elapsed.TotalMilliseconds:F0} milliseconds";
            }
        }

        /// <summary>
        /// IDisposable implementation, supports automatic timing with using statement
        /// </summary>
        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// Static method for quickly creating a timer
        /// </summary>
        public static Timer Start(string operationName)
        {
            return new Timer(operationName);
        }
    }
}