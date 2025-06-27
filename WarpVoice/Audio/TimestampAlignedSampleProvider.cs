using NAudio.Wave;

namespace WarpVoice.Audio
{
    public class TimestampAlignedSampleProvider(UserAudioBuffer buffer, WaveFormat waveFormat) : ISampleProvider
    {
        private readonly WaveFormat _waveFormat = waveFormat;
        private readonly UserAudioBuffer _buffer = buffer;
        private long _currentTimestamp = 0;
        private readonly int _frameSize = 960 * waveFormat.Channels;

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
