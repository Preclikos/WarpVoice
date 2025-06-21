using NAudio.Wave;

namespace WarpVoice.Audio
{
public class TimestampAlignedSampleProvider : ISampleProvider
{
    private readonly WaveFormat _waveFormat;
    private readonly UserAudioBuffer _buffer;
    private long _currentTimestamp;
    private readonly int _frameSize;

    public TimestampAlignedSampleProvider(UserAudioBuffer buffer, WaveFormat waveFormat)
    {
        _waveFormat = waveFormat;
        _buffer = buffer;
        _frameSize = 960 * waveFormat.Channels;
        _currentTimestamp = 0;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = 0;

        while (samplesRead < count)
        {
            float[] frame = _buffer.GetFrame(_currentTimestamp, _frameSize);
            Array.Copy(frame, 0, buffer, offset + samplesRead, frame.Length);
            samplesRead += frame.Length;
            _currentTimestamp += frame.Length / _waveFormat.Channels;
        }

        return samplesRead;
    }

    public WaveFormat WaveFormat => _waveFormat;
}
}
