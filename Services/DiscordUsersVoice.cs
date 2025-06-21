using Discord;
using Discord.Audio;
using WarpVoice.Audio;

namespace WarpVoice.Services
{
    public class DiscordUsersVoice
    {
        private readonly CancellationTokenSource cancellationToken = new CancellationTokenSource();
        private readonly Dictionary<ulong, UserStreamHandler> _userHandlers = new();
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

        public DiscordUsersVoice(IVoiceChannel voiceChannel, IAudioClient audioClient)
        {
            _mixer = new DiscordAudioMixer(cancellationToken.Token);
            _audioClient = audioClient;

            _audioClient.StreamCreated += StartUserStream;
            _audioClient.StreamDestroyed += StopUserStream;

            var streams = _audioClient.GetStreams();

            // Handle already connected users
            Task.Run(async () =>
            {
                await foreach (var userBatch in voiceChannel.GetUsersAsync())
                {
                    foreach (var member in userBatch)
                    {
                        if (!member.IsBot && !_userHandlers.ContainsKey(member.Id))
                        {
                            try
                            {
                                var stream = streams.Single(s => s.Key == member.Id).Value;
                                await StartUserStream(member.Id, stream);
                                Console.WriteLine($"[INIT] Added user: {member.Username}");
                            }
                            catch
                            {
                                Console.WriteLine($"[WARN] Could not get stream for: {member.Username}");
                            }
                        }
                    }
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
                Console.WriteLine($"[LEAVE] {userId}");
            }

            return Task.CompletedTask;
        }

        private Task StartUserStream(ulong userId, AudioInStream stream)
        {
            Console.WriteLine("New stream for user " + userId);
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken.Token);
            var handler = new UserStreamHandler
            {
                Stream = stream,
                CancellationTokenSource = cts,
                Task = Task.Run(() => HandleUserAudioAsync(userId, stream, cts.Token))
            };

            _userHandlers[userId] = handler;
            Console.WriteLine($"[JOIN] {userId}");
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
                            await _mixer.FeedUserFrameAsync(userId, frame.Payload, frame.Timestamp);
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
                    Console.WriteLine($"[Stream Error] {userId}: {ex.Message}");
                    break;
                }
            }

            Console.WriteLine($"[INFO] Audio stream stopped for user {userId}");
        }
    }
}
