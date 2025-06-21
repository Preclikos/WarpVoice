using NAudio.Wave.SampleProviders;
using NAudio.Wave;
using System.Collections.Concurrent;
using System.Diagnostics;
using Discord.Audio;
using SIPSorcery.Net;
using NAudio.Codecs;
using System.Threading;

namespace WarpVoice.Audio
{
    public class DiscordAudioMixer
    {
        private readonly ConcurrentDictionary<ulong, UserAudioBuffer> _userBuffers = new();
        private readonly ConcurrentDictionary<ulong, long> _startTimeStamp = new();
        private readonly ConcurrentDictionary<ulong, DateTime> _startDateTime = new();
        private readonly ConcurrentDictionary<ulong, TimestampAlignedSampleProvider> _userInputs = new();
        private readonly CancellationToken _cancellationToken;
        private readonly MixingSampleProvider _mixer;
        private readonly WaveFormat _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public DiscordAudioMixer(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _mixer = new MixingSampleProvider(_waveFormat) { ReadFully = true };
        }

        public UserAudioBuffer AddUserStream(ulong userId)
        {
            var buffer = new UserAudioBuffer();
            _userBuffers[userId] = buffer;
            _userInputs[userId] = new TimestampAlignedSampleProvider(buffer, _waveFormat);
            _mixer.AddMixerInput(_userInputs[userId]);

            return _userBuffers[userId];
        }

        public void RemoveUserStream(ulong userId)
        {
            _userBuffers.TryRemove(userId, out var buffer);
            _startTimeStamp.TryRemove(userId, out var timeStamp);
            _startDateTime.TryRemove(userId, out var dateTime);
            if (_userInputs.TryRemove(userId, out var input))
            {
                _mixer.RemoveMixerInput(input);
            }
        }

        public static float[] ConvertPcm16ToFloat(byte[] pcm16Bytes)
        {
            if (pcm16Bytes == null)
                throw new ArgumentNullException(nameof(pcm16Bytes));

            if (pcm16Bytes.Length % 4 != 0)
                throw new ArgumentException("Expected 16-bit stereo PCM data (bytes must be a multiple of 4).");

            int totalSamples = pcm16Bytes.Length / 2; // 2 bytes per sample
            float[] floatSamples = new float[totalSamples];

            for (int i = 0; i < totalSamples; i++)
            {
                int byteIndex = i * 2;

                // Read little-endian 16-bit sample
                short sample = (short)(pcm16Bytes[byteIndex] | (pcm16Bytes[byteIndex + 1] << 8));

                // Normalize to float [-1.0f, ~0.99997f]
                floatSamples[i] = sample / 32768f;
            }

            return floatSamples;
        }

        public async Task FeedUserFrameAsync(ulong userId, byte[] opusPayload, long rtpTimestamp)
        {
            if (!_userBuffers.TryGetValue(userId, out var buffer))
            {
                AddUserStream(userId);
                _userBuffers.TryGetValue(userId, out buffer);
            }

            if (!_startTimeStamp.TryGetValue(userId, out var userBaseRtpTimestamp))
            {
                _startTimeStamp.TryAdd(userId, rtpTimestamp);
                userBaseRtpTimestamp = rtpTimestamp;
            }

            var floatSamples = ConvertPcm16ToFloat(opusPayload);

            buffer.AddFrame(new TimestampedFrame
            {
                SampleTimestamp = (rtpTimestamp - userBaseRtpTimestamp) + 960,
                Samples = floatSamples
            });
        }

        public static byte[] ConvertFloat32ToInt16LE(float[] floatBuffer)
        {
            // Allocate byte array: 2 bytes per sample
            byte[] int16Buffer = new byte[floatBuffer.Length * 2];

            for (int i = 0; i < floatBuffer.Length; i++)
            {
                // Clamp float to [-1.0f, 1.0f]
                float sample = Math.Clamp(floatBuffer[i], -1.0f, 1.0f);

                // Convert to 16-bit signed int
                short intSample = (short)(sample * short.MaxValue);

                // Write to byte array in little endian order
                int16Buffer[i * 2] = (byte)(intSample & 0xFF);          // Low byte
                int16Buffer[i * 2 + 1] = (byte)((intSample >> 8) & 0xFF); // High byte
            }

            return int16Buffer;
        }

        public static byte[] ConvertFloatToMuLawChunk(float[] floatSamples)
        {
            byte[] muLawBytes = new byte[floatSamples.Length];

            for (int i = 0; i < floatSamples.Length; i++)
            {
                // Clamp float to [-1.0, 1.0] and convert to short (16-bit PCM)
                float clamped = Math.Clamp(floatSamples[i], -1.0f, 1.0f);
                short pcm = (short)(clamped * short.MaxValue);

                // Convert short PCM to μ-law
                muLawBytes[i] = MuLawEncoder.LinearToMuLawSample(pcm);
            }

            return muLawBytes;
        }
        const int targetMs = 20;
        public async Task StartMixingLoopAsync(IAudioClient audioClient, RTPSession mediaSession)
        {
            var buffer = new float[960 * 2]; // 20ms stereo float at 48kHz
            DateTime startTime = DateTime.UtcNow;
            Stopwatch stopwatch = Stopwatch.StartNew();
            int iteration = 0;

            while (!_cancellationToken.IsCancellationRequested)
            {
                var iterationStartTime = stopwatch.ElapsedMilliseconds;

                int read = _mixer.Read(buffer, 0, buffer.Length);
                var ulawBytes = FloatToMuLawSimpleConverter.ConvertFloatToMuLawWithDuration(buffer, 48000, 2);

                try
                {
                    if (mediaSession.AudioDestinationEndPoint != null)
                    {
                        mediaSession.SendAudio((uint)ulawBytes.rtpDuration, ulawBytes.ulawBytes);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                iteration++;

                // Target time for next frame in ms
                long targetNextFrameTime = iteration * targetMs;

                // Time to sleep (how much until the next frame should be sent)
                long delay = targetNextFrameTime - stopwatch.ElapsedMilliseconds;

                if (delay > 0)
                {
                    await Task.Delay((int)delay);
                }
                else
                {
                    // Running behind schedule
                    Console.WriteLine($"Warning: Audio loop running behind by {-delay}ms");
                }
            }
        }

        public void ReceiveSendToDiscord(IAudioClient audioClient, RTPSession mediaSession)
        {
            var discordStream = audioClient.CreatePCMStream(AudioApplication.Voice, bufferMillis: 60);
            mediaSession.OnAudioFrameReceived += async (audioFrame) =>
            {
                byte[] convertedChunk = ConvertMuLawChunk(audioFrame.EncodedAudio);
                try
                {
                    await discordStream.WriteAsync(convertedChunk, 0, convertedChunk.Length, _cancellationToken);
                }
                catch (OperationCanceledException)
                {

                }
            };
        }

        public byte[] ConvertMuLawChunk(byte[] muLawChunk)
        {
            // Step 1: Decode μ-law to 16-bit PCM mono
            short[] monoPcmSamples = new short[muLawChunk.Length];
            for (int i = 0; i < muLawChunk.Length; i++)
            {
                monoPcmSamples[i] = MuLawDecoder.MuLawToLinearSample(muLawChunk[i]);
            }

            // Step 2: Resample from 8000 Hz to 48000 Hz (6x)
            int inputSampleRate = 8000;
            int outputSampleRate = 48000;
            int inputLength = monoPcmSamples.Length;
            int outputLength = (int)((long)inputLength * outputSampleRate / inputSampleRate);

            short[] resampledMono = new short[outputLength];

            for (int i = 0; i < outputLength; i++)
            {
                double srcIndex = (double)i * inputLength / outputLength;
                int index = (int)Math.Floor(srcIndex);
                double frac = srcIndex - index;

                short sample1 = monoPcmSamples[Math.Min(index, inputLength - 1)];
                short sample2 = monoPcmSamples[Math.Min(index + 1, inputLength - 1)];

                resampledMono[i] = (short)(sample1 + (sample2 - sample1) * frac); // Linear interpolation
            }

            // Step 3: Convert mono to stereo
            short[] stereoSamples = new short[resampledMono.Length * 2];
            for (int i = 0; i < resampledMono.Length; i++)
            {
                short sample = resampledMono[i];
                stereoSamples[i * 2] = sample;     // Left
                stereoSamples[i * 2 + 1] = sample; // Right
            }

            // Step 4: Convert short[] to byte[]
            byte[] outputBytes = new byte[stereoSamples.Length * sizeof(short)];
            Buffer.BlockCopy(stereoSamples, 0, outputBytes, 0, outputBytes.Length);

            return outputBytes;
        }
    }
}
