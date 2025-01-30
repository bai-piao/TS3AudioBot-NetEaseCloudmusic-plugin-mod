using NeteaseApiData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using TS3AudioBot.ResourceFactories;

public enum Mode
{
    SeqPlay = 0,
    SeqLoopPlay = 1,
    RandomPlay = 2,
    RandomLoopPlay = 3,
}

public class PlayListMeta
{
    public string Id;
    public string Name;
    public string Image;

    public PlayListMeta(string id, string name, string image)
    {
        Id = id;
        Name = name;
        Image = image;
    }
}

public class MusicInfo
{
    public string Id = "";
    public string Name = "";
    public string Image = "";
    public string DetailUrl = "";
    public bool InPlayList;
    public bool IsLocalFile { get; private set; }
    public string Title { get; set; }
    private Dictionary<string, int?> Author = new Dictionary<string, int?>();

    public MusicInfo(string id, bool inPlayList = true)
    {
        this.Id = id;
        InPlayList = inPlayList;
        IsLocalFile = Path.IsPathRooted(id); // 判断是否为本地文件路径

        if(IsLocalFile)
        {
            Name = Path.GetFileNameWithoutExtension(id);
        }
    }

    public string GetAuthor()
    {
        return string.Join(" / ", Author.Keys);
    }

    public string GetFullName()
    {
        var author = GetAuthor();
        author = !string.IsNullOrEmpty(author) ? $" - {author}" : "";
        return Name + author;
    }

    public string GetFullNameBBCode()
    {
        var author = GetAuthorBBCode();
        author = !string.IsNullOrEmpty(author) ? $" - {author}" : "";
        return $"[URL={DetailUrl}]{Name}[/URL]{author}";
    }

    public string GetAuthorBBCode()
    {
        return string.Join(" / ", Author.Select(entry =>
        {
            string key = entry.Key;
            int? id = entry.Value;
            string authorName = id == null ? key : $"[URL=https://music.163.com/#/artist?id={id}]{key}[/URL]";
            return authorName;
        }));
    }

    public AudioResource GetMusicInfo()
    {
        if (IsLocalFile)
        {
            return new AudioResource(Id, GetFullName(), "media")
                .Add("LocalFile", "true");
        }
        
        return new AudioResource(DetailUrl, GetFullName(), "media")
            .Add("PlayUri", Image);
    }

    public async Task<byte[]> GetImage()
    {
        if (IsLocalFile || string.IsNullOrEmpty(Image))
        {
            return null;
        }

        HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Image);
        request.Method = "GET";

        using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
        using (Stream stream = response.GetResponseStream())
        using (MemoryStream memoryStream = new MemoryStream())
        {
            await stream.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }
    }

    public async Task InitMusicInfo(string api, Dictionary<string, string> header = null)
    {
        // 本地文件特殊处理
        if (IsLocalFile)
        {
            if (string.IsNullOrEmpty(Name))
            {
                Name = Path.GetFileNameWithoutExtension(Id);
            }
            if (string.IsNullOrEmpty(DetailUrl))
            {
                DetailUrl = Id;
            }
            if (string.IsNullOrEmpty(Image))
            {
                Image = ""; // 本地文件暂不支持封面图
            }
            return;
        }

        // 网易云音乐处理
        if (!string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(Image))
        {
            return;
        }
        try
        {
            string musicdetailurl = $"{api}/song/detail?ids={Id}&t={Utils.GetTimeStamp()}";
            JsonSongDetail musicDetail = await Utils.HttpGetAsync<JsonSongDetail>(musicdetailurl, header);
            Image = musicDetail.songs[0].al.picUrl;
            Name = musicDetail.songs[0].name;
            DetailUrl = $"https://music.163.com/#/song?id={Id}";

            Author.Clear();

            var artists = musicDetail.songs[0].ar;
            if (artists != null)
            {
                foreach (var artist in artists)
                {
                    if (!string.IsNullOrEmpty(artist.name))
                    {
                        Author.Add(artist.name, artist.id);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Name = "歌名获取失败!\n" + e.Message;
        }
    }

    // 获得歌曲URL
    public async Task<string> getMusicUrl(string api, Dictionary<string, string> header = null)
    {
        if(IsLocalFile)
            return Id; // 直接返回本地文件路径
            
        // 原有的网易云音乐获取逻辑
        string api_url = $"{api}/song/url?id={Id}&t={Utils.GetTimeStamp()}";

        try
        {
            var request = (HttpWebRequest)WebRequest.Create(api_url);
            request.Timeout = 10000; // 10秒超时
            request.ReadWriteTimeout = 10000;
            request.KeepAlive = true;
            request.ServicePoint.UseNagleAlgorithm = false; // 禁用Nagle算法
            request.ServicePoint.ConnectionLimit = 1; // 限制并发连接

            if (header != null)
            {
                foreach (var h in header)
                    request.Headers[h.Key] = h.Value;
            }

            using var response = await request.GetResponseAsync() as HttpWebResponse;
            using var reader = new StreamReader(response.GetResponseStream());
            var result = await reader.ReadToEndAsync();
            var musicurl = System.Text.Json.JsonSerializer.Deserialize<MusicURL>(result);
            return musicurl.data[0].url;
        }
        catch (Exception e)
        {
            YunPlugin.YunPlgun.GetLogger().Error(e, $"Get music url error: {api_url}");
            return $"error: {e.Message}";
        }
    }

    public override string ToString()
    {
        return Name ?? Title ?? string.Empty;
    }

    public static implicit operator string(MusicInfo info)
    {
        return info?.ToString() ?? string.Empty;
    }

    public static implicit operator ReadOnlySpan<char>(MusicInfo info)
    {
        return info?.ToString() ?? string.Empty;
    }
}