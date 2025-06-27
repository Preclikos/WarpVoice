using NAudio.Wave.SampleProviders;
using NAudio.Wave;

namespace WarpVoice.Converters
{
    public static class WaveConverter
    {
        /// <summary>
        /// Converts 16-bit stereo PCM audio bytes to an array of normalized float samples.
        /// </summary>
        /// <param name="pcm16Bytes">Byte array of interleaved 16-bit PCM samples (stereo).</param>
        /// <returns>Array of float samples in the range [-1.0, 1.0].</returns>
        public static float[] ConvertPcm16ToFloat(byte[] pcm16Bytes)
        {
            if (pcm16Bytes == null)
                throw new ArgumentNullException(nameof(pcm16Bytes));

            if (pcm16Bytes.Length % 4 != 0)
                throw new ArgumentException("Expected 16-bit stereo PCM data (bytes must be a multiple of 4).");

            int totalSamples = pcm16Bytes.Length / 2;
            float[] floatSamples = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                int byteIndex = i * 2;

                // Combine two bytes into a 16-bit signed sample (little-endian)
                short sample = (short)(pcm16Bytes[byteIndex] | (pcm16Bytes[byteIndex + 1] << 8));

                // Normalize to range [-1.0, 1.0]
                floatSamples[i] = sample / 32768f;
            }

            return floatSamples;
        }

        /// <summary>
        /// Converts a float sample (range [-1.0, 1.0]) to a 16-bit PCM value.
        /// </summary>
        /// <param name="sample">Float sample to convert.</param>
        /// <returns>PCM-encoded 16-bit sample.</returns>
        public static short FloatToPcm16(float sample)
        {
            // Clamp to valid audio range
            sample = Math.Clamp(sample, -1f, 1f);

            // Scale and convert to short
            return (short)(sample * short.MaxValue);
        }

        /// <summary>
        /// Resamples an audio buffer using NAudio's WdlResamplingSampleProvider.
        /// </summary>
        /// <param name="input">Float array of mono audio samples.</param>
        /// <param name="inputRate">Input sample rate (Hz).</param>
        /// <param name="outputRate">Output sample rate (Hz).</param>
        /// <returns>Resampled float array.</returns>
        public static float[] Resample(float[] input, int inputRate, int outputRate)
        {
            if (input == null || input.Length == 0)
                return Array.Empty<float>();

            // Convert float array to byte array (IEEE 32-bit float)
            byte[] inputBytes = new byte[input.Length * sizeof(float)];
            Buffer.BlockCopy(input, 0, inputBytes, 0, inputBytes.Length);

            // Create a buffered provider to stream audio samples
            var sourceProvider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(inputRate, 1));
            sourceProvider.AddSamples(inputBytes, 0, inputBytes.Length);

            // Convert to ISampleProvider for resampling
            var sampleProvider = sourceProvider.ToSampleProvider();

            // Initialize resampler
            var resampler = new WdlResamplingSampleProvider(sampleProvider, outputRate);

            // Estimate the length of the output buffer
            int outputLength = (int)((long)input.Length * outputRate / inputRate);
            float[] output = new float[outputLength];

            // Read resampled data
            int samplesRead = resampler.Read(output, 0, outputLength);

            // Trim the array if fewer samples were produced
            if (samplesRead < outputLength)
            {
                Array.Resize(ref output, samplesRead);
            }

            return output;
        }

        /// <summary>
        /// Downmixes stereo audio to mono by averaging each pair of left and right samples.
        /// </summary>
        /// <param name="stereoSamples">Float array of interleaved stereo samples.</param>
        /// <returns>Mono float array.</returns>
        public static float[] DownmixStereoToMono(float[] stereoSamples)
        {
            int monoLength = stereoSamples.Length / 2;
            float[] mono = new float[monoLength];

            for (int i = 0; i < monoLength; i++)
            {
                // Average left and right samples
                mono[i] = 0.5f * (stereoSamples[i * 2] + stereoSamples[i * 2 + 1]);
            }

            return mono;
        }
    }
}
