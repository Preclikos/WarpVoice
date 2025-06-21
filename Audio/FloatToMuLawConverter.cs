using NAudio.Codecs;

namespace WarpVoice.Audio
{
    public static class FloatToMuLawSimpleConverter
    {
        /// <summary>
        /// Convert a float[] buffer (48kHz stereo) to 8kHz mono μ-law byte[].
        /// </summary>
        /// <param name="inputFloats">Input float samples (interleaved if stereo)</param>
        /// <param name="inputSampleRate">Input sample rate, e.g. 48000</param>
        /// <param name="inputChannels">Number of channels in input (1=mono, 2=stereo)</param>
        /// <returns>μ-law encoded bytes at 8kHz mono</returns>
        public static (byte[] ulawBytes, uint rtpDuration) ConvertFloatToMuLawWithDuration(
            float[] inputFloats, int inputSampleRate = 48000, int inputChannels = 2)
        {
            // Downmix
            float[] monoFloats = inputChannels == 1 ? inputFloats : DownmixStereoToMono(inputFloats);

            // Resample
            float[] resampled = Resample(monoFloats, inputSampleRate, 8000);

            // Convert float to ulaw bytes
            byte[] ulawBytes = new byte[resampled.Length];
            for (int i = 0; i < resampled.Length; i++)
            {
                float clamped = Math.Clamp(resampled[i], -1f, 1f);
                short pcm = (short)(clamped * short.MaxValue);
                ulawBytes[i] = MuLawEncoder.LinearToMuLawSample(pcm);
            }

            // Duration in RTP timestamp units = number of samples at 8kHz
            uint durationRtpUnits = (uint)resampled.Length;

            return (ulawBytes, durationRtpUnits);
        }

        // Simple stereo-to-mono downmix by averaging pairs
        private static float[] DownmixStereoToMono(float[] stereoSamples)
        {
            int monoLength = stereoSamples.Length / 2;
            float[] mono = new float[monoLength];
            for (int i = 0; i < monoLength; i++)
            {
                mono[i] = 0.5f * (stereoSamples[i * 2] + stereoSamples[i * 2 + 1]);
            }
            return mono;
        }

        // Very basic linear resampling (nearest neighbor or linear interpolation)
        // For better quality use a resampler library but here is a simple example
        private static float[] Resample(float[] input, int inputRate, int outputRate)
        {
            int outputLength = (int)((long)input.Length * outputRate / inputRate);
            float[] output = new float[outputLength];

            double ratio = (double)input.Length / outputLength;

            for (int i = 0; i < outputLength; i++)
            {
                double pos = i * ratio;
                int index = (int)pos;
                double frac = pos - index;

                if (index + 1 < input.Length)
                    output[i] = (float)((1 - frac) * input[index] + frac * input[index + 1]);
                else
                    output[i] = input[index];
            }

            return output;
        }
    }
}
