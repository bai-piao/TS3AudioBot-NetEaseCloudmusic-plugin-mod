using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TS3AudioBot;
using TS3AudioBot.Audio;
using TS3AudioBot.ResourceFactories;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

public class PlayControl
{
    private PlayManager playManager;
    private InvokerData invoker;
    private Ts3Client ts3Client;
    private List<MusicInfo> songList = new List<MusicInfo>();
    private NLog.Logger Log;
    private string neteaseApi;
    private Mode mode;
    private int currentPlay = 0;
    private Dictionary<string, string> header = null;
    private MusicInfo currentPlayMusicInfo;
    private PlayListMeta playListMeta;
    private bool isPrivateFMMode;
    private string localMusicPath; // 本地音乐目录路径
    private Dictionary<string, List<string>> musicCache = new Dictionary<string, List<string>>();
    private Configuration config;  // 添加配置对象
    private const int BUFFER_SIZE = 262144; // 增加到256KB
    private const int RETRY_COUNT = 3;
    private const int RETRY_DELAY = 1000; // 1秒

    public PlayControl(PlayManager playManager, Ts3Client ts3Client, NLog.Logger log)
    {
        Log = log;
        this.playManager = playManager;
        this.ts3Client = ts3Client;
        config = Configuration.Instance;  // 或其他获取配置的方式
    }

    public void SetNeteaseApi(string neteaseApi)
    {
        this.neteaseApi = neteaseApi;
    }

    public string GetNeteaseApi()
    {
        return neteaseApi;
    }

    public MusicInfo GetCurrentPlayMusicInfo()
    {
        return currentPlayMusicInfo;
    }

    public Dictionary<string, string> GetHeader()
    {
        return header;
    }

    public void SetHeader(Dictionary<string, string> header)
    {
        this.header = header;
    }

    public InvokerData Getinvoker()
    {
        return this.invoker;
    }

    public void SetInvoker(InvokerData invoker)
    {
        this.invoker = invoker;
    }

    public Mode GetMode()
    {
        return this.mode;
    }

    public void SetMode(Mode mode)
    {
        this.mode = mode;
    }

    public void SetPrivateFM(bool isPrivateFMMode)
    {
        this.isPrivateFMMode = isPrivateFMMode;
    }

    public bool GetPrivateFM()
    {
        return this.isPrivateFMMode;
    }

    public void SetPlayList(PlayListMeta meta, List<MusicInfo> list)
    {
        playListMeta = meta;
        songList = new List<MusicInfo>(list);
        currentPlay = 0;
        if (mode == Mode.RandomPlay || mode == Mode.RandomLoopPlay)
        {
            Utils.ShuffleArrayList(songList);
        }
    }

    public List<MusicInfo> GetPlayList()
    {
        return songList;
    }

    public void AddMusic(MusicInfo musicInfo, bool insert = true)
    {
        songList.RemoveAll(m => m.Id == musicInfo.Id);
        if (insert)
            songList.Insert(0, musicInfo);
        else
            songList.Add(musicInfo);
    }

    public async Task PlayNextMusic()
    {
        if (songList.Count == 0)
        {
            return;
        }
        var musicInfo = GetNextMusic();
        await PlayMusic(musicInfo);
    }

    public async Task PlayMusic(MusicInfo musicInfo)
    {
        try
        {
            var invoker = Getinvoker();
            currentPlayMusicInfo = musicInfo;

            await musicInfo.InitMusicInfo(neteaseApi, header);
            string musicUrl = await GetMusicUrlWithRetry(musicInfo);
            
            if (musicUrl.StartsWith("error"))
            {
                await ts3Client.SendChannelMessage($"音乐链接获取失败 [{musicInfo.Name}] {musicUrl}");
                await PlayNextMusic();
                return;
            }

            // 简化资源创建
            var playResource = new MediaPlayResource(
                musicUrl, 
                musicInfo.GetMusicInfo(),
                IsLocalMusic(musicUrl) ? null : await musicInfo.GetImage(), 
                false);

            await playManager.Play(invoker, playResource);
            await ts3Client.SendChannelMessage($"► 正在播放：{musicInfo.GetFullNameBBCode()}");

            string desc = musicInfo.InPlayList 
                ? $"[{currentPlay}/{songList.Count}] {musicInfo.GetFullName()}"
                : musicInfo.GetFullName();

            await ts3Client.ChangeDescription(desc);

            // 非本地音乐才设置头像
            if (!IsLocalMusic(musicUrl))
            {
                try
                {
                    await MainCommands.CommandBotAvatarSet(ts3Client, musicInfo.Image);
                }
                catch (Exception e)
                {
                    Log.Error(e, "Set avatar error");
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "PlayMusic error");
            await ts3Client.SendChannelMessage($"播放音乐失败 [{musicInfo.Name}]");
            await PlayNextMusic();
        }
    }

    private async Task<string> GetMusicUrlWithRetry(MusicInfo musicInfo)
    {
        for (int i = 0; i < RETRY_COUNT; i++)
        {
            try
            {
                var url = await musicInfo.getMusicUrl(neteaseApi, header);
                if (!url.StartsWith("error"))
                    return url;
                    
                Log.Debug($"Attempt {i + 1}/{RETRY_COUNT}: Failed to get music URL");
                
                if (i < RETRY_COUNT - 1)
                {
                    Log.Debug($"Waiting {RETRY_DELAY}ms before retry...");
                    await Task.Delay(RETRY_DELAY);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Retry {i + 1}/{RETRY_COUNT} failed: {ex.Message}");
                if (i < RETRY_COUNT - 1)
                    await Task.Delay(RETRY_DELAY);
            }
        }
        
        return "error: Failed after retries";
    }

    private bool IsLocalMusic(string path)
    {
        return path.StartsWith("/") || path.Contains(":\\");
    }

    public List<MusicInfo> GetNextPlayList(int limit = 3)
    {
        var list = new List<MusicInfo>();
        limit = Math.Min(limit, songList.Count);
        for (int i = 0; i < limit; i++)
        {
            list.Add(songList[i]);
        }
        return list;
    }

    private MusicInfo GetNextMusic()
    {
        MusicInfo result = songList[0];
        songList.RemoveAt(0);
        if (mode == Mode.SeqLoopPlay || mode == Mode.RandomLoopPlay) // 循环的重新加入列表
        {
            songList.Add(result);
            currentPlay += 1;
        }
        else
        {
            currentPlay = 1; // 不是循环播放就固定当前播放第一首
        }

        if (mode == Mode.RandomLoopPlay) // 如果播放计次达到播放列表最大就重新排序
        {
            if (currentPlay >= songList.Count)
            {
                Utils.ShuffleArrayList(songList);
                currentPlay = 1; // 重排了就从头开始
            }
        }

        return result;
    }

    public async Task<string> GetPlayListString()
    {
        var musicList = GetNextPlayList();
        var musicInfo = GetCurrentPlayMusicInfo();
        var descBuilder = new StringBuilder();
        descBuilder.AppendLine($"\n当前正在播放：{musicInfo.GetFullNameBBCode()}");
        var modeStr = mode switch
        {
            Mode.SeqPlay => "顺序播放",
            Mode.SeqLoopPlay => "当顺序循环",
            Mode.RandomPlay => "随机播放",
            Mode.RandomLoopPlay => "随机循环",
            _ => $"未知模式{mode}",
        };
        descBuilder.AppendLine($"当前播放模式：{modeStr}");
        descBuilder.Append("播放列表 ");
        if (playListMeta != null)
        {
            descBuilder.Append($"[URL=https://music.163.com/#/playlist?id={playListMeta.Id}]{playListMeta.Name}[/URL] ");
        }
        descBuilder.AppendLine($"[{currentPlay}/{songList.Count}]");


        for (var i = 0; i < musicList.Count; i++)
        {
            var music = musicList[i];
            await music.InitMusicInfo(neteaseApi, header);
            descBuilder.AppendLine($"{i + 1}: {music.GetFullNameBBCode()}");
        }

        return descBuilder.ToString();
    }

    public void Clear()
    {
        songList.Clear(); ;
    }

    public void SetLocalMusicPath(string path)
    {
        this.localMusicPath = path;
        // 初始化缓存
        RefreshMusicCache();
    }

    private void RefreshMusicCache()
    {
        if(string.IsNullOrEmpty(localMusicPath) || !Directory.Exists(localMusicPath))
            return;
            
        musicCache.Clear();
        string[] supportedExtensions = new[] { "*.mp3", "*.wav", "*.flac", "*.m4a" };
        var allFiles = new List<string>();
        
        foreach(var ext in supportedExtensions)
        {
            allFiles.AddRange(Directory.GetFiles(localMusicPath, ext, 
                config.SearchSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly));
        }

        foreach(var file in allFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file).ToLower();
            if(!musicCache.ContainsKey(fileName))
                musicCache[fileName] = new List<string>();
            musicCache[fileName].Add(file);
        }
    }

    public List<MusicInfo> SearchLocalMusic(string keyword)
    {
        var results = new List<MusicInfo>();
        if(string.IsNullOrEmpty(localMusicPath))
            return results;

        // 模糊搜索
        var matchedFiles = musicCache
            .Where(kvp => kvp.Key.Contains(keyword.ToLower()))
            .SelectMany(kvp => kvp.Value)
            .Take(config.MaxSearchResults)
            .ToList();

        foreach(var file in matchedFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var musicInfo = new MusicInfo(file, false)
            {
                Name = fileName,
                DetailUrl = file
            };
            results.Add(musicInfo);
        }
        
        return results;
    }
}
