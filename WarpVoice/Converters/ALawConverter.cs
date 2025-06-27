using NAudio.Codecs;

namespace WarpVoice.Converters
{
    public static class ALawConverter
    {
        /// <summary>
        /// Decodes an A-law encoded byte array into a 16-bit PCM byte array.
        /// </summary>
        /// <param name="aLawBytes">A-law encoded byte array.</param>
        /// <returns>Decoded 16-bit PCM samples as a byte array (little-endian).</returns>
        public static byte[] DecodeALaw(byte[] aLawBytes)
        {
            // Allocate array for decoded 16-bit PCM samples (1 short per input byte)
            short[] pcmSamples = new short[aLawBytes.Length];

            // Decode each A-law byte into a 16-bit PCM sample
            for (int i = 0; i < aLawBytes.Length; i++)
            {
                pcmSamples[i] = ALawDecoder.ALawToLinearSample(aLawBytes[i]);
            }

            // Convert short[] to byte[] (2 bytes per sample, little-endian)
            byte[] pcmBytes = new byte[pcmSamples.Length * 2];
            Buffer.BlockCopy(pcmSamples, 0, pcmBytes, 0, pcmBytes.Length);

            return pcmBytes;
        }
    }
}
