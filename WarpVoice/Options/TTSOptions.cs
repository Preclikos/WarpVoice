namespace WarpVoice.Options
{
    public class TTSOptions
    {
        public const string TTS = "TTS";

        public bool Enabled { get; set; }
        public string PiperPath { get; set; } = String.Empty;
        public string PiperModel { get; set; } = String.Empty;
    }
}
