using NAudio.Wave.SampleProviders;
using NAudio.Wave;
using System.Diagnostics;

namespace WarpVoice.TTS
{
    public class PiperTTS
    {
        private string piperPath = "/home/preclikos/piper/piper";
        private string modelPath = "/home/preclikos/piper/en_US-lessac-medium.onnx";

        public PiperTTS(/*string piperExecutablePath, string modelDirectoryPath*/)
        {
            /*piperPath = piperExecutablePath;
            modelPath = modelDirectoryPath;*/
        }

        /// <summary>
        /// Synthesize text to WAV file using Piper TTS CLI.
        /// </summary>
        /// <param name="text">Text to synthesize</param>
        /// <param name="outputWavFile">Output WAV file path</param>
        public byte[] Synthesize(string text)
        {
            if (!File.Exists(piperPath))
                throw new FileNotFoundException("Piper executable not found.", piperPath);

            if (!File.Exists(modelPath))
                throw new DirectoryNotFoundException("Model folder not found: " + modelPath);

            string fileName = Guid.NewGuid().ToString() + ".wav";

            var psi = new ProcessStartInfo
            {
                FileName = piperPath,
                Arguments = $"--model \"{modelPath}\" --output_file \"{fileName}\"",
                RedirectStandardInput = true,     // <-- This allows writing to stdin
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (var process = new Process { StartInfo = psi })
            {
                process.Start();

                // Write text to Piper's stdin
                using (var writer = process.StandardInput)
                {
                    writer.WriteLine(text);
                }

                // Optionally capture stdout/stderr
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();

                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new Exception($"Piper exited with code {process.ExitCode}");

                return PiperTTS.ResampleWavTo48kHzStereo(fileName);
            }
        }
        public static byte[] ResampleWavTo48kHzStereo(string inputFile)
        {
            using (var reader = new AudioFileReader(inputFile))
            {
                // Resample to 48kHz
                var resampler = new WdlResamplingSampleProvider(reader, 48000);

                // Ensure stereo output:
                ISampleProvider stereoProvider;

                if (resampler.WaveFormat.Channels == 1)
                {
                    // Convert mono to stereo by duplicating channel
                    stereoProvider = new MonoToStereoSampleProvider(resampler);
                }
                else
                {
                    // Already stereo or more channels — use as is
                    stereoProvider = resampler;
                }

                // Create 16-bit PCM WaveFormat stereo, 48kHz
                var outFormat = new WaveFormat(48000, 16, 2);

                // Convert from float samples to 16-bit PCM samples
                var pcmProvider = new SampleToWaveProvider16(stereoProvider);

                using (var ms = new MemoryStream())
                {
                    using (var waveWriter = new WaveFileWriter(ms, outFormat))
                    {
                        byte[] buffer = new byte[pcmProvider.WaveFormat.AverageBytesPerSecond / 4]; // ~250ms buffer
                        int bytesRead;
                        while ((bytesRead = pcmProvider.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            waveWriter.Write(buffer, 0, bytesRead);
                        }
                    }
                    return ms.ToArray();
                }
            }
        }
    }
}