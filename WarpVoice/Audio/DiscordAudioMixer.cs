using Discord.Audio;
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
        private readonly VoIPOptions _voIPOptions;
        private readonly PiperTTS _piper;
        private readonly WaveFormat _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        private MixingSampleProvider? _mixer;

        public DiscordAudioMixer(ILogger<DiscordAudioMixer> logger, TTSOptions ttsOptions, VoIPOptions voIPOptions, CancellationToken cancellationToken)
        {
            _logger = logger;
            _cancellationToken = cancellationToken;
            _ttsOptions = ttsOptions;
            _piper = new PiperTTS(ttsOptions.PiperPath, ttsOptions.PiperModel);
            _voIPOptions = voIPOptions;
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

        public async Task DiscordToVoIp(IAudioClient audioClient, RTPSession mediaSession)
        {
            _mixer = new MixingSampleProvider(_waveFormat) { ReadFully = true };

            await Task.Delay(40, _cancellationToken); // Initial sync delay

            const int sampleRate = 48000;
            const int channels = 2;
            const int frameDurationMs = 20;
            const int samplesPerFrame = sampleRate * frameDurationMs / 1000; // 960 samples
            const int floatBufferSize = samplesPerFrame * channels;

            float[] buffer = new float[floatBufferSize];

            var stopwatch = Stopwatch.StartNew();
            long nextFrameTime = stopwatch.ElapsedMilliseconds;

            while (!_cancellationToken.IsCancellationRequested)
            {
                int read = _mixer.Read(buffer, 0, floatBufferSize);

                if (read > 0)
                {
                    var alawBytes = ALawConverter.ConvertFloatToALawWithDuration(buffer, sampleRate, channels, _voIPOptions.SipGain);

                    try
                    {
                        if (mediaSession.IsAudioStarted && !mediaSession.IsClosed)
                        {
                            mediaSession.SendAudio((uint)alawBytes.rtpDuration, alawBytes.alawBytes);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Call audio send error: {ex.Message}");
                    }
                }

                nextFrameTime += frameDurationMs;
                long delay = nextFrameTime - stopwatch.ElapsedMilliseconds;

                if (delay > 0)
                {
                    await Task.Delay((int)delay, _cancellationToken);
                }
                else
                {
                    _logger.LogWarning($"Audio loop behind by {-delay}ms");
                    // We're behind — calculate how many frames behind and skip mixer data
                    int missedFrames = (int)(-delay / frameDurationMs);

                    if (missedFrames > 0)
                    {
                        _logger.LogWarning($"Audio loop behind by {-delay}ms, skipping {missedFrames} frames");

                        int samplesToSkip = missedFrames * samplesPerFrame * channels;
                        float[] skipBuffer = new float[samplesToSkip];

                        int skipped = 0;
                        while (skipped < samplesToSkip)
                        {
                            int s = _mixer.Read(skipBuffer, skipped, samplesToSkip - skipped);
                            if (s <= 0) break;
                            skipped += s;
                        }

                        nextFrameTime += missedFrames * frameDurationMs;
                    }
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
            const int discordFrameSizeSamples = 960;
            const int discordSampleRate = 48000;
            const int inputSampleRate = 8000;

            var waveFormat8kMono = WaveFormat.CreateCustomFormat(WaveFormatEncoding.Pcm, inputSampleRate, 1, inputSampleRate * 2, 2, 16);

            var bufferedWaveProvider = new BufferedWaveProvider(waveFormat8kMono)
            {
                DiscardOnBufferOverflow = true,
                BufferLength = waveFormat8kMono.AverageBytesPerSecond * 5
            };

            var resampler = new WdlResamplingSampleProvider(bufferedWaveProvider.ToSampleProvider(), discordSampleRate);

            byte[] discordBuffer = new byte[discordFrameSizeSamples * 2 * 2]; // 16-bit stereo
            float[] floatBuffer = new float[discordFrameSizeSamples];

            mediaSession.OnAudioFrameReceived += (frame) =>
            {
                var decodedPcmBytes = ALawConverter.DecodeALaw(frame.EncodedAudio);
                bufferedWaveProvider.AddSamples(decodedPcmBytes, 0, decodedPcmBytes.Length);
            };

            try
            {
                while (!_cancellationToken.IsCancellationRequested)
                {
                    int samplesRead = resampler.Read(floatBuffer, 0, discordFrameSizeSamples);

                    if (samplesRead < discordFrameSizeSamples)
                    {
                        await Task.Delay(5, _cancellationToken); // slightly shorter delay
                        continue;
                    }

                    // Fill Discord buffer with stereo interleaved 16-bit PCM
                    Span<byte> discordSpan = discordBuffer;
                    for (int i = 0; i < discordFrameSizeSamples; i++)
                    {
                        // Apply gain to the float sample
                        float amplified = floatBuffer[i] * _voIPOptions.DiscordGain;

                        // Clamp to avoid clipping
                        amplified = Math.Clamp(amplified, -1.0f, 1.0f);

                        // Convert to PCM 16-bit
                        short sample = WaveConverter.FloatToPcm16(amplified);

                        // Write to both left and right (stereo)
                        int offset = i * 4;
                        discordSpan[offset] = discordSpan[offset + 2] = (byte)(sample & 0xFF);
                        discordSpan[offset + 1] = discordSpan[offset + 3] = (byte)(sample >> 8);
                    }

                    await audioStream.WriteAsync(discordBuffer, 0, discordBuffer.Length, _cancellationToken);
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogInformation("Discord audio stream canceled: " + ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error writing to Discord audio stream: " + ex.Message);
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
                        var data = _piper.Synthesize("Receiving call from " + number);
                        await PlayAudioToDiscord(discordStream, data);
                    }
                    await userAgent.Answer(serverUserAgent, mediaSession);
                }

                await BridgeVoIpToDiscord(discordStream, mediaSession);
            }
        }
    }
}
