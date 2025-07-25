﻿using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using WarpVoice.Audio;
using WarpVoice.Enums;
using WarpVoice.Models;
using WarpVoice.Options;

namespace WarpVoice.Services
{
    public class SessionManager : ISessionManager
    {
        private readonly ConcurrentDictionary<ulong, VoiceSession> _sessions = new();
        private readonly TTSOptions _ttsOptions;
        private readonly ILogger<SessionManager> _logger;
        private readonly ILogger<DiscordUsersVoice> _loggerDiscordUsersVoice;
        private readonly ILogger<DiscordAudioMixer> _loggerDiscordAudioMixer;
        private readonly VoIPOptions _voIpOptions;
        private readonly AddressBookOptions _addressBookOptions;
        private readonly DiscordSocketClient _discord;
        private readonly ISipService _sipService;

        public SessionManager(ILogger<SessionManager> logger, ILogger<DiscordUsersVoice> loggerDiscordUsersVoice, ILogger<DiscordAudioMixer> loggerDiscordAudioMixer,
            IOptions<VoIPOptions> voIpOptions, IOptions<AddressBookOptions> addressBookOptions, IOptions<TTSOptions> ttsOptions,
            DiscordSocketClient discord, ISipService sipService
            )
        {
            _ttsOptions = ttsOptions.Value;
            _logger = logger;
            _loggerDiscordUsersVoice = loggerDiscordUsersVoice;
            _loggerDiscordAudioMixer = loggerDiscordAudioMixer;
            _voIpOptions = voIpOptions.Value;
            _addressBookOptions = addressBookOptions.Value;
            _discord = discord;
            _sipService = sipService;
        }

        public bool CanStartSession(ulong guildId)
        {
            if (_sessions.ContainsKey(guildId)) return false;
            if (_sessions.Values.Any(s => s.IsSipInUse)) return false;
            return true;
        }

        public async Task<List<IGuildUser>> FlattenAsyncEnumerable(
    IAsyncEnumerable<IReadOnlyCollection<IGuildUser>> asyncEnumerable)
        {
            var result = new List<IGuildUser>();

            await foreach (var group in asyncEnumerable)
            {
                result.AddRange(group); // Flatten each collection into the result list
            }

            return result;
        }

        public async Task<bool> StartSession(ulong guildId, ulong messageChannelId, ulong voiceChannelId, SIPUserAgent userAgent, SIPServerUserAgent? serverUserAgent, RTPSession rtpSession, CallDirection direction, string number)
        {
            if (!CanStartSession(guildId)) return false;
            var session = new VoiceSession(guildId, userAgent, rtpSession);
            if (_sessions.TryAdd(guildId, session))
            {
                try
                {
                    _logger.LogInformation($"{guildId} - Session added");
                    var guild = _discord.GetGuild(guildId);

                    var messageChannel = guild.GetTextChannel(messageChannelId);
                    session.MessageChannel = messageChannel;

                    var voiceChannel = guild.GetVoiceChannel(voiceChannelId);
                    session.VoiceChannel = voiceChannel;

                    var result = number;
                    if (number.StartsWith("420"))
                    {
                        number = number.Substring(3);
                    }

                    string pattern = @"(\d{3})$";
                    string replacement = "XXX";

                    result = _addressBookOptions.NameNumbers.Any(a => a.Value == number) ?
                        _addressBookOptions.NameNumbers.SingleOrDefault(a => a.Value == number).Key :
                       number.Length > 8 ? Regex.Replace(number, pattern, replacement) : number;

                    var callerNumber = String.Empty;
                    if (direction == CallDirection.Outgoing)
                    {
                        var destinationUri = $"sip:{number}@{_voIpOptions.Domain}";
                        await _sipService.MakeCallAsync(rtpSession, destinationUri);

                        await messageChannel.SendMessageAsync($"Call to {result} was started");
                    }
                    else
                    {
                        callerNumber = _addressBookOptions.NameNumbers.Any(a => a.Value == number) ?
                            _addressBookOptions.NameNumbers.SingleOrDefault(a => a.Value == number).Key :
                            string.Join(" ", number.ToCharArray());
                        await messageChannel.SendMessageAsync($"Receiving call from {result}");
                    }

                    var users = await FlattenAsyncEnumerable(((IVoiceChannel)voiceChannel).GetUsersAsync());
                    var usersInVoiceChannel = users.Where(w => w.VoiceChannel != null && w.VoiceChannel.Id == voiceChannelId && !w.IsBot);
                    if (usersInVoiceChannel.Count() == 0 || !usersInVoiceChannel.Any(a => !a.IsSelfDeafened))
                    {
                        await messageChannel.SendMessageAsync($"There are no active users to handle call.");
                        await EndSessionDiscord(guildId);
                        return false;
                    }

                    var audioClient = await voiceChannel.ConnectAsync();
                    session.AudioClient = audioClient;

                    var userVoices = new DiscordUsersVoice(_loggerDiscordUsersVoice, _loggerDiscordAudioMixer, _ttsOptions, _voIpOptions, voiceChannel, audioClient);
                    session.DiscordVoiceManager = userVoices;

                    //Single node only
                    //await _discord.SetGameAsync(result, type: ActivityType.Playing); //long time needed :( remove it or maybe other

                    SIPCallFailedDelegate callFailedHandler = async (uac, errorMessage, sipResponse) => await UserAgent_ClientCallFailed(guildId, uac, errorMessage, sipResponse);
                    Action<SIPDialogue> hungupHandler = async (sIPDialogue) => await UserAgent_OnCallHungup(guildId, sIPDialogue);
                    Action<SDPMediaTypesEnum> sessionOnTimeout = async (mediaTypeEnum) => await RtpSession_OnTimeout(guildId, mediaTypeEnum);

                    userAgent.ClientCallFailed += callFailedHandler;
                    userAgent.OnCallHungup += hungupHandler;
                    rtpSession.OnTimeout += sessionOnTimeout;

                    session.SIPAgentEvents = (callFailedHandler, hungupHandler, sessionOnTimeout);

                    _logger.LogInformation($"{guildId} - Session configured");

                    _ = Task.Run(async () => { await userVoices.GetMixer().DiscordToVoIp(audioClient, rtpSession); });
                    _ = Task.Run(async () => { await userVoices.GetMixer().VoIpToDiscord(audioClient, userAgent, serverUserAgent, rtpSession, callerNumber); });

                    _logger.LogInformation($"{guildId} - Session audio started");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Can't start session");
                    await EndSessionDiscord(guildId);
                    return false;
                }

            }
            return false;
        }

        private async Task RtpSession_OnTimeout(ulong guildId, SDPMediaTypesEnum obj)
        {
            await EndSessionVoIp(guildId);
        }

        private async Task UserAgent_OnCallHungup(ulong guildId, SIPDialogue sIPDialogue)
        {
            await EndSessionDiscord(guildId);
        }

        private async Task UserAgent_ClientCallFailed(ulong guildId, ISIPClientUserAgent uac, string errorMessage, SIPResponse sipResponse)
        {
            await EndSessionDiscord(guildId);
        }

        public Task EndSessionVoIp(ulong guildId)
        {
            if (_sessions.TryGetValue(guildId, out var session))
            {
                if (session.SIPUserAgent.IsCallActive)
                {
                    session.SIPUserAgent.Hangup();
                }
                else
                {
                    session.SIPUserAgent.Cancel();
                }
            }
            return Task.CompletedTask;
        }

        public async Task EndSessionDiscord(ulong guildId)
        {
            if (_sessions.TryGetValue(guildId, out var session))
            {
                if (session.DiscordVoiceManager != null)
                {
                    await session.DiscordVoiceManager.Stop();
                }
                if (session.VoiceChannel != null)
                {
                    await session.VoiceChannel.DisconnectAsync();
                }

                session.SIPUserAgent.ClientCallFailed -= session.SIPAgentEvents.callFailedHandler;
                session.SIPUserAgent.OnCallHungup -= session.SIPAgentEvents.hungupHandler;
                session.MediaSession.OnTimeout -= session.SIPAgentEvents.sessionOnTimeout;

                //Single node only
                //await _discord.SetGameAsync("/call | /hangup", type: ActivityType.Listening); //long time needed :( remove it or maybe other
                if (session.MessageChannel != null)
                {
                    await session.MessageChannel.SendMessageAsync("Hanged up");
                }

                if (!_sessions.Remove(guildId, out var _))
                {
                    _logger.LogError("End session no session!!!!!!!!!!");
                }
            }
            else
            {
                _logger.LogInformation("End session no session!!!!!!!!!!");
            }
        }

        public VoiceSession? GetSession(ulong guildId)
        {
            _sessions.TryGetValue(guildId, out var session);
            return session;
        }
    }
}
