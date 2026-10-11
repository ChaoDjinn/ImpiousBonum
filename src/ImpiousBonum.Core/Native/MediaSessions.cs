using System.Runtime.InteropServices;

namespace ImpiousBonum.Core.Native;

/// <summary>The session's state, as Windows reports it (GlobalSystemMediaTransportControlsSessionPlaybackStatus).</summary>
public enum MediaPlaybackStatus
{
    Closed = 0,
    Opened = 1,
    Changing = 2,
    Stopped = 3,
    Playing = 4,
    Paused = 5,
}

/// <summary>What one read of the current media session found.</summary>
/// <param name="AppId">The app's AppUserModelID, e.g. <c>Spotify.exe</c>.</param>
/// <param name="Position">Where playback was at <paramref name="UpdatedUtc"/>.</param>
/// <param name="Artwork">The album art's image bytes, when <paramref name="ArtworkRead"/>; null if the track has none.</param>
public sealed record MediaSnapshot(
    string AppId, string Title, string Artist, string Album, MediaPlaybackStatus Status,
    TimeSpan Position, TimeSpan Duration, DateTime UpdatedUtc, bool ArtworkRead, byte[]? Artwork)
{
    /// <summary>Identifies the track, to tell when the album art needs reading again.</summary>
    public string Key => $"{AppId}\n{Title}\n{Artist}\n{Album}";
}

/// <summary>
/// Reads the current media session from Windows (the one the volume flyout's media controls show: Spotify, browsers,
/// Media Player…). Talks to the WinRT ABI directly, a handful of vtable calls, rather than taking on the Windows SDK
/// projection, which would add about 25 MB to the app. Interface ids and slot numbers are from windows.media.control.h
/// in the Windows SDK; slots 0–2 are IUnknown and 3–5 IInspectable. Not thread-safe: use one reader from one loop.
/// </summary>
internal sealed unsafe class MediaSessionReader : IDisposable
{
    private const string ManagerClass = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";

    private static readonly Guid ManagerStaticsId = new("2050c4ee-11a0-57de-aed7-c97c70338245");
    private static readonly Guid AsyncInfoId = new("00000036-0000-0000-C000-000000000046");
    private static readonly Guid StreamId = new("0000000c-0000-0000-C000-000000000046");

    /// <summary>Album art bigger than this is skipped rather than held in memory.</summary>
    private const int MaxArtworkBytes = 8 * 1024 * 1024;

    // IGlobalSystemMediaTransportControlsSessionManagerStatics
    private const int RequestAsyncSlot = 6;

    // IGlobalSystemMediaTransportControlsSessionManager
    private const int GetCurrentSessionSlot = 6;

    // IGlobalSystemMediaTransportControlsSession
    private const int SourceAppUserModelIdSlot = 6;
    private const int TryGetMediaPropertiesAsyncSlot = 7;
    private const int GetTimelinePropertiesSlot = 8;
    private const int GetPlaybackInfoSlot = 9;

    // IGlobalSystemMediaTransportControlsSessionMediaProperties
    private const int TitleSlot = 6;
    private const int ArtistSlot = 9;
    private const int AlbumTitleSlot = 10;
    private const int ThumbnailSlot = 15;

    // IGlobalSystemMediaTransportControlsSessionTimelineProperties (TimeSpan and DateTime are 100 ns ticks)
    private const int StartTimeSlot = 6;
    private const int EndTimeSlot = 7;
    private const int PositionSlot = 10;
    private const int LastUpdatedTimeSlot = 11;

    // IGlobalSystemMediaTransportControlsSessionPlaybackInfo
    private const int PlaybackStatusSlot = 7;

    // IRandomAccessStreamReference
    private const int OpenReadAsyncSlot = 6;

    // IAsyncInfo, and IAsyncOperation<T>.GetResults
    private const int AsyncStatusSlot = 7;
    private const int AsyncCancelSlot = 9;
    private const int GetResultsSlot = 8;

    // IStream (classic COM: IUnknown, ISequentialStream.Read)
    private const int StreamReadSlot = 3;

    [ThreadStatic]
    private static bool t_apartment;

    private nint _manager;
    private bool _unavailable;

    /// <summary>
    /// The current session, or null when nothing is playing (or this Windows has no media sessions).
    /// The album art is read only when the track differs from <paramref name="artworkKey"/>.
    /// </summary>
    public MediaSnapshot? Read(string? artworkKey)
    {
        if (!EnsureManager())
            return null;

        if (Get(_manager, GetCurrentSessionSlot, out var session) < 0 || session == 0)
            return null;
        try
        {
            var appId = GetString(session, SourceAppUserModelIdSlot);

            var status = MediaPlaybackStatus.Closed;
            if (Get(session, GetPlaybackInfoSlot, out var playback) >= 0 && playback != 0)
            {
                status = GetInt32(playback, PlaybackStatusSlot, out var value) >= 0 ? (MediaPlaybackStatus)value : MediaPlaybackStatus.Closed;
                Marshal.Release(playback);
            }

            TimeSpan position = default, duration = default;
            var updated = DateTime.UtcNow;
            if (Get(session, GetTimelinePropertiesSlot, out var timeline) >= 0 && timeline != 0)
            {
                var start = GetInt64(timeline, StartTimeSlot);
                duration = TimeSpan.FromTicks(Math.Max(0, GetInt64(timeline, EndTimeSlot) - start));
                position = TimeSpan.FromTicks(Math.Max(0, GetInt64(timeline, PositionSlot) - start));
                var ticks = GetInt64(timeline, LastUpdatedTimeSlot);
                if (ticks > 0)
                    updated = DateTime.FromFileTimeUtc(ticks);
                Marshal.Release(timeline);
            }

            string title = "", artist = "", album = "";
            var artworkRead = false;
            byte[]? artwork = null;
            var properties = Get(session, TryGetMediaPropertiesAsyncSlot, out var operation) >= 0 ? Await(operation, 2000) : 0;
            if (properties != 0)
            {
                try
                {
                    title = GetString(properties, TitleSlot);
                    artist = GetString(properties, ArtistSlot);
                    album = GetString(properties, AlbumTitleSlot);
                    if ($"{appId}\n{title}\n{artist}\n{album}" != artworkKey)
                    {
                        artwork = ReadThumbnail(properties);
                        artworkRead = true;
                    }
                }
                finally
                {
                    Marshal.Release(properties);
                }
            }

            return new MediaSnapshot(appId, title, artist, album, status, position, duration, updated, artworkRead, artwork);
        }
        finally
        {
            Marshal.Release(session);
        }
    }

    public void Dispose()
    {
        if (_manager != 0)
            Marshal.Release(_manager);
        _manager = 0;
    }

    private bool EnsureManager()
    {
        // WinRT needs COM on the calling thread; the sampler's loop moves between thread-pool threads, which are MTA.
        if (!t_apartment)
        {
            RoInitialize(RoInitMultithreaded);
            t_apartment = true;
        }

        if (_manager != 0 || _unavailable)
            return _manager != 0;

        nint className = 0, statics = 0;
        try
        {
            if (WindowsCreateString(ManagerClass, ManagerClass.Length, out className) < 0
                || RoGetActivationFactory(className, in ManagerStaticsId, out statics) < 0
                || Get(statics, RequestAsyncSlot, out var operation) < 0)
            {
                // Windows 10 before 1809 has no media sessions API; don't keep asking.
                _unavailable = true;
                return false;
            }
            _manager = Await(operation, 5000);
            return _manager != 0;
        }
        finally
        {
            if (statics != 0)
                Marshal.Release(statics);
            if (className != 0)
                WindowsDeleteString(className);
        }
    }

    private static byte[]? ReadThumbnail(nint properties)
    {
        if (Get(properties, ThumbnailSlot, out var reference) < 0 || reference == 0)
            return null;
        try
        {
            var randomAccess = Get(reference, OpenReadAsyncSlot, out var operation) >= 0 ? Await(operation, 3000) : 0;
            if (randomAccess == 0)
                return null;
            try
            {
                if (CreateStreamOverRandomAccessStream(randomAccess, in StreamId, out var stream) < 0 || stream == 0)
                    return null;
                try
                {
                    return ReadAll(stream);
                }
                finally
                {
                    Marshal.Release(stream);
                }
            }
            finally
            {
                Marshal.Release(randomAccess);
            }
        }
        finally
        {
            Marshal.Release(reference);
        }
    }

    private static byte[]? ReadAll(nint stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        var read = (delegate* unmanaged[Stdcall]<nint, byte*, uint, uint*, int>)Slot(stream, StreamReadSlot);
        fixed (byte* bytes = chunk)
        {
            while (true)
            {
                uint count;
                var hr = read(stream, bytes, (uint)chunk.Length, &count);
                if (hr < 0)
                    return null;
                buffer.Write(chunk, 0, (int)count);
                if (buffer.Length > MaxArtworkBytes)
                    return null;
                // S_FALSE, or nothing read: the end of the stream.
                if (hr == 1 || count == 0)
                    break;
            }
        }
        return buffer.Length > 0 ? buffer.ToArray() : null;
    }

    /// <summary>
    /// Waits for an IAsyncOperation&lt;T&gt; and returns its result (an owned interface pointer), or 0 if it failed or
    /// took longer than <paramref name="timeoutMs"/>. Releases the operation either way. Polls rather than registering a
    /// completion handler, which would mean implementing a COM interface; these complete in a few milliseconds.
    /// </summary>
    private static nint Await(nint operation, int timeoutMs)
    {
        if (operation == 0)
            return 0;
        try
        {
            if (Marshal.QueryInterface(operation, in AsyncInfoId, out var info) < 0)
                return 0;
            try
            {
                var waited = 0;
                while (true)
                {
                    if (GetInt32(info, AsyncStatusSlot, out var status) < 0)
                        return 0;
                    if (status == AsyncCompleted)
                        break;
                    if (status != AsyncStarted || waited >= timeoutMs)
                    {
                        ((delegate* unmanaged[Stdcall]<nint, int>)Slot(info, AsyncCancelSlot))(info);
                        return 0;
                    }
                    Thread.Sleep(5);
                    waited += 5;
                }
            }
            finally
            {
                Marshal.Release(info);
            }

            return Get(operation, GetResultsSlot, out var result) >= 0 ? result : 0;
        }
        finally
        {
            Marshal.Release(operation);
        }
    }

    private const int AsyncStarted = 0;
    private const int AsyncCompleted = 1;
    private const int RoInitMultithreaded = 1;

    private static void* Slot(nint instance, int slot) => (*(void***)instance)[slot];

    /// <summary>Calls a method whose only parameter is an out interface pointer (or HSTRING).</summary>
    private static int Get(nint instance, int slot, out nint result)
    {
        nint value = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(instance, slot))(instance, &value);
        result = hr >= 0 ? value : 0;
        return hr;
    }

    private static int GetInt32(nint instance, int slot, out int result)
    {
        int value = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(instance, slot))(instance, &value);
        result = value;
        return hr;
    }

    private static long GetInt64(nint instance, int slot)
    {
        long value = 0;
        return ((delegate* unmanaged[Stdcall]<nint, long*, int>)Slot(instance, slot))(instance, &value) >= 0 ? value : 0;
    }

    /// <summary>Calls an HSTRING getter and frees the string; empty if it fails.</summary>
    private static string GetString(nint instance, int slot)
    {
        if (Get(instance, slot, out var hstring) < 0 || hstring == 0)
            return string.Empty;
        try
        {
            var buffer = WindowsGetStringRawBuffer(hstring, out var length);
            return buffer is null ? string.Empty : new string(buffer, 0, (int)length);
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    [DllImport("combase.dll")]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string sourceString, int length, out nint hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll")]
    private static extern char* WindowsGetStringRawBuffer(nint hstring, out uint length);

    [DllImport("shcore.dll")]
    private static extern int CreateStreamOverRandomAccessStream(nint randomAccessStream, in Guid iid, out nint stream);
}
