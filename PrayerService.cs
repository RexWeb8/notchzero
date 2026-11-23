using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using Windows.Devices.Geolocation;

namespace NotchZero
{
    internal enum PrayerName { Fajr, Dhuhr, Asr, Maghrib, Isha }

    internal enum PrayerWindowKind { None, Soon, Now }

    internal static class PrayerNames
    {
        public static readonly PrayerName[] All =
            { PrayerName.Fajr, PrayerName.Dhuhr, PrayerName.Asr, PrayerName.Maghrib, PrayerName.Isha };
    }

    internal sealed class PrayerDay
    {
        /// <summary>The Malaysian calendar date.</summary>
        public DateTime Date { get; set; }
        public DateTimeOffset Fajr { get; set; }
        public DateTimeOffset Dhuhr { get; set; }
        public DateTimeOffset Asr { get; set; }
        public DateTimeOffset Maghrib { get; set; }
        public DateTimeOffset Isha { get; set; }
        public DateTimeOffset? Syuruk { get; set; }

        /// <summary>Sunrise, or a close estimate if the source didn't give one.</summary>
        [JsonIgnore]
        public DateTimeOffset Sunrise => Syuruk ?? Fajr.AddMinutes(72);

        public DateTimeOffset Time(PrayerName name) => name switch
        {
            PrayerName.Fajr => Fajr,
            PrayerName.Dhuhr => Dhuhr,
            PrayerName.Asr => Asr,
            PrayerName.Maghrib => Maghrib,
            _ => Isha,
        };
    }

    internal readonly struct PrayerEvent
    {
        public PrayerEvent(PrayerName name, DateTimeOffset time)
        {
            Name = name;
            Time = time;
        }

        public PrayerName Name { get; }
        public DateTimeOffset Time { get; }
    }

    internal sealed class ZoneInfo
    {
        public string Code { get; set; }
        public string State { get; set; }
        public string District { get; set; }
    }

    internal sealed class PrayerCache
    {
        public string Zone { get; set; }
        public bool Automatic { get; set; }
        public List<PrayerDay> Days { get; set; } = new List<PrayerDay>();
        public List<ZoneInfo> Zones { get; set; } = new List<ZoneInfo>();
    }

    /// <summary>JAKIM prayer times, via api.waktusolat.app, with e-Solat itself as the fallback.</summary>
    internal static class PrayerApi
    {
        /// <summary>Malaysia is UTC+8 all year.</summary>
        public static readonly TimeSpan MyOffset = TimeSpan.FromHours(8);

        public static DateTimeOffset MyNow => DateTimeOffset.UtcNow.ToOffset(MyOffset);

        public static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("NotchZero/1.0 (Windows)");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            return client;
        }

        public static string Short(DateTimeOffset t) => t.ToOffset(MyOffset).ToString("h:mm", En);

        public static string Clock(DateTimeOffset t) => t.ToOffset(MyOffset).ToString("h:mm tt", En);

        /// <summary>`path` is a zone code like "WLY01" or "gps/3.1390/101.6869".</summary>
        public static async Task<(string zone, List<PrayerDay> days)> FetchWaktuSolatAsync(string path, int? year = null, int? month = null)
        {
            var url = "https://api.waktusolat.app/v2/solat/" + path;
            if (year.HasValue && month.HasValue)
                url += string.Format(CultureInfo.InvariantCulture, "?year={0}&month={1}", year.Value, month.Value);

            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string zone = null;
            if (root.TryGetProperty("zone", out var z) && z.ValueKind == JsonValueKind.String) zone = z.GetString();

            var days = new List<PrayerDay>();
            if (root.TryGetProperty("prayers", out var prayers) && prayers.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in prayers.EnumerateArray())
                {
                    if (!TryUnix(d, "fajr", out var fajr) || !TryUnix(d, "dhuhr", out var dhuhr) ||
                        !TryUnix(d, "asr", out var asr) || !TryUnix(d, "maghrib", out var maghrib) ||
                        !TryUnix(d, "isha", out var isha))
                        continue;

                    days.Add(new PrayerDay
                    {
                        Date = fajr.ToOffset(MyOffset).Date,
                        Fajr = fajr,
                        Dhuhr = dhuhr,
                        Asr = asr,
                        Maghrib = maghrib,
                        Isha = isha,
                        Syuruk = TryUnix(d, "syuruk", out var syuruk) ? syuruk : (DateTimeOffset?)null,
                    });
                }
            }
            if (days.Count == 0) throw new InvalidDataException("No prayer times in response");
            return (zone, days);
        }

        private static bool TryUnix(JsonElement e, string name, out DateTimeOffset time)
        {
            time = default;
            if (!e.TryGetProperty(name, out var v)) return false;
            double seconds;
            if (v.ValueKind == JsonValueKind.Number) seconds = v.GetDouble();
            else if (v.ValueKind == JsonValueKind.String &&
                     double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) seconds = parsed;
            else return false;
            if (seconds <= 0) return false;
            time = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)).ToOffset(MyOffset);
            return true;
        }

        /// <summary>Straight from JAKIM e-Solat, used if the API above is down. Current month only.</summary>
        public static async Task<List<PrayerDay>> FetchJakimAsync(string zone)
        {
            var url = "https://www.e-solat.gov.my/index.php?r=esolatApi/TakwimSolat&period=month&zone=" + Uri.EscapeDataString(zone);
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var days = new List<PrayerDay>();
            if (doc.RootElement.TryGetProperty("prayerTime", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in list.EnumerateArray())
                {
                    var ds = Str(d, "date");
                    if (ds == null || !DateTime.TryParseExact(ds, "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                        continue;
                    var fajr = At(Str(d, "fajr"), date);
                    var dhuhr = At(Str(d, "dhuhr"), date);
                    var asr = At(Str(d, "asr"), date);
                    var maghrib = At(Str(d, "maghrib"), date);
                    var isha = At(Str(d, "isha"), date);
                    if (fajr == null || dhuhr == null || asr == null || maghrib == null || isha == null) continue;
                    days.Add(new PrayerDay
                    {
                        Date = date.Date,
                        Fajr = fajr.Value,
                        Dhuhr = dhuhr.Value,
                        Asr = asr.Value,
                        Maghrib = maghrib.Value,
                        Isha = isha.Value,
                        Syuruk = At(Str(d, "syuruk"), date),
                    });
                }
            }
            if (days.Count == 0) throw new InvalidDataException("No prayer times from JAKIM");
            return days;
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static DateTimeOffset? At(string time, DateTime day)
        {
            if (string.IsNullOrEmpty(time)) return null;
            var parts = time.Split(':');
            if (parts.Length < 2 ||
                !int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m)) return null;
            int s = parts.Length > 2 && int.TryParse(parts[2], out int sec) ? sec : 0;
            return new DateTimeOffset(day.Year, day.Month, day.Day, h, m, s, MyOffset);
        }

        public static async Task<List<ZoneInfo>> FetchZonesAsync()
        {
            var json = await Http.GetStringAsync("https://api.waktusolat.app/zones");
            using var doc = JsonDocument.Parse(json);
            var zones = new List<ZoneInfo>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var z in doc.RootElement.EnumerateArray())
                {
                    var code = Str(z, "jakimCode");
                    if (string.IsNullOrEmpty(code)) continue;
                    zones.Add(new ZoneInfo { Code = code, State = Str(z, "negeri") ?? "", District = Str(z, "daerah") ?? "" });
                }
            }
            return zones;
        }

        public static string ZoneName(string code)
        {
            if (string.IsNullOrEmpty(code)) return "";
            switch (code)
            {
                case "WLY01": return "Kuala Lumpur";
                case "WLY02": return "Labuan";
            }
            var states = new Dictionary<string, string>
            {
                ["JHR"] = "Johor", ["KDH"] = "Kedah", ["KTN"] = "Kelantan", ["MLK"] = "Melaka",
                ["NGS"] = "Negeri Sembilan", ["PHG"] = "Pahang", ["PLS"] = "Perlis", ["PNG"] = "Penang",
                ["PRK"] = "Perak", ["SBH"] = "Sabah", ["SGR"] = "Selangor", ["SWK"] = "Sarawak",
                ["TRG"] = "Terengganu", ["WLY"] = "Wilayah Persekutuan",
            };
            return code.Length >= 3 && states.TryGetValue(code.Substring(0, 3), out var name) ? name : code;
        }

        /// <summary>Used only until the full zone list has been downloaded once.</summary>
        public static List<ZoneInfo> FallbackZones()
        {
            var codes = new[]
            {
                "WLY01", "WLY02", "SGR01", "SGR02", "SGR03", "JHR02", "JHR03", "JHR04", "MLK01", "NGS01", "NGS02",
                "PRK02", "PNG01", "KDH01", "PLS01", "PHG02", "TRG01", "KTN01", "SBH07", "SWK08",
            };
            return codes.Select(c => new ZoneInfo { Code = c, State = ZoneName(c), District = "" }).ToList();
        }
    }

    /// <summary>
    /// Today's prayer times for wherever the PC is (JAKIM zone from location, else the chosen zone,
    /// else WLY01), cached so it keeps working offline, plus the "it's time" trigger.
    /// </summary>
    internal sealed class PrayerService
    {
        private readonly AppSettings _settings;
        private readonly Dispatcher _ui;
        private DispatcherTimer _tick;
        private DispatcherTimer _eventTimer;
        private DateTimeOffset? _lastAlerted;
        private DateTimeOffset? _lastRefresh;

        public event Action Changed;
        public event Action<PrayerEvent> PrayerTime;

        public List<PrayerDay> Days { get; private set; } = new List<PrayerDay>();
        public List<ZoneInfo> Zones { get; private set; } = new List<ZoneInfo>();
        public string Zone { get; private set; }
        public bool ZoneIsAutomatic { get; private set; }
        public DateTimeOffset Now { get; private set; } = PrayerApi.MyNow;
        public bool IsLoading { get; private set; }

        private static string CachePath => Path.Combine(Log.Dir, "prayer-cache.json");

        public PrayerService(AppSettings settings)
        {
            _settings = settings;
            _ui = Dispatcher.CurrentDispatcher;
            Zone = string.IsNullOrEmpty(settings.Zone) ? "WLY01" : settings.Zone;
            LoadCache();
        }

        public string ZoneName => PrayerApi.ZoneName(Zone);

        public void Start()
        {
            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _tick.Tick += (s, e) => Tick();
            _tick.Start();
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            Refresh();
            ScheduleNextEvent();
            if (Zones.Count == 0) LoadZones();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            _ui.InvokeAsync(() =>
            {
                Now = PrayerApi.MyNow;
                Refresh();
                ScheduleNextEvent();
                Changed?.Invoke();
            });
        }

        // MARK: Queries

        public PrayerDay Today => DayFor(Now.Date);

        public PrayerDay DayFor(DateTime date) => Days.FirstOrDefault(d => d.Date.Date == date.Date);

        private IEnumerable<PrayerEvent> AllEvents =>
            Days.SelectMany(d => PrayerNames.All.Select(n => new PrayerEvent(n, d.Time(n))));

        public PrayerEvent? NextEvent
        {
            get
            {
                PrayerEvent? best = null;
                foreach (var e in AllEvents)
                    if (e.Time > Now && (best == null || e.Time < best.Value.Time)) best = e;
                if (best != null) return best;

                // Past the end of the data we have: tomorrow's Fajr is about the same time as today's.
                var last = Days.OrderBy(d => d.Date).LastOrDefault();
                if (last != null)
                {
                    var guess = last.Fajr.AddDays(1);
                    if (guess > Now) return new PrayerEvent(PrayerName.Fajr, guess);
                }
                return null;
            }
        }

        public PrayerEvent? CurrentEvent
        {
            get
            {
                PrayerEvent? best = null;
                foreach (var e in AllEvents)
                    if (e.Time <= Now && (best == null || e.Time > best.Value.Time)) best = e;
                return best;
            }
        }

        /// <summary>Soon = up to 10 minutes before; Now = the first 15 minutes after it starts.</summary>
        public (PrayerWindowKind kind, PrayerEvent ev) CurrentWindow
        {
            get
            {
                var current = CurrentEvent;
                if (current.HasValue && (Now - current.Value.Time).TotalMinutes < 15)
                    return (PrayerWindowKind.Now, current.Value);
                var next = NextEvent;
                if (next.HasValue && (next.Value.Time - Now).TotalMinutes <= 10)
                    return (PrayerWindowKind.Soon, next.Value);
                return (PrayerWindowKind.None, default);
            }
        }

        public string CountdownText(PrayerEvent e)
        {
            double seconds = (e.Time - Now).TotalSeconds;
            if (seconds <= 0) return "now";
            int minutes = (int)Math.Ceiling(seconds / 60);
            if (minutes < 60) return $"in {minutes}m";
            return $"in {minutes / 60}h {minutes % 60}m";
        }

        // MARK: Zone choice

        public void SetAutomatic()
        {
            _settings.AutoZone = true;
            _settings.Save();
            Refresh();
        }

        public void SetManualZone(string code)
        {
            _settings.AutoZone = false;
            _settings.Zone = code;
            _settings.Save();
            if (code != Zone)
            {
                Zone = code;
                ZoneIsAutomatic = false;
                Days = new List<PrayerDay>();
                Changed?.Invoke();
            }
            Refresh();
        }

        // MARK: Timing

        private void Tick()
        {
            Now = PrayerApi.MyNow;
            CheckDue();
            if (Today == null || _lastRefresh == null || (DateTimeOffset.UtcNow - _lastRefresh.Value).TotalHours > 6)
                Refresh();
            Changed?.Invoke();
        }

        private void ScheduleNextEvent()
        {
            _eventTimer?.Stop();
            var next = NextEvent;
            if (next == null) return;
            var wait = next.Value.Time - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(0.5);
            if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
            if (wait > TimeSpan.FromHours(1)) wait = TimeSpan.FromHours(1); // re-check at least hourly
            var timer = new DispatcherTimer { Interval = wait };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Now = PrayerApi.MyNow;
                CheckDue();
                ScheduleNextEvent();
                Changed?.Invoke();
            };
            _eventTimer = timer;
            timer.Start();
        }

        /// <summary>Fires PrayerTime for a prayer that started within the last 2 minutes (never twice).</summary>
        private void CheckDue()
        {
            var current = CurrentEvent;
            if (current == null) return;
            if ((Now - current.Value.Time).TotalSeconds >= 120) return;
            if (_lastAlerted == current.Value.Time) return;
            _lastAlerted = current.Value.Time;
            PrayerTime?.Invoke(current.Value);
        }

        // MARK: Loading

        public async void Refresh()
        {
            if (IsLoading) return;
            IsLoading = true;
            Changed?.Invoke();
            try
            {
                (double lat, double lon)? coordinate = null;
                if (_settings.AutoZone) coordinate = await GetLocationAsync();
                await LoadAsync(coordinate);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Prayer refresh");
            }
            finally
            {
                IsLoading = false;
                Changed?.Invoke();
            }
        }

        private static async Task<(double lat, double lon)?> GetLocationAsync()
        {
            try
            {
                var access = await Geolocator.RequestAccessAsync();
                if (access != GeolocationAccessStatus.Allowed) return null;
                var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
                var task = locator.GetGeopositionAsync(TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(10)).AsTask();
                var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(15)));
                if (done != task) return null;
                var position = (await task).Coordinate.Point.Position;
                return (position.Latitude, position.Longitude);
            }
            catch (Exception ex)
            {
                Log.Info("Location unavailable: " + ex.Message);
                return null;
            }
        }

        private async Task LoadAsync((double lat, double lon)? coordinate)
        {
            string newZone = _settings.AutoZone ? Zone : (_settings.Zone ?? "WLY01");
            bool automatic = false;
            var fetched = new List<PrayerDay>();

            if (coordinate.HasValue)
            {
                var path = string.Format(CultureInfo.InvariantCulture, "gps/{0:0.0000}/{1:0.0000}",
                                         coordinate.Value.lat, coordinate.Value.lon);
                try
                {
                    var result = await PrayerApi.FetchWaktuSolatAsync(path);
                    if (!string.IsNullOrEmpty(result.zone)) newZone = result.zone.ToUpperInvariant();
                    fetched = result.days;
                    automatic = true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Prayer times by location");
                }
            }

            if (fetched.Count == 0)
            {
                try
                {
                    fetched = (await PrayerApi.FetchWaktuSolatAsync(newZone)).days;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Prayer times (waktusolat)");
                    try { fetched = await PrayerApi.FetchJakimAsync(newZone); }
                    catch (Exception ex2) { Log.Error(ex2, "Prayer times (JAKIM)"); }
                }
            }
            if (fetched.Count == 0) return; // offline: keep what's cached

            // Next month too (best effort), so the first days of next month work offline.
            var nextMonth = fetched.Max(d => d.Date).AddDays(1);
            try
            {
                var more = await PrayerApi.FetchWaktuSolatAsync(newZone, nextMonth.Year, nextMonth.Month);
                fetched.AddRange(more.days);
            }
            catch
            {
                // Not published yet, or offline.
            }

            var byDay = new Dictionary<DateTime, PrayerDay>();
            foreach (var d in fetched) byDay[d.Date.Date] = d;
            var cutoff = PrayerApi.MyNow.Date.AddDays(-2);

            Days = byDay.Values.Where(d => d.Date >= cutoff).OrderBy(d => d.Date).ToList();
            Zone = newZone;
            ZoneIsAutomatic = automatic;
            if (automatic && _settings.Zone != newZone)
            {
                _settings.Zone = newZone;
                _settings.Save();
            }
            _lastRefresh = DateTimeOffset.UtcNow;
            Now = PrayerApi.MyNow;
            SaveCache();
            ScheduleNextEvent();
            CheckDue();
        }

        private async void LoadZones()
        {
            try
            {
                var zones = await PrayerApi.FetchZonesAsync();
                if (zones.Count > 0)
                {
                    Zones = zones;
                    SaveCache();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Zone list");
            }
        }

        private void LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return;
                var cache = JsonSerializer.Deserialize<PrayerCache>(File.ReadAllText(CachePath));
                if (cache == null) return;
                if (cache.Zones != null) Zones = cache.Zones;
                bool sameZone = _settings.AutoZone || cache.Zone == _settings.Zone;
                if (sameZone && cache.Days != null)
                {
                    Days = cache.Days;
                    if (!string.IsNullOrEmpty(cache.Zone)) Zone = cache.Zone;
                    ZoneIsAutomatic = cache.Automatic;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Prayer cache");
            }
        }

        private void SaveCache()
        {
            try
            {
                Directory.CreateDirectory(Log.Dir);
                var cache = new PrayerCache { Zone = Zone, Automatic = ZoneIsAutomatic, Days = Days, Zones = Zones };
                File.WriteAllText(CachePath, JsonSerializer.Serialize(cache));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Saving prayer cache");
            }
        }
    }
}
