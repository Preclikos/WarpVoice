using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorcery.SIP;

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
