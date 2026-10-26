using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;

// NAudio 3 marks WasapiLoopbackCapture / WasapiOut obsolete in favor of the new builder APIs;
// the classic classes are still fully supported and simpler for this use.
#pragma warning disable CS0618

namespace Screenshot;

/// <summary>
/// Records whatever the computer is playing (WASAPI loopback of the default output device)
/// into a temporary WAV file, which can then be encoded to MP3.
/// </summary>
internal sealed class AudioRecorder : IDisposable
{
    private static readonly Guid KsDataFormatSubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly object _sync = new();
    private readonly Stopwatch _clock = new();
    private WasapiLoopbackCapture? _capture;
    private WasapiOut? _silenceKeeper;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<Exception?>? _stopped;
    private float _peak;

    public string? TempFile { get; private set; }
    public bool IsRecording { get; private set; }
    public TimeSpan Elapsed => _clock.Elapsed;
    public long BytesRecorded { get; private set; }

    /// <summary>Raised (on a background thread) if recording stops by itself, e.g. the output device was unplugged.</summary>
    public event EventHandler<Exception?>? StoppedUnexpectedly;

    /// <summary>Peak level (0..1) since the last call; resets on read.</summary>
    public float ReadPeak()
    {
        float p = _peak;
        _peak = 0;
        return Math.Min(1f, p);
    }

    public void Start()
    {
        if (IsRecording) throw new InvalidOperationException("Already recording.");

        TempFile = Path.Combine(Path.GetTempPath(), $"CaptureEverything_{Guid.NewGuid():N}.wav");
        BytesRecorded = 0;
        _peak = 0;

        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        _capture = new WasapiLoopbackCapture(device);
        _writer = new WaveFileWriter(TempFile, _capture.WaveFormat);
        _stopped = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        // Loopback delivers no data while nothing is playing, which would cut the pauses out
        // of the recording. Playing inaudible silence keeps the stream (and the timeline) running.
        try
        {
            _silenceKeeper = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            _silenceKeeper.Init(new SilenceProvider(_capture.WaveFormat));
            _silenceKeeper.Play();
        }
        catch
        {
            _silenceKeeper?.Dispose();
            _silenceKeeper = null; // recording still works, silent gaps are just skipped
        }

        try
        {
            _capture.StartRecording();
        }
        catch
        {
            Cleanup(deleteFile: true);
            throw;
        }

        IsRecording = true;
        _clock.Restart();
    }

    /// <summary>Stops recording (or finalizes one that stopped by itself) and returns the temp WAV path (null if nothing was captured).</summary>
    public async Task<string?> StopAsync()
    {
        if (_capture is null || _stopped is null) return null;
        if (IsRecording)
        {
            IsRecording = false;
            _clock.Stop();
            _capture.StopRecording();
        }
        await _stopped.Task; // writer is finalized in OnRecordingStopped
        Cleanup(deleteFile: false);
        return BytesRecorded > 0 ? TempFile : null;
    }

    /// <summary>Stops and throws the recording away.</summary>
    public async Task DiscardAsync()
    {
        await StopAsync();
        DeleteTempFile();
    }

    public void DeleteTempFile()
    {
        try { if (TempFile != null && File.Exists(TempFile)) File.Delete(TempFile); } catch { /* best effort */ }
        TempFile = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        var data = e.BufferSpan[..e.BytesRecorded];

        lock (_sync)
        {
            _writer?.Write(data);
            BytesRecorded += data.Length;
        }

        _peak = Math.Max(_peak, MeasurePeak(data, _capture!.WaveFormat));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        bool unexpected = IsRecording;
        IsRecording = false;
        _clock.Stop();

        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
        _silenceKeeper?.Stop();
        _silenceKeeper?.Dispose();
        _silenceKeeper = null;

        _stopped?.TrySetResult(e.Exception);
        if (unexpected) StoppedUnexpectedly?.Invoke(this, e.Exception);
    }

    private static float MeasurePeak(ReadOnlySpan<byte> data, WaveFormat format)
    {
        float peak = 0;
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (format is WaveFormatExtensible ext && ext.SubFormat == KsDataFormatSubtypeIeeeFloat);

        if (isFloat && format.BitsPerSample == 32)
        {
            foreach (float s in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(data))
                peak = Math.Max(peak, Math.Abs(s));
        }
        else if (format.BitsPerSample == 16)
        {
            foreach (short s in System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(data))
                peak = Math.Max(peak, Math.Abs(s / 32768f));
        }
        return peak;
    }

    /// <summary>
    /// Encodes a recorded WAV to MP3 (192 kbps stereo) with the Media Foundation encoder built into Windows.
    /// Throws if no MP3 encoder is available (e.g. Windows "N" editions without the Media Feature Pack).
    /// </summary>
    public static void EncodeMp3(string wavPath, string mp3Path)
    {
        MediaFoundationApi.Startup();
        using var reader = new WaveFileReader(wavPath);
        int rate = reader.WaveFormat.SampleRate is 44100 or 48000 ? reader.WaveFormat.SampleRate : 48000;
        // The MP3 encoder only takes 16-bit mono/stereo PCM; this also downmixes 5.1/7.1 output.
        using var pcm = new MediaFoundationResampler(reader, new WaveFormat(rate, 16, 2)) { ResamplerQuality = 60 };
        MediaFoundationEncoder.EncodeToMp3(pcm, mp3Path, 192_000);
    }

    private void Cleanup(bool deleteFile)
    {
        IsRecording = false;
        _silenceKeeper?.Dispose();
        _silenceKeeper = null;
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
        if (_capture != null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;
        }
        if (deleteFile) DeleteTempFile();
    }

    public void Dispose()
    {
        if (IsRecording)
        {
            try { _capture?.StopRecording(); } catch { /* shutting down */ }
        }
        Cleanup(deleteFile: true);
    }
}
