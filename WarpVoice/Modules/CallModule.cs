using WarpVoice.Services;
using Discord;
using Discord.Interactions;
using SIPSorceryMedia.Abstractions;
using SIPSorcery.Net;
using WarpVoice.Options;
using System.Net;
using Microsoft.Extensions.Options;
using WarpVoice.Enums;
namespace WarpVoice.Modules
{
    [RequireContext(ContextType.Guild)]
    public class CallModule : InteractionModuleBase<SocketInteractionContext>
    {

        private readonly ILogger<CallModule> _logger;
        private readonly ISipService _sipService;
        private readonly VoIPOptions _voIpOptions;
        private readonly ISessionManager _sessionManager;

        public CallModule(ILogger<CallModule> logger, ISipService sipService, ISessionManager sessionManager, IOptions<VoIPOptions> voIpOptions)
        {
            _logger = logger;
            _sipService = sipService;
            _voIpOptions = voIpOptions.Value;
            _sessionManager = sessionManager;
        }

        [SlashCommand("hangup", "Disconnect current voice call")]
        public async Task HangUp()
        {
            await DeferAsync().ConfigureAwait(false);
            var session = _sessionManager.GetSession(Context.Guild.Id);
            if (session != null)
            {
                await _sessionManager.EndSession(Context.Guild.Id);
                await FollowupAsync("Call is HangedUp");
            }
            else
            {
                await FollowupAsync("There is no running Call");
            }
        }

        [SlashCommand("call", "Call from the current voice channel connected to")]
        public async Task Call(string number)
        {
            await DeferAsync().ConfigureAwait(false);
            var user = Context.User as IGuildUser;
            var _voiceChannel = user?.VoiceChannel;

            if (_voiceChannel == null)
            {
                await FollowupAsync("You must be in a voice channel.");
                return;
            }

            if (!_sessionManager.CanStartSession(Context.Guild.Id))
            {
                await FollowupAsync("Call is already in progress in this server.");
                return;
            }

            IPAddress localIp = IPAddress.Parse(_voIpOptions.RtpIp);
            int rtpPort = _voIpOptions.RtpPort;

            var mediaSession = new RTPSession(false, false, false, localIp, rtpPort);
            mediaSession.addTrack(new MediaStreamTrack(SDPWellKnownMediaFormatsEnum.PCMU));

            var result = await _sessionManager.StartSession(Context.Guild.Id, Context.Channel.Id, _voiceChannel.Id, _sipService.GetUserAgent(), mediaSession, CallDirection.Outgoing, number);

            if (result)
            {
                await FollowupAsync("Starting call");
            }
            else
            {
                await FollowupAsync("Something failed");
            }
        }
    }
}
