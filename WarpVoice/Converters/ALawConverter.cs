using NAudio.Codecs;

namespace WarpVoice.Converters
{
    public static class ALawConverter
    {
        /// <summary>
        /// Converts a float buffer (typically 48kHz stereo) to 8kHz mono A-law encoded byte array.
        /// Also returns RTP duration (number of 8kHz samples).
        /// </summary>
        public static (byte[] alawBytes, uint rtpDuration) ConvertFloatToALawWithDuration(
            float[] inputFloats, int inputSampleRate = 48000, int inputChannels = 2)
        {
            // Downmix if stereo
            ReadOnlySpan<float> mono = inputChannels == 1
                ? inputFloats
                : DownmixStereoToMonoSpan(inputFloats);

            // Resample from inputSampleRate → 8000 Hz
            Span<float> resampled = ResampleTo8000Hz(mono, inputSampleRate);

            // Convert float PCM to A-law
            byte[] alawBytes = new byte[resampled.Length];

            for (int i = 0; i < resampled.Length; i++)
            {
                float clamped = Math.Clamp(resampled[i], -1f, 1f);
                short pcm = (short)(clamped * short.MaxValue);
                alawBytes[i] = ALawEncoder.LinearToALawSample(pcm);
            }

            return (alawBytes, (uint)alawBytes.Length);
        }

        /// <summary>
        /// Decodes an A-law encoded byte array into 16-bit PCM byte array (little-endian).
        /// </summary>
        public static byte[] DecodeALaw(byte[] alawBytes)
        {
            byte[] pcmBytes = new byte[alawBytes.Length * 2];
            for (int i = 0; i < alawBytes.Length; i++)
            {
                short sample = ALawDecoder.ALawToLinearSample(alawBytes[i]);
                pcmBytes[i * 2] = (byte)(sample & 0xFF);
                pcmBytes[i * 2 + 1] = (byte)(sample >> 8);
            }
            return pcmBytes;
        }

        // --- Helpers ---

        private static ReadOnlySpan<float> DownmixStereoToMonoSpan(float[] stereo)
        {
            int monoLen = stereo.Length / 2;
            float[] mono = new float[monoLen];

            for (int i = 0, j = 0; i < monoLen; i++, j += 2)
            {
                mono[i] = 0.5f * (stereo[j] + stereo[j + 1]);
            }

            return mono;
        }

        private static Span<float> ResampleTo8000Hz(ReadOnlySpan<float> input, int inputRate)
        {
            if (inputRate == 8000)
                return input.ToArray(); // no resampling needed

            int outputLength = (int)((input.Length / (float)inputRate) * 8000);
            float[] output = new float[outputLength];

            float resampleRatio = input.Length / (float)outputLength;

            for (int i = 0; i < outputLength; i++)
            {
                float srcIndex = i * resampleRatio;
                int index = (int)srcIndex;

                if (index < input.Length - 1)
                {
                    float frac = srcIndex - index;
                    output[i] = input[index] * (1 - frac) + input[index + 1] * frac;
                }
                else
                {
                    output[i] = input[^1]; // last sample
                }
            }

            return output;
        }
    }
}
