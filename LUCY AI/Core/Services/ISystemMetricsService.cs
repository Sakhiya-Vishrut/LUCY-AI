using System;
using System.Threading;
using System.Threading.Tasks;

namespace LucyAI.Core.Services
{
    /// <summary>
    /// Snapshot of the PC's health at a point in time.
    /// Record = immutable value type — safe to pass across threads without copying.
    /// </summary>
    public record SystemMetrics(
        double CpuUsagePercentage,
        double RamUsagePercentage,
        double AvailableRamMb,
        double TotalRamMb,
        string BatteryStatus,
        int    BatteryPercent,
        bool   IsInternetConnected);

    public interface ISystemMetricsService
    {
        /// <summary>Returns the latest snapshot immediately (synchronous).</summary>
        SystemMetrics GetCurrentMetrics();

        /// <summary>
        /// Hot observable that emits a new SystemMetrics every poll interval.
        /// Subscribe from the UI to drive live dashboard widgets.
        /// </summary>
        IObservable<SystemMetrics> MetricsStream { get; }

        /// <summary>Starts the background polling loop until cancellation.</summary>
        Task StartMonitoringAsync(CancellationToken cancellationToken);
    }
}
