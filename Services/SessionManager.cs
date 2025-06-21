using Discord.WebSocket;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using System.Collections.Concurrent;
using WarpVoice.Models;

namespace WarpVoice.Services
{
    public class SessionManager : ISessionManager
    {
        private readonly ConcurrentDictionary<ulong, VoiceSession> _sessions = new();
        private readonly DiscordSocketClient _discord;
        private readonly ISipService _sipService;

        public SessionManager(DiscordSocketClient discord, ISipService sipService)
        {
            _discord = discord;
            _sipService = sipService;
        }

        public bool CanStartSession(ulong guildId)
        {
            if (_sessions.ContainsKey(guildId)) return false;
            if (_sessions.Values.Any(s => s.IsSipInUse)) return false;
            return true;
        }

        public async Task<bool> StartSession(ulong guildId, ulong messageChannelId, ulong voiceChannelId, SIPUserAgent userAgent, RTPSession rtpSession, string number)
        {
            if (!CanStartSession(guildId)) return false;

            var guild = _discord.GetGuild(guildId);

            var messageChannel = guild.GetTextChannel(messageChannelId);
            var voiceChannel = guild.GetVoiceChannel(voiceChannelId);

            var audioClient = await voiceChannel.ConnectAsync();

            var userVoices = new DiscordUsersVoice(voiceChannel, audioClient);

            userVoices.GetMixer().ReceiveSendToDiscord(audioClient, rtpSession);
            Task.Run(async () => { await userVoices.GetMixer().StartMixingLoopAsync(audioClient, rtpSession); });

            if (number != null)
            {
                await _sipService.MakeCallAsync(rtpSession, number);
            }

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
            await EndSession(guildId);
        }

        private async Task UserAgent_ClientCallFailed(ulong guildId, ISIPClientUserAgent uac, string errorMessage, SIPResponse sipResponse)
        {
            await EndSession(guildId);
        }

        public async Task<bool> EndSession(ulong guildId)
        {
            if (_sessions.TryGetValue(guildId, out var session))
            {
                if (session.SIPUserAgent.IsCallActive && !session.MediaSession.IsClosed)
                {
                    session.SIPUserAgent.Hangup();
                }
                await session.DiscordVoiceManager.Stop();
                await session.VoiceChannel.DisconnectAsync();

                _sessions.Remove(guildId, out _);

                return true;
            }

            return false; 

        }

        public VoiceSession? GetSession(ulong guildId)
        {
            _sessions.TryGetValue(guildId, out var session);
            return session;
        }
    }
}
