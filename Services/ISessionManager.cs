using WarpVoice.Models;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using WarpVoice.Enums;

namespace WarpVoice.Services
{
    public interface ISessionManager
    {
        bool CanStartSession(ulong guildId);
        //bool StartSession(ulong guildId, string channelId);
        Task<bool> StartSession(ulong guildId, ulong messageChannelId, ulong voiceChannelId, SIPUserAgent userAgent, RTPSession mediaSession, CallDirection direction, string number);
        Task EndSession(ulong guildId);
        VoiceSession? GetSession(ulong guildId);
    }
}
