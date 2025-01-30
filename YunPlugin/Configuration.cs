public class Configuration
{
    private static Configuration instance;
    public static Configuration Instance
    {
        get
        {
            if (instance == null)
                instance = new Configuration();
            return instance;
        }
    }

    public bool SearchSubDirectories { get; set; } = true;
    public int MaxSearchResults { get; set; } = 10;
    public string LocalMusicPath { get; set; } = "music"; // Ä¬ÈÏÒôÀÖÎÄ¼ş¼Ğ
}
