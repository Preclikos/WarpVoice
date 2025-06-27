using Discord.Audio;
using NAudio.Codecs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using System.Collections.Concurrent;
using System.Diagnostics;
using WarpVoice.Converters;
using WarpVoice.Options;
using WarpVoice.TTS;

namespace WarpVoice.Audio
{
    public class DiscordAudioMixer
    {
        private readonly ILogger<DiscordAudioMixer> _logger;

        private readonly ConcurrentDictionary<ulong, UserAudioBuffer> _userBuffers = new();
        private readonly ConcurrentDictionary<ulong, long> _startTimeStamp = new();
        private readonly ConcurrentDictionary<ulong, TimestampAlignedSampleProvider> _userInputs = new();

        private readonly CancellationToken _cancellationToken;
        private readonly TTSOptions _ttsOptions;
        private readonly PiperTTS _piper;
        private readonly WaveFormat _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        private MixingSampleProvider? _mixer;

        public DiscordAudioMixer(ILogger<DiscordAudioMixer> logger, TTSOptions ttsOptions, CancellationToken cancellationToken)
        {
            _logger = logger;
            _cancellationToken = cancellationToken;
            _ttsOptions = ttsOptions;
            _piper = new PiperTTS(ttsOptions.PiperPath, ttsOptions.PiperModel);
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

                var floatSamples = WaveConverter.ConvertPcm16ToFloat(opusPayload);

                buffer?.AddFrame(new TimestampedFrame
                {
                    SampleTimestamp = (rtpTimestamp - userBaseRtpTimestamp) + 960,
                    Samples = floatSamples
                });
            }
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

        public async Task DiscordToVoIp(IAudioClient audioClient, RTPSession mediaSession)
        {
            _mixer = new MixingSampleProvider(_waveFormat) { ReadFully = true };

            var targetMs = 20;
            var buffer = new float[960 * 2]; // 20ms stereo float at 48kHz
            DateTime startTime = DateTime.UtcNow;
            Stopwatch stopwatch = Stopwatch.StartNew();
            int iteration = 0;

            while (!_cancellationToken.IsCancellationRequested)
            {
                var iterationStartTime = stopwatch.ElapsedMilliseconds;

                int read = _mixer.Read(buffer, 0, buffer.Length);
                var ulawBytes = MuLawConverter.ConvertFloatToMuLawWithDuration(buffer, 48000, 2);

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

                var targetNextFrameTime = iteration * targetMs;
                var delay = targetNextFrameTime - (int)stopwatch.ElapsedMilliseconds;

                if (delay > 0)
                {
                    await Task.Delay(delay, _cancellationToken);
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
                var decodedPcmBytes = MuLawConverter.DecodeMuLaw(frame.EncodedAudio);
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
                    short sample = WaveConverter.FloatToPcm16(floatBuffer[i]);
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

        public async Task VoIpToDiscord(IAudioClient audioClient, SIPUserAgent userAgent, SIPServerUserAgent? serverUserAgent, RTPSession mediaSession, string number)
        {
            using (var discordStream = audioClient.CreatePCMStream(AudioApplication.Voice, bufferMillis: 20))
            {
                //Play whatever on discord before accept
                if (serverUserAgent != null)
                {
                    if (_ttsOptions.Enabled)
                    {
                        var data = _piper.Synthesize("Receiving call from:" + number);
                        await PlayAudioToDiscord(discordStream, data);
                    }
                    await userAgent.Answer(serverUserAgent, mediaSession);
                }

                await BridgeVoIpToDiscord(discordStream, mediaSession);
            }
        }
    }
}
