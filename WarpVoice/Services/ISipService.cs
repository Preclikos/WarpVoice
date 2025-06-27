using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace WarpVoice.Services
{
    public interface ISipService
    {
        SIPTransport GetTransport();
        SIPUserAgent GetUserAgent();

        Task<RTPSession> MakeCallAsync(RTPSession rTPSession, string destinationUri);
        void Hangup();
    }
}
