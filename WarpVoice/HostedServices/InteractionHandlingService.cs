using Discord.Interactions;
using Discord.WebSocket;
using Discord;
using System.Reflection;

namespace WarpVoice.HostedServices
{
    public class InteractionHandlingService : IHostedService
    {
        private readonly DiscordSocketClient _discord;
        private readonly InteractionService _interactions;
        //private readonly CommandService _commands;
        private readonly IServiceProvider _services;
        private readonly ILogger<InteractionService> _logger;
        //private readonly LavaNode<LavaPlayer<LavaTrack>, LavaTrack> _lavaNode;

        public InteractionHandlingService(
            DiscordSocketClient discord,
            InteractionService interactions,
            //CommandService commands,
            IServiceProvider services,
            //LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode,
            ILogger<InteractionService> logger)
        {
            _discord = discord;
            _interactions = interactions;
            //_commands = commands;
            _services = services;
            //_lavaNode = lavaNode;
            _logger = logger;

            _interactions.Log += msg => LogHelper.OnLogAsync(_logger, msg);
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await _interactions.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            //await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            _discord.InteractionCreated += OnInteractionAsync;
            //_discord.MessageReceived += OnCommandAsync;
            _discord.Ready += async () => {
                await _interactions.RegisterCommandsGloballyAsync();
                //await _interactions.RegisterCommandsToGuildAsync(900693605612654622);
                await _discord.SetGameAsync("/call | /hangup", type: ActivityType.Listening);
            };
            //_discord.Ready += () => _services.UseLavaNodeAsync();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _interactions.Dispose();
            return Task.CompletedTask;
        }

        private async Task OnInteractionAsync(SocketInteraction interaction)
        {
            try
            {
                var context = new SocketInteractionContext(_discord, interaction);
                var result = await _interactions.ExecuteCommandAsync(context, _services);

                if (!result.IsSuccess)
                    await context.Channel.SendMessageAsync(result.ToString());
            }
            catch
            {
                if (interaction.Type == InteractionType.ApplicationCommand)
                {
                    await interaction.GetOriginalResponseAsync()
                        .ContinueWith(msg => msg.Result.DeleteAsync());
                }
            }
        }
        /*
        private async Task OnCommandAsync(SocketMessage messageParam)
        {
            // Ignore system messages, DMs, etc.
            if (messageParam is not SocketUserMessage message) return;
            if (message.Source != MessageSource.User) return;

            int argPos = 0;

            // Prefix: '!'
            if (!message.HasCharPrefix('!', ref argPos)) return;

            var context = new SocketCommandContext(_discord, message);

            var result = await _commands.ExecuteAsync(context, argPos, _services);

            if (!result.IsSuccess)
                await message.Channel.SendMessageAsync(result.ErrorReason);
        }*/
    }
}
