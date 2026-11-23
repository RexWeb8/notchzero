using System;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

// Usage: FakePlayer <title> <artist> <cover.png> <seconds>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        var player = new MediaPlayer();
        player.CommandManager.IsEnabled = false;
        var smtc = player.SystemMediaTransportControls;
        smtc.IsEnabled = true;
        smtc.IsPlayEnabled = true;
        smtc.IsPauseEnabled = true;
        smtc.IsNextEnabled = true;
        smtc.IsPreviousEnabled = true;
        smtc.ButtonPressed += (s, e) => Console.WriteLine("Button: " + e.Button);

        var display = smtc.DisplayUpdater;
        display.Type = MediaPlaybackType.Music;
        display.MusicProperties.Title = args[0];
        display.MusicProperties.Artist = args[1];
        display.MusicProperties.AlbumTitle = "Demo";
        var cover = await StorageFile.GetFileFromPathAsync(System.IO.Path.GetFullPath(args[2]));
        display.Thumbnail = RandomAccessStreamReference.CreateFromFile(cover);
        display.Update();

        smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromSeconds(214),
            MinSeekTime = TimeSpan.Zero,
            MaxSeekTime = TimeSpan.FromSeconds(214),
            Position = TimeSpan.FromSeconds(63),
        });
        smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
        Console.WriteLine("Now playing: " + args[0]);
        await Task.Delay(TimeSpan.FromSeconds(int.Parse(args[3])));
    }
}
