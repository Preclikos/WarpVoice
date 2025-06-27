namespace WarpVoice.Audio
{
    public class UserAudioBuffer
    {
        public readonly SortedList<long, TimestampedFrame> Buffer = new();
        public long LastConsumedSample = 0;

        public void AddFrame(TimestampedFrame frame)
        {
            lock (Buffer)
            {
                Buffer[frame.SampleTimestamp] = frame;
            }
        }

        public float[] GetFrame(long timestamp, int frameSize)
        {
            lock (Buffer)
            {
                if (Buffer.TryGetValue(timestamp, out var frame))
                {
                    Buffer.Remove(timestamp);
                    LastConsumedSample = timestamp + frame.Samples.Length / 2;
                    return frame.Samples;
                }

                // Insert silence
                return new float[frameSize];
            }
        }
    }
}
