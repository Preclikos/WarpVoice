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

namespace WarpVoice.HostedServices
{
    public class VoIPStartupService : IHostedService
    {
        private readonly DiscordSocketClient _discord;
        private readonly VoIPOptions _voIPOptions;

        private readonly ILogger<DiscordSocketClient> _logger;
        private readonly SIPRegistrationUserAgent _registrationUserAgent;

        public VoIPStartupService(ILogger<DiscordSocketClient> logger, IOptions<VoIPOptions> voIPOptions, ISipService sipService, ISessionManager sessionManager)
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
            userAgent.ServerCallCancelled += (uas, cancelReq) => logger.LogDebug("Incoming call cancelled by remote party.");
            userAgent.OnIncomingCall += async (ua, req) =>
            {
                var mediaSession = new RTPSession(false, false, false);
                mediaSession.addTrack(new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMU));

                var uas = userAgent.AcceptCall(req);

                if (sessionManager.CanStartSession(_voIPOptions.GuildId))
                {
                    await userAgent.Answer(uas, mediaSession);
                    await sessionManager.StartSession(_voIPOptions.GuildId, _voIPOptions.MessageChannelId, _voIPOptions.VoiceChannelId, userAgent, mediaSession, null);
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
                    Console.WriteLine("✅ Responded to OPTIONS ping.");
                }
            };

            _registrationUserAgent.RegistrationFailed += (uri, resp, err) =>
            {
                Console.WriteLine($"❌ Registration failed: {resp?.StatusCode} {resp?.ReasonPhrase} | {err}");
            };

            _registrationUserAgent.RegistrationSuccessful += (uri, response) =>
            {
                Console.WriteLine("✅ Registration succeeded.");
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
