using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;
using System.Net;
using SIPSorceryMedia.Abstractions;
using NAudio.Wave;
using WarpVoice.Options;
using Microsoft.Extensions.Options;

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
            //EnableTraceLogs(_sipTransport);
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

        private void EnableTraceLogs(SIPTransport sipTransport)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Console.WriteLine($"Request received: {localEP}<-{remoteEP}");
                Console.WriteLine(req.ToString());
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Console.WriteLine($"Request sent: {localEP}->{remoteEP}");
                Console.WriteLine(req.ToString());
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Console.WriteLine($"Response received: {localEP}<-{remoteEP}");
                Console.WriteLine(resp.ToString());
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Console.WriteLine($"Response sent: {localEP}->{remoteEP}");
                Console.WriteLine(resp.ToString());
            };

            sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                Console.WriteLine($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds:0.###}s ago.");
            };

            sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                Console.WriteLine($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds:0.###}s ago.");
            };
        }

        public void Hangup()
        {
            _userAgent.Hangup();
        }
    }
}
