using Discord;
using Discord.Audio;
using WarpVoice.Audio;
using WarpVoice.Options;

namespace WarpVoice.Services
{
    public class DiscordUsersVoice
    {
        private readonly CancellationTokenSource cancellationToken = new CancellationTokenSource();
        private readonly Dictionary<ulong, UserStreamHandler> _userHandlers = new();
        private readonly ILogger<DiscordUsersVoice> _logger;
        private readonly ILogger<DiscordAudioMixer> _loggerDiscordAudioMixer;
        private DiscordAudioMixer _mixer;
        private readonly IAudioClient _audioClient;

        private class UserStreamHandler
        {
            public AudioInStream? Stream { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; } = new CancellationTokenSource();
            public Task? Task { get; set; }
        }

        public async Task Stop()
        {
            await cancellationToken.CancelAsync();
        }

        public DiscordAudioMixer GetMixer()
        {
            return _mixer;
        }


        public async Task<List<IGuildUser>> FlattenAsyncEnumerable(
    IAsyncEnumerable<IReadOnlyCollection<IGuildUser>> asyncEnumerable)
        {
            var result = new List<IGuildUser>();

            await foreach (var group in asyncEnumerable)
            {
                result.AddRange(group); // Flatten each collection into the result list
            }

            return result;
        }

        public DiscordUsersVoice(ILogger<DiscordUsersVoice> logger, ILogger<DiscordAudioMixer> loggerDiscordAudioMixer, TTSOptions ttsOptions, VoIPOptions voIPOptions, IVoiceChannel voiceChannel, IAudioClient audioClient)
        {
            _logger = logger;
            _loggerDiscordAudioMixer = loggerDiscordAudioMixer;
            _mixer = new DiscordAudioMixer(_loggerDiscordAudioMixer, ttsOptions, voIPOptions, cancellationToken.Token);
            _audioClient = audioClient;

            _audioClient.StreamCreated += StartUserStream;
            _audioClient.StreamDestroyed += StopUserStream;

            var streams = _audioClient.GetStreams();

            // Handle already connected users
            Task.Run(async () =>
            {
                var users = await FlattenAsyncEnumerable(((IVoiceChannel)voiceChannel).GetUsersAsync());
                var usersInVoiceChannel = users.Where(w => w.VoiceChannel != null && w.VoiceChannel.Id == voiceChannel.Id && !w.IsBot);
                foreach (var member in usersInVoiceChannel)
                {
                    /*foreach (var member in userBatch)
                    {*/
                    if (!member.IsBot && !_userHandlers.ContainsKey(member.Id))
                    {
                        try
                        {
                            var stream = streams.Single(s => s.Key == member.Id).Value;
                            await StartUserStream(member.Id, stream);
                            logger.LogInformation($"[INIT] Added user: {member.Username}");
                        }
                        catch
                        {
                            logger.LogWarning($"Could not get stream for: {member.Username}");
                        }
                    }
                    //}
                }
            }).Wait();

        }

        private Task StopUserStream(ulong userId)
        {
            if (_userHandlers.TryGetValue(userId, out var handler))
            {
                handler.CancellationTokenSource.Cancel();
                _userHandlers.Remove(userId);
                _mixer.RemoveUserStream(userId);
                _logger.LogInformation($"[LEAVE] {userId}");
            }

            return Task.CompletedTask;
        }

        private Task StartUserStream(ulong userId, AudioInStream stream)
        {
            _logger.LogInformation("New stream for user " + userId);
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken.Token);
            var handler = new UserStreamHandler
            {
                Stream = stream,
                CancellationTokenSource = cts,
                Task = Task.Run(() => HandleUserAudioAsync(userId, stream, cts.Token))
            };

            _userHandlers[userId] = handler;
            _logger.LogInformation($"[JOIN] {userId}");
            return Task.CompletedTask;
        }

        private async Task HandleUserAudioAsync(ulong userId, AudioInStream stream, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (stream.AvailableFrames > 0)
                    {
                        var frame = await stream.ReadFrameAsync(token);
                        if (frame.Payload.Length > 0)
                        {
                            _mixer.FeedUserFrameAsync(userId, frame.Payload, frame.Timestamp);
                        }
                    }
                    else
                    {
                        await Task.Delay(20);
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // Graceful shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Stream {userId}: {ex.Message}");
                    break;
                }
            }

            _logger.LogInformation($"Audio stream stopped for user {userId}");
        }
    }
}
