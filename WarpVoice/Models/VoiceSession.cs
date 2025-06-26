using Discord;
using Discord.Audio;
using Discord.WebSocket;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using WarpVoice.Services;

namespace WarpVoice.Models
{
    public class VoiceSession
    {
        public ulong GuildId { get; set; }

        public ISocketMessageChannel MessageChannel { get; set; }
        public IVoiceChannel VoiceChannel { get; set; }
        public IAudioClient AudioClient { get; set; }

        public DiscordUsersVoice DiscordVoiceManager { get; set; }

        public SIPUserAgent SIPUserAgent { get; set; }
        public (SIPCallFailedDelegate callFailedHandler, Action<SIPDialogue> hungupHandler) SIPAgentEvents { get;set; }

        public RTPSession MediaSession { get; set; }


        public bool IsSipInUse { get; set; }
        public DateTime StartedAt { get; set; }
    }
}
