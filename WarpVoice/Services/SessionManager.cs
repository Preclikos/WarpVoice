using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using WarpVoice.Audio;
using WarpVoice.Enums;
using WarpVoice.Models;
using WarpVoice.Options;

namespace WarpVoice.Services
{
    public class SessionManager : ISessionManager
    {
        private readonly ConcurrentDictionary<ulong, VoiceSession> _sessions = new();
        private readonly ILogger<SessionManager> _logger;
        private readonly ILogger<DiscordUsersVoice> _loggerDiscordUsersVoice;
        private readonly ILogger<DiscordAudioMixer> _loggerDiscordAudioMixer;
        private readonly VoIPOptions _voIpOptions;
        private readonly AddressBookOptions _addressBookOptions;
        private readonly DiscordSocketClient _discord;
        private readonly ISipService _sipService;

        public SessionManager(ILogger<SessionManager> logger, ILogger<DiscordUsersVoice> loggerDiscordUsersVoice, ILogger<DiscordAudioMixer> loggerDiscordAudioMixer,
            IOptions<VoIPOptions> voIpOptions, IOptions<AddressBookOptions> addressBookOptions,
            DiscordSocketClient discord, ISipService sipService
            )
        {
            _logger = logger;
            _loggerDiscordUsersVoice = loggerDiscordUsersVoice;
            _loggerDiscordAudioMixer = loggerDiscordAudioMixer;
            _voIpOptions = voIpOptions.Value;
            _addressBookOptions = addressBookOptions.Value;
            _discord = discord;
            _sipService = sipService;
        }

        public bool CanStartSession(ulong guildId)
        {
            if (_sessions.ContainsKey(guildId)) return false;
            if (_sessions.Values.Any(s => s.IsSipInUse)) return false;
            return true;
        }

        public async Task<bool> StartSession(ulong guildId, ulong messageChannelId, ulong voiceChannelId, SIPUserAgent userAgent, RTPSession rtpSession, CallDirection direction, string number)
        {
            if (!CanStartSession(guildId)) return false;

            var guild = _discord.GetGuild(guildId);

            var messageChannel = guild.GetTextChannel(messageChannelId);
            var voiceChannel = guild.GetVoiceChannel(voiceChannelId);

            var audioClient = await voiceChannel.ConnectAsync();
            var userVoices = new DiscordUsersVoice(_loggerDiscordUsersVoice, _loggerDiscordAudioMixer, voiceChannel, audioClient);

            var result = number;
            if (number.Length > 8)
            {
                if (number.StartsWith("420"))
                {
                    number = number.Substring(3);
                }

                string pattern = @"(\d{3})$";
                string replacement = "XXX";

                result = _addressBookOptions.NameNumbers.Any(a => a.Value == number) ?
                    _addressBookOptions.NameNumbers.SingleOrDefault(a => a.Value == number).Key :
                    Regex.Replace(number, pattern, replacement);
            }

            if (direction == CallDirection.Outgoing)
            {
                var destinationUri = $"sip:{number}@{_voIpOptions.Domain}";
                await _sipService.MakeCallAsync(rtpSession, destinationUri);

                await messageChannel.SendMessageAsync($"Call to: {result} was started");
            }
            else
            {
                await messageChannel.SendMessageAsync($"Receiving call from: {result}");
            }

            //Single node only
            //await _discord.SetGameAsync(result, type: ActivityType.Playing); //long time needed :( remove it or maybe other

            SIPCallFailedDelegate callFailedHandler = async (uac, errorMessage, sipResponse) => await UserAgent_ClientCallFailed(guildId, uac, errorMessage, sipResponse);
            Action<SIPDialogue> hungupHandler = async (sIPDialogue) => await UserAgent_OnCallHungup(guildId, sIPDialogue);

            userAgent.ClientCallFailed += callFailedHandler;
            userAgent.OnCallHungup += hungupHandler;

            var session = new VoiceSession
            {
                GuildId = guildId,
                MessageChannel = messageChannel,
                VoiceChannel = voiceChannel,
                AudioClient = audioClient,
                DiscordVoiceManager = userVoices,
                SIPUserAgent = userAgent,
                SIPAgentEvents = (callFailedHandler, hungupHandler),
                MediaSession = rtpSession,
                IsSipInUse = true,
                StartedAt = DateTime.UtcNow
            };

            if (_sessions.TryAdd(guildId, session))
            {
                _logger.LogInformation("Session added");
                _ = Task.Run(async () => { await userVoices.GetMixer().ReceiveSendToDiscord(audioClient, rtpSession); });
                _ = Task.Run(async () => { await userVoices.GetMixer().StartMixingLoopAsync(audioClient, rtpSession); });
                return true;
            }

            return false;
        }

        private async Task UserAgent_OnCallHungup(ulong guildId, SIPDialogue sIPDialogue)
        {
            await EndSessionInternal(guildId);
        }

        private async Task UserAgent_ClientCallFailed(ulong guildId, ISIPClientUserAgent uac, string errorMessage, SIPResponse sipResponse)
        {
            await EndSessionInternal(guildId);
        }

        public Task EndSession(ulong guildId)
        {
            if (_sessions.TryGetValue(guildId, out var session))
            {
                if (session.SIPUserAgent.IsCallActive)
                {
                    session.SIPUserAgent.Hangup();
                }
                else
                {
                    session.SIPUserAgent.Cancel();
                }
            }
            return Task.CompletedTask;
        }

        private async Task EndSessionInternal(ulong guildId)
        {
            if (_sessions.TryGetValue(guildId, out var session))
            {
                await session.DiscordVoiceManager.Stop();
                await session.VoiceChannel.DisconnectAsync();

                session.SIPUserAgent.ClientCallFailed -= session.SIPAgentEvents.callFailedHandler;
                session.SIPUserAgent.OnCallHungup -= session.SIPAgentEvents.hungupHandler;

                //Single node only
                //await _discord.SetGameAsync("/call | /hangup", type: ActivityType.Listening); //long time needed :( remove it or maybe other
                await session.MessageChannel.SendMessageAsync("Hanged up");

                if (!_sessions.Remove(guildId, out var _))
                {
                    _logger.LogError("End session no session!!!!!!!!!!");
                }
            }
            else
            {
                _logger.LogInformation("End session no session!!!!!!!!!!");
            }
        }

        public VoiceSession? GetSession(ulong guildId)
        {
            _sessions.TryGetValue(guildId, out var session);
            return session;
        }
    }
}
