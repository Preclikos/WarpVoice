namespace WarpVoice.Options
{
    public class VoIPOptions
    {
        public const string VoIP = "VoIP";

        public ulong GuildId { get; set; }
        public ulong MessageChannelId { get; set; }
        public ulong VoiceChannelId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string RtpIp { get; set; } = string.Empty;
        public int RtpPort { get; set; }
        public int SipPort { get; set; }
    }
}
