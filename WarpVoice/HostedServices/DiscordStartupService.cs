using WarpVoice.Options;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;

namespace WarpVoice.HostedServices
{
    public class DiscordStartupService : IHostedService
    {
        private readonly DiscordSocketClient _discord;
        private readonly DiscordOptions _discordOptions;

        private readonly ILogger<DiscordSocketClient> _logger;

        public DiscordStartupService(DiscordSocketClient discord, IOptions<DiscordOptions> discordOptions, ILogger<DiscordSocketClient> logger)
        {
            _discord = discord;
            _discordOptions = discordOptions.Value;
            _logger = logger;


            _discord.Log += msg => LogHelper.OnLogAsync(_logger, msg);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _discord.LoginAsync(TokenType.Bot, _discordOptions.Token);
            await _discord.StartAsync();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await _discord.LogoutAsync();
            await _discord.StopAsync();
        }
    }
}
