using WarpVoice.Options;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using SIPSorcery.SIP.App;
using SIPSorcery.SIP;
using SIPSorcery.Net;
using WarpVoice.Services;
using System.Net;
using System;
using SIPSorceryMedia.Abstractions;
using WarpVoice.Enums;

namespace WarpVoice.HostedServices
{
    public class VoIPStartupService : IHostedService
    {
        private readonly VoIPOptions _voIPOptions;

        private readonly ILogger<VoIPStartupService> _logger;
        private readonly SIPRegistrationUserAgent _registrationUserAgent;

        public VoIPStartupService(ILogger<VoIPStartupService> logger, IOptions<VoIPOptions> voIPOptions, ISipService sipService, ISessionManager sessionManager)
        {
            _voIPOptions = voIPOptions.Value;
            _logger = logger;

            var sipTransport = sipService.GetTransport();

            _registrationUserAgent = new SIPRegistrationUserAgent(
                sipTransport,
                _voIPOptions.UserName,
                _voIPOptions.Password,
                _voIPOptions.Domain,
                300);

            var userAgent = sipService.GetUserAgent();
            userAgent.ServerCallCancelled += (uas, cancelReq) => logger.LogInformation("Incoming call cancelled by remote party.");
            userAgent.OnIncomingCall += async (ua, req) =>
            {
                var mediaSession = new RTPSession(false, false, false);
                mediaSession.addTrack(new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMU));

                var uas = userAgent.AcceptCall(req);

                if (sessionManager.CanStartSession(_voIPOptions.GuildId))
                {
                    var callerNumber = SIPURI.ParseSIPURIRelaxed(req.Header.From.FromURI.ToString()).User;
                    await userAgent.Answer(uas, mediaSession);
                    await sessionManager.StartSession(_voIPOptions.GuildId, _voIPOptions.MessageChannelId, _voIPOptions.VoiceChannelId, userAgent, mediaSession, CallDirection.Incoming, callerNumber);
                }
                else
                {
                    uas.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
                }
            };

            sipTransport.SIPTransportRequestReceived += async (localEndPoint, remoteEndPoint, sipRequest) =>
            {
                if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                {
                    var okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await sipTransport.SendResponseAsync(okResponse);
                    logger.LogInformation("✅ Responded to OPTIONS ping.");
                }
            };

            _registrationUserAgent.RegistrationFailed += (uri, resp, err) =>
            {
                logger.LogError($"❌ Registration failed: {resp?.StatusCode} {resp?.ReasonPhrase} | {err}");
            };

            _registrationUserAgent.RegistrationSuccessful += (uri, response) =>
            {
                logger.LogInformation("✅ Registration succeeded.");
            };
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _registrationUserAgent.Start();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _registrationUserAgent.Stop();
        }
    }
}
