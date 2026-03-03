using NAudio.Wave;
using System.IO;

namespace mystickymonologues.Services;

public class AudioService : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioStream;
    private WaveFileWriter? _waveWriter;
    private bool _isRecording;

    public bool IsRecording => _isRecording;

    public void StartRecording()
    {
        if (_isRecording) return;

        _audioStream = new MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 1) // 16kHz mono for speech recognition
        };

        _waveWriter = new WaveFileWriter(_audioStream, _waveIn.WaveFormat);

        _waveIn.DataAvailable += (s, e) =>
        {
            _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
        };

        _waveIn.StartRecording();
        _isRecording = true;
    }

    public byte[] StopRecording()
    {
        if (!_isRecording) return Array.Empty<byte>();

        _waveIn?.StopRecording();
        _waveWriter?.Flush();
        _isRecording = false;

        var audioData = _audioStream?.ToArray() ?? Array.Empty<byte>();

        _waveWriter?.Dispose();
        _waveIn?.Dispose();
        _audioStream?.Dispose();

        _waveWriter = null;
        _waveIn = null;
        _audioStream = null;

        return audioData;
    }

    public void Dispose()
    {
        if (_isRecording) StopRecording();
        _waveWriter?.Dispose();
        _waveIn?.Dispose();
        _audioStream?.Dispose();
    }
}
