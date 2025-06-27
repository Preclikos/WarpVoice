using Discord.Audio;
using NAudio.Codecs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace WarpVoice.Audio
{
    public class DiscordAudioMixer
    {
        private readonly ILogger<DiscordAudioMixer> _logger;

        private readonly ConcurrentDictionary<ulong, UserAudioBuffer> _userBuffers = new();
        private readonly ConcurrentDictionary<ulong, long> _startTimeStamp = new();
        private readonly ConcurrentDictionary<ulong, TimestampAlignedSampleProvider> _userInputs = new();

        private readonly CancellationToken _cancellationToken;
        private readonly WaveFormat _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        private MixingSampleProvider? _mixer;

        public DiscordAudioMixer(ILogger<DiscordAudioMixer> logger, CancellationToken cancellationToken)
        {
            _logger = logger;
            _cancellationToken = cancellationToken;
        }

        public UserAudioBuffer AddUserStream(ulong userId)
        {
            var buffer = new UserAudioBuffer();
            _userBuffers[userId] = buffer;
            _userInputs[userId] = new TimestampAlignedSampleProvider(buffer, _waveFormat);
            if (_mixer != null)
            {
                _mixer.AddMixerInput(_userInputs[userId]);
            }
            return _userBuffers[userId];
        }

        public void RemoveUserStream(ulong userId)
        {
            _userBuffers.TryRemove(userId, out var buffer);
            _startTimeStamp.TryRemove(userId, out var timeStamp);
            if (_mixer != null && _userInputs.TryRemove(userId, out var input))
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

        public void FeedUserFrameAsync(ulong userId, byte[] opusPayload, long rtpTimestamp)
        {
            if (_mixer != null)
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

                buffer?.AddFrame(new TimestampedFrame
                {
                    SampleTimestamp = (rtpTimestamp - userBaseRtpTimestamp) + 960,
                    Samples = floatSamples
                });
            }
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
        public async Task DiscordToVoIp(IAudioClient audioClient, RTPSession mediaSession)
        {
            _mixer = new MixingSampleProvider(_waveFormat) { ReadFully = true };

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
                    if (mediaSession.IsAudioStarted && !mediaSession.IsClosed)
                    {
                        mediaSession.SendAudio((uint)ulawBytes.rtpDuration, ulawBytes.ulawBytes);
                    }
                    else
                    {
                        await Task.Delay(10);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("Call audio send " + ex.Message);
                }

                iteration++;

                // Target time for next frame in ms
                long targetNextFrameTime = iteration * targetMs;

                // Time to sleep (how much until the next frame should be sent)
                long delay = targetNextFrameTime - stopwatch.ElapsedMilliseconds;

                if (delay > 0)
                {
                    await Task.Delay((int)delay, _cancellationToken);
                }
                else
                {
                    _logger.LogWarning($"Audio loop running behind by {-delay}ms");
                }
            }
        }

        private async Task PlayAudioToDiscord(AudioOutStream audioStream, byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using (var reader = new WaveFileReader(ms))
            {
                byte[] buffer = new byte[3840]; // 20ms of 48kHz 16-bit stereo PCM
                int bytesRead;

                while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    await audioStream.WriteAsync(buffer, 0, bytesRead, _cancellationToken);
                    await Task.Delay(20);
                }
            }
        }

        private async Task BridgeVoIpToDiscord(AudioOutStream audioStream, RTPSession mediaSession)
        {
            int discordFrameSizeSamples = 960;

            var waveFormat8kMono = WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, 8000, 1, 16000, 2, 16);

            var bufferedWaveProvider = new BufferedWaveProvider(waveFormat8kMono)
            {
                DiscardOnBufferOverflow = true,
                BufferLength = waveFormat8kMono.AverageBytesPerSecond * 5 // 5 sec buffer max
            };

            var resampler = new WdlResamplingSampleProvider(bufferedWaveProvider.ToSampleProvider(), 48000);

            byte[] discordBuffer = new byte[discordFrameSizeSamples * 2 * 2];

            mediaSession.OnAudioFrameReceived += (frame) =>
            {
                var decodedPcmBytes = DecodeMuLaw(frame.EncodedAudio);
                bufferedWaveProvider.AddSamples(decodedPcmBytes, 0, decodedPcmBytes.Length);
            };

            float[] floatBuffer = new float[discordFrameSizeSamples];
            int samplesRead = 0;

            while (!_cancellationToken.IsCancellationRequested)
            {
                samplesRead = resampler.Read(floatBuffer, 0, discordFrameSizeSamples);

                if (samplesRead < discordFrameSizeSamples)
                {
                    // Not enough data yet for a full Discord frame - break and wait for more input
                    await Task.Delay(20);
                }

                for (int i = 0; i < discordFrameSizeSamples; i++)
                {
                    short sample = FloatToPcm16(floatBuffer[i]);
                    int byteIndex = i * 4;
                    discordBuffer[byteIndex] = (byte)(sample & 0xFF);
                    discordBuffer[byteIndex + 1] = (byte)(sample >> 8);
                    discordBuffer[byteIndex + 2] = (byte)(sample & 0xFF);
                    discordBuffer[byteIndex + 3] = (byte)(sample >> 8);
                }

                try
                {
                    await audioStream.WriteAsync(discordBuffer, 0, discordBuffer.Length, _cancellationToken);
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogInformation("Discord audio send " + ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Discord audio send " + ex.Message);
                }
            }
        }

        public async Task VoIpToDiscord(IAudioClient audioClient, SIPUserAgent userAgent, SIPServerUserAgent? serverUserAgent, RTPSession mediaSession)
        {

            using (var discordStream = audioClient.CreatePCMStream(AudioApplication.Voice, bufferMillis: 20))
            {
                //Play whatever on discord before accept
                if (serverUserAgent != null)
                {
                    await userAgent.Answer(serverUserAgent, mediaSession);
                }

                await BridgeVoIpToDiscord(discordStream, mediaSession);
            }
        }

        private byte[] DecodeMuLaw(byte[] muLawBytes)
        {
            short[] pcmSamples = new short[muLawBytes.Length];
            for (int i = 0; i < muLawBytes.Length; i++)
            {
                pcmSamples[i] = MuLawDecoder.MuLawToLinearSample(muLawBytes[i]);
            }
            byte[] pcmBytes = new byte[pcmSamples.Length * 2];
            Buffer.BlockCopy(pcmSamples, 0, pcmBytes, 0, pcmBytes.Length);
            return pcmBytes;
        }

        private byte[] DecodeALaw(byte[] aLawBytes)
        {
            short[] pcmSamples = new short[aLawBytes.Length];
            for (int i = 0; i < aLawBytes.Length; i++)
            {
                pcmSamples[i] = ALawDecoder.ALawToLinearSample(aLawBytes[i]);
            }
            byte[] pcmBytes = new byte[pcmSamples.Length * 2];
            Buffer.BlockCopy(pcmSamples, 0, pcmBytes, 0, pcmBytes.Length);
            return pcmBytes;
        }

        private short FloatToPcm16(float sample)
        {
            sample = Math.Clamp(sample, -1f, 1f);
            return (short)(sample * short.MaxValue);
        }
    }
}
