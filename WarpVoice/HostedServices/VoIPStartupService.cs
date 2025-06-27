using Microsoft.Extensions.Options;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using WarpVoice.Enums;
using WarpVoice.Options;
using WarpVoice.Services;

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
                    var sessionResult = await sessionManager.StartSession(_voIPOptions.GuildId, _voIPOptions.MessageChannelId, _voIPOptions.VoiceChannelId, userAgent, mediaSession, CallDirection.Incoming, callerNumber);
                    if (sessionResult)
                    {
                        await userAgent.Answer(uas, mediaSession);
                    }
                    else
                    {
                        uas.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
                    }
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

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _registrationUserAgent.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _registrationUserAgent.Stop();
            return Task.CompletedTask;
        }
    }
}
