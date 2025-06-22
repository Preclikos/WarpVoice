using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using WarpVoice.Enums;
using WarpVoice.Models;
using WarpVoice.Options;

namespace WarpVoice.Services
{
    public class SessionManager : ISessionManager
    {
        private readonly ConcurrentDictionary<ulong, VoiceSession> _sessions = new();
        private readonly VoIPOptions _voIpOptions;
        private readonly DiscordSocketClient _discord;
        private readonly ISipService _sipService;

        public SessionManager(DiscordSocketClient discord, ISipService sipService, IOptions<VoIPOptions> voIpOptions)
        {
            _voIpOptions = voIpOptions.Value;
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

            var userVoices = new DiscordUsersVoice(voiceChannel, audioClient);

            userVoices.GetMixer().ReceiveSendToDiscord(audioClient, rtpSession);
            Task.Run(async () => { await userVoices.GetMixer().StartMixingLoopAsync(audioClient, rtpSession); });

            var result = number;
            if (number.Length > 8)
            {
                if (number.StartsWith("420"))
                {
                    number = number.Substring(3);
                }

                string pattern = @"(\d{3})$";
                string replacement = "XXX";

                result = Regex.Replace(number, pattern, replacement);
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
            await _discord.SetGameAsync(result, type: ActivityType.Playing);

            //need resolve unregister correctlly
            userAgent.ClientCallFailed += async (uac, errorMessage, sipResponse) => await UserAgent_ClientCallFailed(guildId, uac, errorMessage, sipResponse);
            userAgent.OnCallHungup += async (sIPDialogue) => await UserAgent_OnCallHungup(guildId, sIPDialogue);

            var session = new VoiceSession
            {
                GuildId = guildId,
                MessageChannel = messageChannel,
                VoiceChannel = voiceChannel,
                AudioClient = audioClient,
                DiscordVoiceManager = userVoices,
                SIPUserAgent = userAgent,
                MediaSession = rtpSession,
                IsSipInUse = true,
                StartedAt = DateTime.UtcNow
            };

            return _sessions.TryAdd(guildId, session);
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
            if (_sessions.Remove(guildId, out var session))
            {
                //Single node only
                await _discord.SetGameAsync("/call | /hangup", type: ActivityType.Listening);
                await session.MessageChannel.SendMessageAsync("Hanged up");

                await session.DiscordVoiceManager.Stop();
                await session.VoiceChannel.DisconnectAsync();
            }
        }

        public VoiceSession? GetSession(ulong guildId)
        {
            _sessions.TryGetValue(guildId, out var session);
            return session;
        }
    }
}
