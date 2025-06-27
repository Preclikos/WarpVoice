using Microsoft.Extensions.Options;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using WarpVoice.Options;

namespace WarpVoice.Services
{
    public class SipService : ISipService
    {
        private readonly SIPTransport _sipTransport;
        private readonly SIPUserAgent _userAgent;
        private readonly VoIPOptions _voIpOptions;
        private readonly ILogger<SipService> _logger;

        public SipService(ILogger<SipService> logger, IOptions<VoIPOptions> voIpOptions)
        {
            _voIpOptions = voIpOptions.Value;

            _logger = logger;
            _sipTransport = new SIPTransport();
            _userAgent = new SIPUserAgent(_sipTransport, null);
        }

        public SIPTransport GetTransport()
        {
            return _sipTransport;
        }

        public SIPUserAgent GetUserAgent()
        {
            return _userAgent;
        }

        public async Task<RTPSession> MakeCallAsync(RTPSession mediaSession, string destinationUri)
        {
            var callDescriptor = new SIPCallDescriptor(
                _voIpOptions.UserName,
                _voIpOptions.Password,
                destinationUri,
                $"sip:{_voIpOptions.UserName}@{_voIpOptions.Domain}",
                destinationUri,
                null, null, null,
                SIPCallDirection.Out,
                SDP.SDP_MIME_CONTENTTYPE,
                null,
                null);

            callDescriptor.CallId = CallProperties.CreateNewCallId();

            await _userAgent.InitiateCallAsync(callDescriptor, mediaSession);

            return mediaSession;
        }

        public void Hangup()
        {
            _userAgent.Hangup();
        }
    }
}
