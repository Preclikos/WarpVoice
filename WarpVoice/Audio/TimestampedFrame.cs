namespace WarpVoice.Audio
{
    public class TimestampedFrame
    {
        public long SampleTimestamp;
        public float[] Samples; // Already decoded to float PCM
    }
}
