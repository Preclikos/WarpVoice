using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using WarpVoice.HostedServices;
using WarpVoice.Options;
using WarpVoice.Services;

namespace WarpVoice
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }
        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<DiscordOptions>(Configuration.GetSection(DiscordOptions.Discord));
            services.Configure<VoIPOptions>(Configuration.GetSection(VoIPOptions.VoIP));
            services.Configure<AddressBookOptions>(Configuration.GetSection(AddressBookOptions.AddressBook));
            services.Configure<TTSOptions>(Configuration.GetSection(AddressBookOptions.AddressBook));

            services.AddControllers()
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                    options.SerializerSettings.Converters.Add(new StringEnumConverter());
                });

            // Register the Swagger generator, defining 1 or more Swagger documents
            services.AddSwaggerGen(c =>
            { //<-- NOTE 'Add' instead of 'Configure'
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "WiggleWiggle Bot Api",
                    Version = "v1"
                });
            });

            services.AddHttpClient();

            var config = new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged |
                     GatewayIntents.Guilds |
                     GatewayIntents.MessageContent |
                     GatewayIntents.GuildVoiceStates |
                     GatewayIntents.GuildMessages |
                     GatewayIntents.GuildMembers
            };

            services.AddSingleton<ISessionManager, SessionManager>();

            services.AddSingleton<ISipService, SipService>();

            services.AddSingleton<DiscordSocketClient>(new DiscordSocketClient(config));
            services.AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()));
            //services.AddSingleton(x => new CommandService()); not used
            services.AddHostedService<InteractionHandlingService>();    // Add the slash command handler
            services.AddHostedService<DiscordStartupService>();
            services.AddHostedService<VoIPStartupService>();
            //services.AddHostedService<IdleDisconnectService>(); not used
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var loggerFactory = app.ApplicationServices.GetRequiredService<ILoggerFactory>();
            SIPSorcery.LogFactory.Set(loggerFactory);

            if (!env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSwagger(c =>
            {
                c.RouteTemplate = env.IsDevelopment() ? "swagger/{documentName}/swagger.json" : "api/swagger/{documentName}/swagger.json";
            });

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("swagger/v1/swagger.json", "WiggleWiggle Bot API");
                c.RoutePrefix = env.IsDevelopment() ? string.Empty : "api";
            });

            app.UseCors(builder => builder
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader());

            app.UseRouting();

            app.UseForwardedHeaders();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            }
            );
        }
    }
}
