using System;
using System.Windows.Threading;

namespace NotchZero
{
    /// <summary>CPU, memory and battery. Samples quickly while the panel is open, slowly otherwise.</summary>
    internal sealed class SystemService
    {
        private readonly DispatcherTimer _timer;
        private long _prevIdle, _prevKernel, _prevUser;
        private bool _hasPrev;

        public event Action Changed;

        public double Cpu { get; private set; }
        public double MemoryFraction { get; private set; }
        public double MemoryUsedGB { get; private set; }
        public double MemoryTotalGB { get; private set; }
        public bool HasBattery { get; private set; }
        public double Battery { get; private set; }
        public bool IsCharging { get; private set; }
        public bool OnAC { get; private set; }

        public SystemService()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _timer.Tick += (s, e) => Sample();
        }

        public void Start()
        {
            Sample();
            _timer.Start();
        }

        public void SetActive(bool active)
        {
            var interval = TimeSpan.FromSeconds(active ? 1.5 : 20);
            if (_timer.Interval != interval) _timer.Interval = interval;
            if (active) Sample();
        }

        private void Sample()
        {
            try
            {
                if (Native.GetSystemTimes(out long idle, out long kernel, out long user))
                {
                    if (_hasPrev)
                    {
                        long dIdle = idle - _prevIdle;
                        long dTotal = (kernel - _prevKernel) + (user - _prevUser); // kernel time includes idle time
                        if (dTotal > 0) Cpu = Math.Clamp(1.0 - (double)dIdle / dTotal, 0, 1);
                    }
                    _prevIdle = idle;
                    _prevKernel = kernel;
                    _prevUser = user;
                    _hasPrev = true;
                }

                var mem = new Native.MEMORYSTATUSEX();
                if (Native.GlobalMemoryStatusEx(mem) && mem.ullTotalPhys > 0)
                {
                    const double gb = 1024.0 * 1024 * 1024;
                    MemoryTotalGB = mem.ullTotalPhys / gb;
                    MemoryUsedGB = (mem.ullTotalPhys - mem.ullAvailPhys) / gb;
                    MemoryFraction = Math.Clamp(MemoryUsedGB / MemoryTotalGB, 0, 1);
                }

                if (Native.GetSystemPowerStatus(out var power))
                {
                    bool noBattery = (power.BatteryFlag & 128) != 0 || power.BatteryFlag == 255;
                    HasBattery = !noBattery && power.BatteryLifePercent <= 100;
                    Battery = HasBattery ? power.BatteryLifePercent / 100.0 : 1;
                    OnAC = power.ACLineStatus == 1;
                    IsCharging = HasBattery && ((power.BatteryFlag & 8) != 0 || (OnAC && power.BatteryLifePercent < 100));
                }

                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "System sample");
            }
        }
    }
}
