using Discord;
using Discord.Audio;
using Discord.WebSocket;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using WarpVoice.Services;

namespace WarpVoice.Models
{
    public class VoiceSession(ulong guildId, SIPUserAgent sIPUserAgent, RTPSession mediaSession)
    {

        public ulong GuildId { get; } = guildId;

        public ISocketMessageChannel? MessageChannel { get; set; }
        public IVoiceChannel? VoiceChannel { get; set; }
        public IAudioClient? AudioClient { get; set; }

        public DiscordUsersVoice? DiscordVoiceManager { get; set; }

        public SIPUserAgent SIPUserAgent { get; } = sIPUserAgent;
        public (SIPCallFailedDelegate callFailedHandler, Action<SIPDialogue> hungupHandler, Action<SDPMediaTypesEnum> sessionOnTimeout) SIPAgentEvents { get; set; }
        public RTPSession MediaSession { get; } = mediaSession;


        public bool IsSipInUse { get; private set; } = true;
        public DateTime StartedAt { get; private set; } = DateTime.UtcNow;
    }
}
