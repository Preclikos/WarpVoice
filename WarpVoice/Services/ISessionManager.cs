using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using WarpVoice.Enums;
using WarpVoice.Models;

namespace WarpVoice.Services
{
    public interface ISessionManager
    {
        bool CanStartSession(ulong guildId);
        //bool StartSession(ulong guildId, string channelId);
        Task<bool> StartSession(ulong guildId, ulong messageChannelId, ulong voiceChannelId, SIPUserAgent userAgent, SIPServerUserAgent? serverUserAgent, RTPSession mediaSession, CallDirection direction, string number);
        Task EndSessionVoIp(ulong guildId);
        Task EndSessionDiscord(ulong guildId);
        VoiceSession? GetSession(ulong guildId);
    }
}
