using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace NotchZero
{
    /// <summary>
    /// Now playing, from Windows' own media controls (the same ones the volume flyout uses),
    /// so it works with Spotify, Apple Music, YouTube in Chrome or Edge, and most other players.
    /// </summary>
    internal sealed class MediaService
    {
        private readonly Dispatcher _ui;
        private readonly DispatcherTimer _poll;
        private GlobalSystemMediaTransportControlsSessionManager _manager;
        private GlobalSystemMediaTransportControlsSession _session;
        private int _propsVersion;
        private int _resumeToken;

        private TimeSpan _position;
        private DateTime _positionStampUtc = DateTime.UtcNow;
        private TimeSpan _startTime;

        public event Action Changed;
        public event Action TrackChanged;

        public bool HasSession => _session != null;
        public bool IsPlaying { get; private set; }
        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public string Album { get; private set; } = "";
        public string SourceName { get; private set; } = "";
        public ImageSource Artwork { get; private set; }
        public TimeSpan Duration { get; private set; }
        public string TrackId { get; private set; } = "";

        public MediaService()
        {
            _ui = Dispatcher.CurrentDispatcher;
            _poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _poll.Tick += (s, e) => PickSession();
        }

        public async void Start()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += (s, e) => Post(PickSession);
                _manager.SessionsChanged += (s, e) => Post(PickSession);
                PickSession();
                _poll.Start();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Media manager");
            }
        }

        /// <summary>Poll a little faster while the panel is open.</summary>
        public void SetActive(bool active)
        {
            _poll.Interval = TimeSpan.FromSeconds(active ? 1.5 : 3);
            if (active) RefreshTimeline();
        }

        private void Post(Action action)
        {
            if (_ui.CheckAccess()) action();
            else _ui.InvokeAsync(action);
        }

        // MARK: Sessions

        private static bool IsPlayingSession(GlobalSystemMediaTransportControlsSession s)
        {
            try
            {
                return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch
            {
                return false;
            }
        }

        private void PickSession()
        {
            if (_manager == null) return;
            GlobalSystemMediaTransportControlsSession pick = null;
            try
            {
                var current = _manager.GetCurrentSession();
                var sessions = _manager.GetSessions();
                if (current != null && IsPlayingSession(current)) pick = current;
                else pick = sessions.FirstOrDefault(IsPlayingSession) ?? current ?? sessions.FirstOrDefault();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Media sessions");
            }

            if (!ReferenceEquals(pick, _session))
            {
                Attach(pick);
                _ = RefreshPropertiesAsync();
            }
            RefreshPlayback();
            RefreshTimeline();
        }

        private void Attach(GlobalSystemMediaTransportControlsSession session)
        {
            if (_session != null)
            {
                try
                {
                    _session.MediaPropertiesChanged -= OnMediaProperties;
                    _session.PlaybackInfoChanged -= OnPlaybackInfo;
                    _session.TimelinePropertiesChanged -= OnTimeline;
                }
                catch
                {
                    // The old session may already be gone.
                }
            }

            _session = session;
            if (session != null)
            {
                try
                {
                    session.MediaPropertiesChanged += OnMediaProperties;
                    session.PlaybackInfoChanged += OnPlaybackInfo;
                    session.TimelinePropertiesChanged += OnTimeline;
                    SourceName = FriendlyName(session.SourceAppUserModelId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Attach media session");
                }
            }
            else
            {
                SourceName = "";
                Title = Artist = Album = "";
                Artwork = null;
                IsPlaying = false;
                Duration = TimeSpan.Zero;
                _position = TimeSpan.Zero;
                TrackId = "";
                Changed?.Invoke();
            }
        }

        private void OnMediaProperties(GlobalSystemMediaTransportControlsSession s, MediaPropertiesChangedEventArgs e) =>
            Post(() => { _ = RefreshPropertiesAsync(); });

        private void OnPlaybackInfo(GlobalSystemMediaTransportControlsSession s, PlaybackInfoChangedEventArgs e) =>
            Post(RefreshPlayback);

        private void OnTimeline(GlobalSystemMediaTransportControlsSession s, TimelinePropertiesChangedEventArgs e) =>
            Post(RefreshTimeline);

        // MARK: Refresh

        private void RefreshPlayback()
        {
            var s = _session;
            if (s == null) return;
            bool playing = IsPlayingSession(s);
            if (playing == IsPlaying) return;
            _position = CurrentPosition();
            _positionStampUtc = DateTime.UtcNow;
            IsPlaying = playing;
            Changed?.Invoke();
        }

        private void RefreshTimeline()
        {
            var s = _session;
            if (s == null) return;
            try
            {
                var t = s.GetTimelineProperties();
                if (t == null) return;
                var duration = t.EndTime - t.StartTime;
                if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
                var stamp = t.LastUpdatedTime.UtcDateTime;
                if (stamp.Year < 2000 || stamp > DateTime.UtcNow.AddSeconds(5)) stamp = DateTime.UtcNow;

                bool changed = Math.Abs((duration - Duration).TotalSeconds) > 0.5;
                _startTime = t.StartTime;
                Duration = duration;
                _position = t.Position - t.StartTime;
                _positionStampUtc = stamp;
                if (changed) Changed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Media timeline");
            }
        }

        private async Task RefreshPropertiesAsync()
        {
            var s = _session;
            if (s == null) return;
            int version = ++_propsVersion;
            try
            {
                var p = await s.TryGetMediaPropertiesAsync();
                if (version != _propsVersion || p == null) return;

                var title = p.Title ?? "";
                var artist = string.IsNullOrEmpty(p.Artist) ? (p.AlbumArtist ?? "") : p.Artist;
                var album = p.AlbumTitle ?? "";
                var id = title + "\n" + artist + "\n" + album;
                bool trackChanged = id != TrackId;

                if (trackChanged || Artwork == null)
                {
                    ImageSource art = null;
                    if (p.Thumbnail != null) art = await LoadThumbnailAsync(p.Thumbnail);
                    if (version != _propsVersion) return;
                    Artwork = art;
                }

                Title = title;
                Artist = artist;
                Album = album;
                if (trackChanged)
                {
                    TrackId = id;
                    if (!string.IsNullOrEmpty(title)) TrackChanged?.Invoke();
                }
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Media properties");
            }
        }

        private static async Task<ImageSource> LoadThumbnailAsync(IRandomAccessStreamReference reference)
        {
            try
            {
                using (var stream = await reference.OpenReadAsync())
                {
                    if (stream.Size == 0) return null;
                    uint size = (uint)Math.Min(stream.Size, 16UL * 1024 * 1024);
                    byte[] bytes;
                    using (var reader = new DataReader(stream.GetInputStreamAt(0)))
                    {
                        uint loaded = await reader.LoadAsync(size);
                        bytes = new byte[loaded];
                        reader.ReadBytes(bytes);
                    }
                    if (bytes.Length == 0) return null;

                    var bitmap = new BitmapImage();
                    using (var ms = new MemoryStream(bytes))
                    {
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.DecodePixelWidth = 400;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                    }
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Album art");
                return null;
            }
        }

        // MARK: Position

        public TimeSpan CurrentPosition()
        {
            var p = _position;
            if (IsPlaying) p += DateTime.UtcNow - _positionStampUtc;
            if (p < TimeSpan.Zero) p = TimeSpan.Zero;
            if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
            return p;
        }

        // MARK: Controls

        public async void PlayPause()
        {
            var s = _session;
            if (s == null) return;
            _resumeToken++;
            _position = CurrentPosition();
            _positionStampUtc = DateTime.UtcNow;
            IsPlaying = !IsPlaying; // feels instant; the real state follows
            Changed?.Invoke();
            try { await s.TryTogglePlayPauseAsync(); }
            catch (Exception ex) { Log.Error(ex, "Play/pause"); }
        }

        public async void Next()
        {
            var s = _session;
            if (s == null) return;
            try { await s.TrySkipNextAsync(); }
            catch (Exception ex) { Log.Error(ex, "Next"); }
        }

        public async void Previous()
        {
            var s = _session;
            if (s == null) return;
            try { await s.TrySkipPreviousAsync(); }
            catch (Exception ex) { Log.Error(ex, "Previous"); }
        }

        public async void Seek(double fraction)
        {
            var s = _session;
            if (s == null || Duration <= TimeSpan.Zero) return;
            var target = TimeSpan.FromTicks((long)(Duration.Ticks * Math.Clamp(fraction, 0, 1)));
            _position = target;
            _positionStampUtc = DateTime.UtcNow;
            Changed?.Invoke();
            try { await s.TryChangePlaybackPositionAsync((_startTime + target).Ticks); }
            catch (Exception ex) { Log.Error(ex, "Seek"); }
        }

        /// <summary>Pauses for a few seconds (prayer time), then carries on unless you changed something.</summary>
        public async void PauseBriefly(double seconds)
        {
            var s = _session;
            if (s == null || !IsPlaying) return;
            int token = ++_resumeToken;
            string track = TrackId;
            try { await s.TryPauseAsync(); }
            catch { return; }

            await Task.Delay(TimeSpan.FromSeconds(seconds));
            if (token != _resumeToken || !ReferenceEquals(s, _session) || track != TrackId) return;
            if (!IsPlayingSession(s))
            {
                try { await s.TryPlayAsync(); }
                catch (Exception ex) { Log.Error(ex, "Resume after prayer pause"); }
            }
        }

        public static void OpenSpotify()
        {
            try
            {
                Process.Start(new ProcessStartInfo("spotify:") { UseShellExecute = true });
            }
            catch
            {
                try { Process.Start(new ProcessStartInfo("https://open.spotify.com") { UseShellExecute = true }); }
                catch (Exception ex) { Log.Error(ex, "Open Spotify"); }
            }
        }

        private static string FriendlyName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var l = id.ToLowerInvariant();
            if (l.Contains("spotify")) return "Spotify";
            if (l.Contains("applemusic") || l.Contains("appleinc")) return "Apple Music";
            if (l.Contains("chrome")) return "Chrome";
            if (l.Contains("msedge") || l.Contains("microsoftedge")) return "Edge";
            if (l.Contains("firefox")) return "Firefox";
            if (l.Contains("zunemusic") || l.Contains("mediaplayer")) return "Media Player";
            if (l.Contains("youtube")) return "YouTube";

            var name = id;
            int bang = name.IndexOf('!');
            if (bang > 0) name = name.Substring(0, bang);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            int slash = name.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0 && slash < name.Length - 1) name = name.Substring(slash + 1);
            int underscore = name.IndexOf('_');
            if (underscore > 0) name = name.Substring(0, underscore);
            int dot = name.LastIndexOf('.');
            if (dot >= 0 && dot < name.Length - 1) name = name.Substring(dot + 1);
            return name;
        }
    }
}
