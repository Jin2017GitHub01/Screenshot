using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;
using static Screenshot.NativeMethods;

#pragma warning disable CS0618 // Classic WasapiLoopbackCapture / WasapiOut

namespace Screenshot;

/// <summary>
/// Captures a screen rectangle (via GDI) and system audio (via WASAPI loopback)
/// into an H.264 + AAC MP4 file using the Windows Media Foundation Sink Writer.
/// </summary>
internal sealed class VideoRecorder : IDisposable
{
    private const int Fps = 30;
    private const long FrameDurationHns = 10_000_000L / Fps; // 100-ns units
    private const int MaxVideoWidth = 1920;
    private const int MaxVideoHeight = 1080;
    private const int MinVideoDimension = 64;

    private static readonly Guid KsDataFormatSubtypeIeeeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    // Media Foundation attribute & format GUIDs
    private static readonly Guid MF_MT_MAJOR_TYPE = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MF_MT_SUBTYPE = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MF_MT_AVG_BITRATE = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid MF_MT_INTERLACE_MODE = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MF_MT_FRAME_SIZE = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MF_MT_FRAME_RATE = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MF_MT_DEFAULT_STRIDE = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid MF_MT_AUDIO_NUM_CHANNELS = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid MF_MT_AUDIO_SAMPLES_PER_SECOND = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid MF_MT_AUDIO_AVG_BYTES_PER_SECOND = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    private static readonly Guid MF_MT_AUDIO_BLOCK_ALIGNMENT = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    private static readonly Guid MF_MT_AUDIO_BITS_PER_SAMPLE = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");
    private static readonly Guid MF_MT_ALL_SAMPLES_INDEPENDENT = new("c9173739-5e56-461c-b713-46fb995cb95f");
    private static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");

    private static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFMediaType_Audio = new("73647561-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");
    private static readonly Guid MFAudioFormat_AAC = new("00001610-0000-0010-8000-00aa00389b71");
    private static readonly Guid MFAudioFormat_PCM = new("00000001-0000-0010-8000-00aa00389b71");

    private readonly Stopwatch _clock = new();
    private readonly ConcurrentQueue<byte[]> _audioQueue = new();
    private WasapiLoopbackCapture? _audioCapture;
    private WasapiOut? _silenceKeeper;
    private WaveFormat? _captureFormat;
    private int _pcmSampleRate;
    private volatile bool _stopRequested;
    private Task? _workerTask;
    private float _peak;

    public string? TempFile { get; private set; }
    public bool IsRecording { get; private set; }
    public TimeSpan Elapsed => _clock.Elapsed;
    public long FramesRecorded { get; private set; }
    public Int32Rect Region { get; private set; }

    /// <summary>Raised (on a background thread) if recording fails unexpectedly after starting.</summary>
    public event EventHandler<Exception>? StoppedUnexpectedly;

    /// <summary>Peak audio level (0..1) since the last call; resets on read.</summary>
    public float ReadPeak()
    {
        float p = _peak;
        _peak = 0;
        return Math.Min(1f, p);
    }

    public void Start(Int32Rect region)
    {
        if (IsRecording) throw new InvalidOperationException("Already recording.");
        if (region.Width < 2 || region.Height < 2)
            throw new ArgumentException("Capture region is too small.", nameof(region));

        Region = region;
        TempFile = Path.Combine(Path.GetTempPath(), $"CaptureEverything_{Guid.NewGuid():N}.mp4");
        FramesRecorded = 0;
        _peak = 0;
        _stopRequested = false;
        while (_audioQueue.TryDequeue(out _)) { }

        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _workerTask = Task.Factory.StartNew(
            () => RecordLoop(region, TempFile, ready),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        // Wait for sink writer + GDI initialization so startup errors surface immediately.
        ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>Stops recording and returns the temporary MP4 path (null if nothing was recorded).</summary>
    public async Task<string?> StopAsync()
    {
        if (_workerTask is null) return null;
        _stopRequested = true;
        _clock.Stop();
        IsRecording = false;

        try
        {
            await _workerTask;
        }
        finally
        {
            _workerTask = null;
        }

        return FramesRecorded > 0 && TempFile != null && File.Exists(TempFile) ? TempFile : null;
    }

    public void DeleteTempFile()
    {
        try { if (TempFile != null && File.Exists(TempFile)) File.Delete(TempFile); } catch { /* best effort */ }
        TempFile = null;
    }

    private void RecordLoop(Int32Rect region, string outputPath, TaskCompletionSource ready)
    {
        IntPtr screenDc = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr hDib = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        IntPtr sinkPtr = IntPtr.Zero;
        IMFSinkWriter? sinkWriter = null;
        bool timerPeriodSet = false;
        bool started = false;

        try
        {
            MediaFoundationApi.Startup();

            var (encodeWidth, encodeHeight) = ComputeEncodeSize(region.Width, region.Height);
            int stride = encodeWidth * 4;
            int frameBytes = stride * encodeHeight;

            // 1. GDI DIB section (positive biHeight = bottom-up, matching MFVideoFormat_RGB32 default stride).
            screenDc = GetDC(IntPtr.Zero);
            memDc = CreateCompatibleDC(screenDc);
            var bmi = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = encodeWidth,
                biHeight = encodeHeight,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
                biSizeImage = frameBytes
            };
            hDib = CreateDIBSection(screenDc, ref bmi, DIB_RGB_COLORS, out IntPtr dibBits, IntPtr.Zero, 0);
            if (hDib == IntPtr.Zero || dibBits == IntPtr.Zero)
                throw new InvalidOperationException("CreateDIBSection failed while initializing video capture.");

            oldBitmap = SelectObject(memDc, hDib);
            if (encodeWidth != region.Width || encodeHeight != region.Height)
            {
                SetStretchBltMode(memDc, HALFTONE);
                SetBrushOrgEx(memDc, 0, 0, IntPtr.Zero);
            }

            // 2. Media Foundation Sink Writer
            sinkWriter = CreateSinkWriter(outputPath, out sinkPtr);
            int videoStreamIndex = ConfigureVideoStream(sinkWriter, encodeWidth, encodeHeight, stride);

            // 3. Optional system audio stream (WASAPI loopback -> AAC)
            int audioStreamIndex = TryStartAudioCapture(sinkWriter);

            MediaFoundationException.ThrowIfFailed(sinkWriter.BeginWriting());

            timeBeginPeriod(1);
            timerPeriodSet = true;

            IsRecording = true;
            _clock.Restart();
            started = true;
            ready.TrySetResult();

            long audioFramesWritten = 0;
            long nextFrameIndex = 0;
            byte[] frameBuffer = new byte[frameBytes];
            var frameClock = Stopwatch.StartNew();

            while (!_stopRequested)
            {
                long elapsedHns = frameClock.ElapsedTicks * 10_000_000L / Stopwatch.Frequency;
                long targetHns = nextFrameIndex * FrameDurationHns;

                if (elapsedHns < targetHns)
                {
                    int sleepMs = (int)((targetHns - elapsedHns) / 10_000L);
                    if (sleepMs > 1) Thread.Sleep(sleepMs - 1);
                    else Thread.Yield();
                    continue;
                }

                // Capture screen frame into the DIB section.
                bool captured;
                if (encodeWidth == region.Width && encodeHeight == region.Height)
                {
                    captured = BitBlt(memDc, 0, 0, region.Width, region.Height,
                        screenDc, region.X, region.Y, SRCCOPY | CAPTUREBLT);
                }
                else
                {
                    captured = StretchBlt(memDc, 0, 0, encodeWidth, encodeHeight,
                        screenDc, region.X, region.Y, region.Width, region.Height, SRCCOPY | CAPTUREBLT);
                }

                if (captured)
                {
                    GdiFlush();
                    Marshal.Copy(dibBits, frameBuffer, 0, frameBytes);
                    WriteSampleBuffer(sinkWriter, videoStreamIndex, frameBuffer, elapsedHns, FrameDurationHns);
                    FramesRecorded++;
                }

                if (audioStreamIndex >= 0)
                    DrainAudioQueue(sinkWriter, audioStreamIndex, ref audioFramesWritten);

                // Advance to the next frame slot; skip slots if encoding fell behind real time.
                do
                {
                    nextFrameIndex++;
                } while (nextFrameIndex * FrameDurationHns <= elapsedHns);
            }

            StopAudioCapture();
            if (audioStreamIndex >= 0)
                DrainAudioQueue(sinkWriter, audioStreamIndex, ref audioFramesWritten);

            if (FramesRecorded > 0)
                MediaFoundationException.ThrowIfFailed(sinkWriter.DoFinalize());
        }
        catch (Exception ex)
        {
            if (!started)
            {
                ready.TrySetException(ex);
            }
            else
            {
                IsRecording = false;
                _clock.Stop();
                StoppedUnexpectedly?.Invoke(this, ex);
            }
        }
        finally
        {
            if (timerPeriodSet) timeEndPeriod(1);
            StopAudioCapture();

            if (sinkWriter != null) Marshal.ReleaseComObject(sinkWriter);
            if (sinkPtr != IntPtr.Zero) Marshal.Release(sinkPtr);

            if (oldBitmap != IntPtr.Zero && memDc != IntPtr.Zero) SelectObject(memDc, oldBitmap);
            if (hDib != IntPtr.Zero) DeleteObject(hDib);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);

            if (FramesRecorded == 0) DeleteTempFile();
        }
    }

    private static (int Width, int Height) ComputeEncodeSize(int width, int height)
    {
        double scale = 1.0;
        if (width > MaxVideoWidth || height > MaxVideoHeight)
            scale = Math.Min((double)MaxVideoWidth / width, (double)MaxVideoHeight / height);
        else if (width < MinVideoDimension || height < MinVideoDimension)
            scale = Math.Max((double)MinVideoDimension / width, (double)MinVideoDimension / height);

        int w = Math.Clamp((int)Math.Round(width * scale) & ~1, MinVideoDimension, MaxVideoWidth);
        int h = Math.Clamp((int)Math.Round(height * scale) & ~1, MinVideoDimension, MaxVideoHeight);
        return (w, h);
    }

    private static IMFSinkWriter CreateSinkWriter(string outputPath, out IntPtr sinkPtr)
    {
        MediaFoundationException.ThrowIfFailed(MFCreateAttributes(out IntPtr attrPtr, 1));
        var attrs = (IMFAttributes)Marshal.GetObjectForIUnknown(attrPtr);
        try
        {
            MediaFoundationException.ThrowIfFailed(attrs.SetUINT32(in MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1));
            MediaFoundationException.ThrowIfFailed(
                MFCreateSinkWriterFromURL(outputPath, IntPtr.Zero, attrPtr, out sinkPtr));
            return (IMFSinkWriter)Marshal.GetObjectForIUnknown(sinkPtr);
        }
        finally
        {
            Marshal.ReleaseComObject(attrs);
            Marshal.Release(attrPtr);
        }
    }

    private static int ConfigureVideoStream(IMFSinkWriter sinkWriter, int width, int height, int stride)
    {
        long frameSize = PackUInt32(width, height);
        long frameRate = PackUInt32(Fps, 1);
        long aspect = PackUInt32(1, 1);
        int bitrate = (int)Math.Clamp((long)width * height * Fps / 5, 1_500_000L, 12_000_000L);

        MediaFoundationException.ThrowIfFailed(MFCreateMediaType(out IntPtr outPtr));
        var outType = (IMFAttributes)Marshal.GetObjectForIUnknown(outPtr);
        try
        {
            MediaFoundationException.ThrowIfFailed(outType.SetGUID(in MF_MT_MAJOR_TYPE, in MFMediaType_Video));
            MediaFoundationException.ThrowIfFailed(outType.SetGUID(in MF_MT_SUBTYPE, in MFVideoFormat_H264));
            MediaFoundationException.ThrowIfFailed(outType.SetUINT32(in MF_MT_AVG_BITRATE, bitrate));
            MediaFoundationException.ThrowIfFailed(outType.SetUINT32(in MF_MT_INTERLACE_MODE, 2)); // Progressive
            MediaFoundationException.ThrowIfFailed(outType.SetUINT64(in MF_MT_FRAME_SIZE, frameSize));
            MediaFoundationException.ThrowIfFailed(outType.SetUINT64(in MF_MT_FRAME_RATE, frameRate));
            MediaFoundationException.ThrowIfFailed(outType.SetUINT64(in MF_MT_PIXEL_ASPECT_RATIO, aspect));

            MediaFoundationException.ThrowIfFailed(sinkWriter.AddStream(outPtr, out int streamIndex));

            MediaFoundationException.ThrowIfFailed(MFCreateMediaType(out IntPtr inPtr));
            var inType = (IMFAttributes)Marshal.GetObjectForIUnknown(inPtr);
            try
            {
                MediaFoundationException.ThrowIfFailed(inType.SetGUID(in MF_MT_MAJOR_TYPE, in MFMediaType_Video));
                MediaFoundationException.ThrowIfFailed(inType.SetGUID(in MF_MT_SUBTYPE, in MFVideoFormat_RGB32));
                MediaFoundationException.ThrowIfFailed(inType.SetUINT32(in MF_MT_INTERLACE_MODE, 2));
                MediaFoundationException.ThrowIfFailed(inType.SetUINT64(in MF_MT_FRAME_SIZE, frameSize));
                MediaFoundationException.ThrowIfFailed(inType.SetUINT64(in MF_MT_FRAME_RATE, frameRate));
                MediaFoundationException.ThrowIfFailed(inType.SetUINT64(in MF_MT_PIXEL_ASPECT_RATIO, aspect));
                MediaFoundationException.ThrowIfFailed(inType.SetUINT32(in MF_MT_DEFAULT_STRIDE, stride));

                MediaFoundationException.ThrowIfFailed(sinkWriter.SetInputMediaType(streamIndex, inPtr, IntPtr.Zero));
                return streamIndex;
            }
            finally
            {
                Marshal.ReleaseComObject(inType);
                Marshal.Release(inPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(outType);
            Marshal.Release(outPtr);
        }
    }

    private int TryStartAudioCapture(IMFSinkWriter sinkWriter)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            _audioCapture = new WasapiLoopbackCapture(device);
            _captureFormat = _audioCapture.WaveFormat;
            _pcmSampleRate = _captureFormat.SampleRate is 44100 or 48000 ? _captureFormat.SampleRate : 48000;

            int streamIndex = ConfigureAudioStream(sinkWriter, _pcmSampleRate);

            try
            {
                _silenceKeeper = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
                _silenceKeeper.Init(new SilenceProvider(_captureFormat));
                _silenceKeeper.Play();
            }
            catch
            {
                _silenceKeeper?.Dispose();
                _silenceKeeper = null;
            }

            _audioCapture.DataAvailable += OnAudioDataAvailable;
            _audioCapture.StartRecording();
            return streamIndex;
        }
        catch
        {
            StopAudioCapture();
            return -1;
        }
    }

    private static int ConfigureAudioStream(IMFSinkWriter sinkWriter, int sampleRate)
    {
        MediaFoundationException.ThrowIfFailed(MFCreateMediaType(out IntPtr outPtr));
        var outType = (IMFAttributes)Marshal.GetObjectForIUnknown(outPtr);
        try
        {
            MediaFoundationException.ThrowIfFailed(outType.SetGUID(in MF_MT_MAJOR_TYPE, in MFMediaType_Audio));
            MediaFoundationException.ThrowIfFailed(outType.SetGUID(in MF_MT_SUBTYPE, in MFAudioFormat_AAC));
            MediaFoundationException.ThrowIfFailed(outType.SetUINT32(in MF_MT_AUDIO_BITS_PER_SAMPLE, 16));
            MediaFoundationException.ThrowIfFailed(outType.SetUINT32(in MF_MT_AUDIO_SAMPLES_PER_SECOND, sampleRate));
            MediaFoundationException.ThrowIfFailed(outType.SetUINT32(in MF_MT_AUDIO_NUM_CHANNELS, 2));
            MediaFoundationException.ThrowIfFailed(outType.SetUINT32(in MF_MT_AUDIO_AVG_BYTES_PER_SECOND, 24000)); // 192 kbps

            MediaFoundationException.ThrowIfFailed(sinkWriter.AddStream(outPtr, out int streamIndex));

            MediaFoundationException.ThrowIfFailed(MFCreateMediaType(out IntPtr inPtr));
            var inType = (IMFAttributes)Marshal.GetObjectForIUnknown(inPtr);
            try
            {
                MediaFoundationException.ThrowIfFailed(inType.SetGUID(in MF_MT_MAJOR_TYPE, in MFMediaType_Audio));
                MediaFoundationException.ThrowIfFailed(inType.SetGUID(in MF_MT_SUBTYPE, in MFAudioFormat_PCM));
                MediaFoundationException.ThrowIfFailed(inType.SetUINT32(in MF_MT_AUDIO_NUM_CHANNELS, 2));
                MediaFoundationException.ThrowIfFailed(inType.SetUINT32(in MF_MT_AUDIO_SAMPLES_PER_SECOND, sampleRate));
                MediaFoundationException.ThrowIfFailed(inType.SetUINT32(in MF_MT_AUDIO_BLOCK_ALIGNMENT, 4));
                MediaFoundationException.ThrowIfFailed(inType.SetUINT32(in MF_MT_AUDIO_AVG_BYTES_PER_SECOND, sampleRate * 4));
                MediaFoundationException.ThrowIfFailed(inType.SetUINT32(in MF_MT_AUDIO_BITS_PER_SAMPLE, 16));
                MediaFoundationException.ThrowIfFailed(inType.SetUINT32(in MF_MT_ALL_SAMPLES_INDEPENDENT, 1));

                MediaFoundationException.ThrowIfFailed(sinkWriter.SetInputMediaType(streamIndex, inPtr, IntPtr.Zero));
                return streamIndex;
            }
            finally
            {
                Marshal.ReleaseComObject(inType);
                Marshal.Release(inPtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(outType);
            Marshal.Release(outPtr);
        }
    }

    private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _captureFormat is null) return;
        var span = e.BufferSpan[..e.BytesRecorded];
        byte[]? pcm = ConvertToStereoPcm16(span, _captureFormat, _pcmSampleRate, out float peak);
        _peak = Math.Max(_peak, peak);
        if (pcm is { Length: > 0 })
            _audioQueue.Enqueue(pcm);
    }

    /// <summary>Converts WASAPI loopback audio (float32 or int16, any channel count) to 16-bit stereo PCM.</summary>
    private static byte[]? ConvertToStereoPcm16(ReadOnlySpan<byte> data, WaveFormat src, int dstRate, out float peak)
    {
        peak = 0;
        int channels = Math.Max(1, src.Channels);
        bool isFloat = src.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (src is WaveFormatExtensible ext && ext.SubFormat == KsDataFormatSubtypeIeeeFloat);

        int srcFrames;
        if (isFloat && src.BitsPerSample == 32)
            srcFrames = data.Length / (4 * channels);
        else if (src.BitsPerSample == 16)
            srcFrames = data.Length / (2 * channels);
        else
            return null;

        if (srcFrames <= 0) return null;

        int dstFrames = src.SampleRate == dstRate
            ? srcFrames
            : Math.Max(1, (int)Math.Round((double)srcFrames * dstRate / src.SampleRate));

        byte[] output = new byte[dstFrames * 4]; // 2 channels * 2 bytes (int16)
        var outShorts = MemoryMarshal.Cast<byte, short>(output.AsSpan());

        if (isFloat && src.BitsPerSample == 32)
        {
            var floats = MemoryMarshal.Cast<byte, float>(data);
            for (int i = 0; i < dstFrames; i++)
            {
                int srcIdx = (src.SampleRate == dstRate ? i : (int)((long)i * srcFrames / dstFrames)) * channels;
                float l = floats[srcIdx];
                float r = channels > 1 ? floats[srcIdx + 1] : l;
                peak = Math.Max(peak, Math.Max(Math.Abs(l), Math.Abs(r)));
                outShorts[i * 2] = FloatToInt16(l);
                outShorts[i * 2 + 1] = FloatToInt16(r);
            }
        }
        else
        {
            var shorts = MemoryMarshal.Cast<byte, short>(data);
            for (int i = 0; i < dstFrames; i++)
            {
                int srcIdx = (src.SampleRate == dstRate ? i : (int)((long)i * srcFrames / dstFrames)) * channels;
                short l = shorts[srcIdx];
                short r = channels > 1 ? shorts[srcIdx + 1] : l;
                peak = Math.Max(peak, Math.Max(Math.Abs(l / 32768f), Math.Abs(r / 32768f)));
                outShorts[i * 2] = l;
                outShorts[i * 2 + 1] = r;
            }
        }

        return output;
    }

    private static short FloatToInt16(float sample) =>
        (short)Math.Clamp((int)Math.Round(sample * 32767f), short.MinValue, short.MaxValue);

    private void DrainAudioQueue(IMFSinkWriter sinkWriter, int audioStreamIndex, ref long audioFramesWritten)
    {
        while (_audioQueue.TryDequeue(out byte[]? pcm))
        {
            int frames = pcm.Length / 4; // 16-bit stereo = 4 bytes/frame
            if (frames <= 0) continue;

            long timestampHns = audioFramesWritten * 10_000_000L / _pcmSampleRate;
            long durationHns = Math.Max(1, frames * 10_000_000L / _pcmSampleRate);
            audioFramesWritten += frames;

            WriteSampleBuffer(sinkWriter, audioStreamIndex, pcm, timestampHns, durationHns);
        }
    }

    private static void WriteSampleBuffer(IMFSinkWriter sinkWriter, int streamIndex,
                                          byte[] srcData, long timestampHns, long durationHns)
    {
        int byteCount = srcData.Length;
        MediaFoundationException.ThrowIfFailed(MFCreateMemoryBuffer(byteCount, out IntPtr bufferPtr));
        var buffer = (IMFMediaBuffer)Marshal.GetObjectForIUnknown(bufferPtr);
        try
        {
            MediaFoundationException.ThrowIfFailed(buffer.Lock(out IntPtr dstPtr, out _, out _));
            try
            {
                Marshal.Copy(srcData, 0, dstPtr, byteCount);
            }
            finally
            {
                buffer.Unlock();
            }
            MediaFoundationException.ThrowIfFailed(buffer.SetCurrentLength(byteCount));

            MediaFoundationException.ThrowIfFailed(MFCreateSample(out IntPtr samplePtr));
            var sample = (IMFSample)Marshal.GetObjectForIUnknown(samplePtr);
            try
            {
                MediaFoundationException.ThrowIfFailed(sample.AddBuffer(bufferPtr));
                MediaFoundationException.ThrowIfFailed(sample.SetSampleTime(timestampHns));
                MediaFoundationException.ThrowIfFailed(sample.SetSampleDuration(durationHns));
                MediaFoundationException.ThrowIfFailed(sinkWriter.WriteSample(streamIndex, samplePtr));
            }
            finally
            {
                Marshal.ReleaseComObject(sample);
                Marshal.Release(samplePtr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(buffer);
            Marshal.Release(bufferPtr);
        }
    }

    private void StopAudioCapture()
    {
        try
        {
            if (_audioCapture != null)
            {
                _audioCapture.DataAvailable -= OnAudioDataAvailable;
                _audioCapture.StopRecording();
                _audioCapture.Dispose();
                _audioCapture = null;
            }
        }
        catch { /* best effort */ }

        try
        {
            _silenceKeeper?.Stop();
            _silenceKeeper?.Dispose();
            _silenceKeeper = null;
        }
        catch { /* best effort */ }
    }

    private static long PackUInt32(int hi, int lo) => ((long)hi << 32) | (uint)lo;

    public void Dispose()
    {
        _stopRequested = true;
        try { _workerTask?.Wait(1500); } catch { /* shutting down */ }
        StopAudioCapture();
        DeleteTempFile();
    }

    #region Media Foundation COM interfaces

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateAttributes(out IntPtr ppMFAttributes, int cInitialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMediaType(out IntPtr ppMFType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateSample(out IntPtr ppIMFSample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFCreateMemoryBuffer(int cbMaxLength, out IntPtr ppBuffer);

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int MFCreateSinkWriterFromURL(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszOutputURL,
        IntPtr pByteStream,
        IntPtr pAttributes,
        out IntPtr ppSinkWriter);

    [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFAttributes
    {
        [PreserveSig] int GetItem(in Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(in Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(in Guid guidKey, IntPtr value, out int pbResult);
        [PreserveSig] int Compare(IntPtr pTheirs, int matchType, out int pbResult);
        [PreserveSig] int GetUINT32(in Guid guidKey, out int punValue);
        [PreserveSig] int GetUINT64(in Guid guidKey, out long punValue);
        [PreserveSig] int GetDouble(in Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(in Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(in Guid guidKey, out int pcchLength);
        [PreserveSig] int GetString(in Guid guidKey, IntPtr pwszValue, int cchBufSize, IntPtr pcchLength);
        [PreserveSig] int GetAllocatedString(in Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
        [PreserveSig] int GetBlobSize(in Guid guidKey, out int pcbBlobSize);
        [PreserveSig] int GetBlob(in Guid guidKey, IntPtr pBuf, int cbBufSize, IntPtr pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(in Guid guidKey, out IntPtr ppBuf, out int pcbSize);
        [PreserveSig] int GetUnknown(in Guid guidKey, in Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(in Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem(in Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(in Guid guidKey, int unValue);
        [PreserveSig] int SetUINT64(in Guid guidKey, long unValue);
        [PreserveSig] int SetDouble(in Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(in Guid guidKey, in Guid guidValue);
        [PreserveSig] int SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(in Guid guidKey, IntPtr pBuf, int cbBufSize);
        [PreserveSig] int SetUnknown(in Guid guidKey, IntPtr pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int pcItems);
        [PreserveSig] int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IntPtr pDest);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr ppbBuffer, out int pcbMaxLength, out int pcbCurrentLength);
        [PreserveSig] int Unlock();
        [PreserveSig] int GetCurrentLength(out int pcbCurrentLength);
        [PreserveSig] int SetCurrentLength(int cbCurrentLength);
        [PreserveSig] int GetMaxLength(out int pcbMaxLength);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSample
    {
        // IMFAttributes base slots (30 methods)
        [PreserveSig] int GetItem(in Guid guidKey, IntPtr pValue);
        [PreserveSig] int GetItemType(in Guid guidKey, out int pType);
        [PreserveSig] int CompareItem(in Guid guidKey, IntPtr value, out int pbResult);
        [PreserveSig] int Compare(IntPtr pTheirs, int matchType, out int pbResult);
        [PreserveSig] int GetUINT32(in Guid guidKey, out int punValue);
        [PreserveSig] int GetUINT64(in Guid guidKey, out long punValue);
        [PreserveSig] int GetDouble(in Guid guidKey, out double pfValue);
        [PreserveSig] int GetGUID(in Guid guidKey, out Guid pguidValue);
        [PreserveSig] int GetStringLength(in Guid guidKey, out int pcchLength);
        [PreserveSig] int GetString(in Guid guidKey, IntPtr pwszValue, int cchBufSize, IntPtr pcchLength);
        [PreserveSig] int GetAllocatedString(in Guid guidKey, out IntPtr ppwszValue, out int pcchLength);
        [PreserveSig] int GetBlobSize(in Guid guidKey, out int pcbBlobSize);
        [PreserveSig] int GetBlob(in Guid guidKey, IntPtr pBuf, int cbBufSize, IntPtr pcbBlobSize);
        [PreserveSig] int GetAllocatedBlob(in Guid guidKey, out IntPtr ppBuf, out int pcbSize);
        [PreserveSig] int GetUnknown(in Guid guidKey, in Guid riid, out IntPtr ppv);
        [PreserveSig] int SetItem(in Guid guidKey, IntPtr value);
        [PreserveSig] int DeleteItem(in Guid guidKey);
        [PreserveSig] int DeleteAllItems();
        [PreserveSig] int SetUINT32(in Guid guidKey, int unValue);
        [PreserveSig] int SetUINT64(in Guid guidKey, long unValue);
        [PreserveSig] int SetDouble(in Guid guidKey, double fValue);
        [PreserveSig] int SetGUID(in Guid guidKey, in Guid guidValue);
        [PreserveSig] int SetString(in Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        [PreserveSig] int SetBlob(in Guid guidKey, IntPtr pBuf, int cbBufSize);
        [PreserveSig] int SetUnknown(in Guid guidKey, IntPtr pUnknown);
        [PreserveSig] int LockStore();
        [PreserveSig] int UnlockStore();
        [PreserveSig] int GetCount(out int pcItems);
        [PreserveSig] int GetItemByIndex(int unIndex, out Guid pguidKey, IntPtr pValue);
        [PreserveSig] int CopyAllItems(IntPtr pDest);

        // IMFSample slots
        [PreserveSig] int GetSampleFlags(out int pdwSampleFlags);
        [PreserveSig] int SetSampleFlags(int dwSampleFlags);
        [PreserveSig] int GetSampleTime(out long phnsSampleTime);
        [PreserveSig] int SetSampleTime(long hnsSampleTime);
        [PreserveSig] int GetSampleDuration(out long phnsSampleDuration);
        [PreserveSig] int SetSampleDuration(long hnsSampleDuration);
        [PreserveSig] int GetBufferCount(out int pdwBufferCount);
        [PreserveSig] int GetBufferByIndex(int dwIndex, out IntPtr ppBuffer);
        [PreserveSig] int ConvertToContiguousBuffer(out IntPtr ppBuffer);
        [PreserveSig] int AddBuffer(IntPtr pBuffer);
        [PreserveSig] int RemoveBufferByIndex(int dwIndex);
        [PreserveSig] int RemoveAllBuffers();
        [PreserveSig] int GetTotalLength(out int pcbTotalLength);
        [PreserveSig] int CopyToBuffer(IntPtr pBuffer);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IntPtr pTargetMediaType, out int pdwStreamIndex);
        [PreserveSig] int SetInputMediaType(int dwStreamIndex, IntPtr pInputMediaType, IntPtr pEncodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(int dwStreamIndex, IntPtr pSample);
        [PreserveSig] int SendStreamTick(int dwStreamIndex, long llTimestamp);
        [PreserveSig] int PlaceMarker(int dwStreamIndex, IntPtr pvContext);
        [PreserveSig] int NotifyEndOfSegment(int dwStreamIndex);
        [PreserveSig] int Flush(int dwStreamIndex);
        [PreserveSig] int DoFinalize();
        [PreserveSig] int GetServiceForStream(int dwStreamIndex, in Guid guidService, in Guid riid, out IntPtr ppvObject);
        [PreserveSig] int GetStatistics(int dwStreamIndex, IntPtr pStats);
    }

    #endregion
}
