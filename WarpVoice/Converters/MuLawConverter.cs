using NAudio.Codecs;

namespace WarpVoice.Converters
{
    public static class MuLawConverter
    {
        /// <summary>
        /// Converts a float buffer (typically 48kHz stereo) to 8kHz mono μ-law encoded byte array.
        /// Also returns RTP duration (number of 8kHz samples).
        /// </summary>
        /// <param name="inputFloats">Input audio samples in float format. Interleaved if stereo.</param>
        /// <param name="inputSampleRate">Sample rate of the input audio. Default is 48000 Hz.</param>
        /// <param name="inputChannels">Number of channels in the input. 1 = mono, 2 = stereo. Default is 2.</param>
        /// <returns>Tuple containing μ-law encoded bytes and RTP duration (sample count at 8kHz).</returns>
        public static (byte[] ulawBytes, uint rtpDuration) ConvertFloatToMuLawWithDuration(
            float[] inputFloats, int inputSampleRate = 48000, int inputChannels = 2)
        {
            // Downmix to mono if needed
            float[] monoFloats = inputChannels == 1
                ? inputFloats
                : WaveConverter.DownmixStereoToMono(inputFloats);

            // Resample from original sample rate to 8000 Hz
            float[] resampled = WaveConverter.Resample(monoFloats, inputSampleRate, 8000);

            // Prepare output byte array
            byte[] ulawBytes = new byte[resampled.Length];

            // Convert each float sample to μ-law
            for (int i = 0; i < resampled.Length; i++)
            {
                // Clamp to valid audio range [-1, 1]
                float clamped = Math.Clamp(resampled[i], -1f, 1f);

                // Scale to 16-bit PCM and convert to μ-law
                short pcm = (short)(clamped * short.MaxValue);
                ulawBytes[i] = MuLawEncoder.LinearToMuLawSample(pcm);
            }

            // RTP duration is simply the number of samples at 8000 Hz
            uint durationRtpUnits = (uint)resampled.Length;

            return (ulawBytes, durationRtpUnits);
        }

        /// <summary>
        /// Decodes a μ-law encoded byte array into 16-bit PCM byte array.
        /// </summary>
        /// <param name="muLawBytes">μ-law encoded byte array.</param>
        /// <returns>Decoded 16-bit PCM samples as a byte array (little-endian).</returns>
        public static byte[] DecodeMuLaw(byte[] muLawBytes)
        {
            // Allocate array for decoded 16-bit PCM samples
            short[] pcmSamples = new short[muLawBytes.Length];

            // Decode each μ-law byte into a PCM short
            for (int i = 0; i < muLawBytes.Length; i++)
            {
                pcmSamples[i] = MuLawDecoder.MuLawToLinearSample(muLawBytes[i]);
            }

            // Convert short[] to byte[] (2 bytes per sample)
            byte[] pcmBytes = new byte[pcmSamples.Length * 2];
            Buffer.BlockCopy(pcmSamples, 0, pcmBytes, 0, pcmBytes.Length);

            return pcmBytes;
        }
    }
}
