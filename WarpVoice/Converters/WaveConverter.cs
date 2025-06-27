namespace WarpVoice.Converters
{
    public static class WaveConverter
    {
        /// <summary>
        /// Converts 16-bit stereo PCM audio bytes to an array of normalized float samples.
        /// </summary>
        public static float[] ConvertPcm16ToFloat(byte[] pcm16Bytes)
        {
            if (pcm16Bytes is null)
                throw new ArgumentNullException(nameof(pcm16Bytes));

            if (pcm16Bytes.Length % 2 != 0)
                throw new ArgumentException("Invalid PCM data. Expected even byte length.");

            int sampleCount = pcm16Bytes.Length / 2;
            float[] floatSamples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                int index = i * 2;
                short sample = (short)(pcm16Bytes[index] | (pcm16Bytes[index + 1] << 8));
                floatSamples[i] = sample / 32768f;
            }

            return floatSamples;
        }

        /// <summary>
        /// Converts a float sample [-1.0, 1.0] to a 16-bit PCM value.
        /// </summary>
        public static short FloatToPcm16(float sample)
        {
            sample = Math.Clamp(sample, -1f, 1f);
            return (short)(sample * short.MaxValue);
        }

        /// <summary>
        /// Resamples a float buffer from one sample rate to another using linear interpolation.
        /// </summary>
        public static float[] Resample(float[] input, int inputRate, int outputRate)
        {
            if (input == null || input.Length == 0 || inputRate <= 0 || outputRate <= 0)
                return Array.Empty<float>();

            int outputLength = (int)((long)input.Length * outputRate / inputRate);
            float[] output = new float[outputLength];

            double ratio = (double)input.Length / outputLength;

            for (int i = 0; i < outputLength; i++)
            {
                double position = i * ratio;
                int index = (int)position;
                double frac = position - index;

                if (index + 1 < input.Length)
                    output[i] = (float)((1.0 - frac) * input[index] + frac * input[index + 1]);
                else
                    output[i] = input[^1]; // last sample fallback
            }

            return output;
        }

        /// <summary>
        /// Downmixes interleaved stereo float samples to mono.
        /// </summary>
        public static float[] DownmixStereoToMono(float[] stereoSamples)
        {
            if (stereoSamples == null || stereoSamples.Length % 2 != 0)
                throw new ArgumentException("Stereo sample data must contain an even number of floats.");

            int monoLength = stereoSamples.Length / 2;
            float[] mono = new float[monoLength];

            for (int i = 0, j = 0; i < monoLength; i++, j += 2)
            {
                mono[i] = 0.5f * (stereoSamples[j] + stereoSamples[j + 1]);
            }

            return mono;
        }
    }
}
