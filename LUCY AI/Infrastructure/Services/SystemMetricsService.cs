using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LucyAI.Core.Services;
using Serilog;

namespace LucyAI.Infrastructure.Services
{
    /// <summary>
    /// Reads live CPU, RAM, battery and internet metrics from Windows.
    ///
    /// KEY FIXES vs the old version:
    ///   1. MetricsStream  — was "throw NotImplementedException()".
    ///                       Now a real IObservable backed by SimpleSubject&lt;T&gt;.
    ///   2. TotalRamMb     — was hardcoded 16384 MB.
    ///                       Now read at startup from GlobalMemoryStatusEx (Win32 API).
    ///   3. BatteryStatus  — was always "AC Connected".
    ///                       Now reads actual Windows power/battery state.
    ///   4. BatteryPercent — new field added to SystemMetrics record.
    ///
    /// WHY NO Rx/System.Reactive?
    ///   We keep zero extra NuGet packages in Phase 1. SimpleSubject&lt;T&gt; below
    ///   is a minimal thread-safe observable — enough for the dashboard.
    ///   We can swap it for System.Reactive Subject&lt;T&gt; in a later phase with
    ///   no interface changes.
    /// </summary>
    public class SystemMetricsService : ISystemMetricsService, IDisposable
    {
        // ── Win32 ─────────────────────────────────────────────────────────────
        // GlobalMemoryStatusEx gives us total physical RAM without needing
        // VisualBasic.Devices or any extra assembly reference.

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint  dwLength;
            public uint  dwMemoryLoad;       // % of physical memory in use (0-100)
            public ulong ullTotalPhys;       // total physical RAM in bytes
            public ulong ullAvailPhys;       // available physical RAM in bytes
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // GetSystemPowerStatus gives us battery charge level and AC/DC state.
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte  ACLineStatus;          // 0=offline, 1=online, 255=unknown
            public byte  BatteryFlag;           // 8=no battery, 128=no battery
            public byte  BatteryLifePercent;    // 0-100, 255=unknown
            public byte  SystemStatusFlag;
            public uint  BatteryLifeTime;       // seconds remaining, 0xFFFFFFFF=unknown
            public uint  BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(ref SYSTEM_POWER_STATUS lpSystemPowerStatus);

        // ── Performance Counters ───────────────────────────────────────────────
        private readonly PerformanceCounter? _cpuCounter;
        private readonly PerformanceCounter? _ramCounter;

        // ── RAM total (read once at startup) ──────────────────────────────────
        private readonly double _totalRamMb;

        // ── Observable ────────────────────────────────────────────────────────
        private readonly SimpleSubject<SystemMetrics> _subject = new();

        // ── Polling interval ──────────────────────────────────────────────────
        private const int PollIntervalMs = 2000;

        private bool _disposed;

        // ── Constructor ───────────────────────────────────────────────────────
        public SystemMetricsService()
        {
            // Detect total physical RAM via Win32 (works on any Windows version)
            _totalRamMb = ReadTotalRamMb();

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    _ramCounter = new PerformanceCounter("Memory", "Available MBytes");

                    // First NextValue() call always returns 0 — prime both counters.
                    _cpuCounter.NextValue();
                    _ramCounter.NextValue();
                }
            }
            catch (Exception ex)
            {
                // PerformanceCounters may fail without admin rights on some systems.
                Log.Warning(ex, "PerformanceCounters unavailable — CPU/RAM will be estimated.");
                _cpuCounter = null;
                _ramCounter = null;
            }

            Log.Information("SystemMetricsService ready — total RAM: {TotalRamMb:F0} MB", _totalRamMb);
        }

        // ── ISystemMetricsService ─────────────────────────────────────────────

        /// <summary>
        /// Live observable stream — emits SystemMetrics every 2 s while
        /// StartMonitoringAsync is running. Subscribe from the ViewModel
        /// to get push notifications without polling in the UI layer.
        ///
        /// Usage:
        ///   _metricsService.MetricsStream.Subscribe(m => CpuUsage = m.CpuUsagePercentage);
        /// </summary>
        public IObservable<SystemMetrics> MetricsStream => _subject;

        /// <summary>
        /// Blocking snapshot — safe to call at any time, even before
        /// StartMonitoringAsync is running. Used by the ViewModel's timer fallback.
        /// </summary>
        public SystemMetrics GetCurrentMetrics()
        {
            double cpu          = ReadCpuPercent();
            double availRamMb   = ReadAvailableRamMb();
            double usedRamMb    = _totalRamMb - availRamMb;
            double ramPercent   = _totalRamMb > 0
                                  ? Math.Round(usedRamMb / _totalRamMb * 100.0, 1)
                                  : 0.0;

            bool   isOnline     = NetworkInterface.GetIsNetworkAvailable();
            var   (batStatus, batPercent) = ReadBatteryStatus();

            return new SystemMetrics(
                CpuUsagePercentage : cpu,
                RamUsagePercentage : ramPercent,
                AvailableRamMb     : Math.Round(availRamMb, 0),
                TotalRamMb         : _totalRamMb,
                BatteryStatus      : batStatus,
                BatteryPercent     : batPercent,
                IsInternetConnected: isOnline);
        }

        /// <summary>
        /// Runs a loop that pushes metrics to subscribers every 2 s.
        /// Call this once from a background Task (e.g. in App.OnStartup).
        /// The loop exits cleanly when cancellationToken is cancelled.
        /// </summary>
        public async Task StartMonitoringAsync(CancellationToken cancellationToken)
        {
            Log.Information("Metrics monitoring loop started.");
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(PollIntervalMs, cancellationToken);

                    var metrics = GetCurrentMetrics();
                    _subject.OnNext(metrics);            // push to all subscribers
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown — not an error
            }
            finally
            {
                _subject.OnCompleted();
                Log.Information("Metrics monitoring loop stopped.");
            }
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        private double ReadCpuPercent()
        {
            try
            {
                if (_cpuCounter != null && OperatingSystem.IsWindows())
                    return Math.Round(_cpuCounter.NextValue(), 1);
            }
            catch { }

            // Fallback: read from GlobalMemoryStatusEx dwMemoryLoad (rough proxy)
            var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref ms))
                return Math.Round(ms.dwMemoryLoad * 0.7, 1); // scale to rough CPU estimate

            return 0.0;
        }

        private double ReadAvailableRamMb()
        {
            try
            {
                if (_ramCounter != null && OperatingSystem.IsWindows())
                    return Math.Round(_ramCounter.NextValue(), 0);
            }
            catch { }

            // Fallback: use Win32 GlobalMemoryStatusEx directly
            var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref ms))
                return Math.Round(ms.ullAvailPhys / (1024.0 * 1024.0), 0);

            return _totalRamMb * 0.5; // last resort guess
        }

        /// <summary>
        /// Read total installed RAM once at startup via Win32 GlobalMemoryStatusEx.
        /// This is more reliable than PerformanceCounters and works without admin rights.
        /// </summary>
        private static double ReadTotalRamMb()
        {
            try
            {
                var ms = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref ms))
                    return Math.Round(ms.ullTotalPhys / (1024.0 * 1024.0), 0);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read total RAM — defaulting to 16 384 MB.");
            }
            return 16384.0; // safe fallback
        }

        /// <summary>
        /// Read battery status via Win32 GetSystemPowerStatus.
        ///
        /// Returns a tuple of (display string, percent int).
        ///
        /// ACLineStatus values:
        ///   0 = on battery,  1 = plugged in (AC),  255 = unknown
        ///
        /// BatteryFlag values:
        ///   1=high, 2=low, 4=critical, 8=charging, 128/255=no battery
        /// </summary>
        private static (string Status, int Percent) ReadBatteryStatus()
        {
            try
            {
                var ps = new SYSTEM_POWER_STATUS();
                if (!GetSystemPowerStatus(ref ps))
                    return ("AC Connected", 100);

                // Desktop PC — no battery installed
                if (ps.BatteryFlag == 128 || ps.BatteryFlag == 255)
                    return ("AC Connected", 100);

                int pct = ps.BatteryLifePercent == 255 ? 100 : ps.BatteryLifePercent;

                string status = ps.ACLineStatus switch
                {
                    1   => $"Charging {pct}%",    // plugged in
                    0   => $"Battery {pct}%",     // on battery
                    _   => "Power Unknown"
                };

                if (ps.BatteryFlag == 4) status = $"Critical {pct}%";
                if (ps.BatteryFlag == 8) status = $"Charging {pct}%";

                return (status, pct);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read battery status.");
                return ("AC Connected", 100);
            }
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_disposed) return;
            _cpuCounter?.Dispose();
            _ramCounter?.Dispose();
            _subject.Dispose();
            _disposed = true;
        }
    }

    // =========================================================================
    // SimpleSubject<T>  — minimal thread-safe IObservable / IObserver
    // =========================================================================
    // Why write our own?
    //   System.Reactive (Rx.NET) is a large package. For Phase 1 we only need
    //   OnNext / OnCompleted / Subscribe — this 60-line class provides exactly
    //   that with no extra dependencies.
    //
    //   Interface: IObservable<T>  (standard .NET BCL — no NuGet needed)
    //   Consumers call:  MetricsStream.Subscribe(m => { ... })
    // =========================================================================
    internal sealed class SimpleSubject<T> : IObservable<T>, IDisposable
    {
        private readonly List<IObserver<T>> _observers = new();
        private readonly object _lock = new();
        private bool _completed;
        private bool _disposed;

        // Called by the producer (monitoring loop) to push a new value
        public void OnNext(T value)
        {
            List<IObserver<T>> snapshot;
            lock (_lock)
            {
                if (_completed || _disposed) return;
                snapshot = new List<IObserver<T>>(_observers);
            }
            foreach (var obs in snapshot)
            {
                try { obs.OnNext(value); }
                catch { /* never let a bad subscriber crash the monitoring loop */ }
            }
        }

        // Called when the monitoring loop exits — signals end-of-stream
        public void OnCompleted()
        {
            List<IObserver<T>> snapshot;
            lock (_lock)
            {
                if (_completed || _disposed) return;
                _completed = true;
                snapshot = new List<IObserver<T>>(_observers);
            }
            foreach (var obs in snapshot)
            {
                try { obs.OnCompleted(); } catch { }
            }
        }

        // IObservable<T> — called by subscribers (e.g. the ViewModel)
        public IDisposable Subscribe(IObserver<T> observer)
        {
            lock (_lock)
            {
                if (!_completed && !_disposed)
                    _observers.Add(observer);
            }
            return new Unsubscriber(_observers, observer, _lock);
        }

        public void Dispose()
        {
            lock (_lock) { _disposed = true; _observers.Clear(); }
        }

        // Returned to the subscriber so they can call .Dispose() to unsubscribe
        private sealed class Unsubscriber : IDisposable
        {
            private readonly List<IObserver<T>> _list;
            private readonly IObserver<T> _obs;
            private readonly object _lock;

            public Unsubscriber(List<IObserver<T>> list, IObserver<T> obs, object lck)
            { _list = list; _obs = obs; _lock = lck; }

            public void Dispose()
            {
                lock (_lock) { _list.Remove(_obs); }
            }
        }
    }
}
