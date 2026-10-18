using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace StaffNavi.Services
{
    /// <summary>
    /// JSONファイルによる簡易データストア（デモ用。DBは使用しない）
    /// </summary>
    public class DataStore
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly object _sync = new();

        public string Root { get; }

        public DataStore(IConfiguration configuration, IWebHostEnvironment env)
        {
            Root = Path.Combine(env.ContentRootPath, configuration["Storage:DataDirectory"] ?? "App_Data");
            Directory.CreateDirectory(Root);
        }

        public bool Exists(string name) => File.Exists(GetPath(name));

        public T Read<T>(string name, Func<T> fallback)
        {
            lock (_sync)
            {
                var path = GetPath(name);
                if (!File.Exists(path))
                {
                    return fallback();
                }
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? fallback();
            }
        }

        public void Write<T>(string name, T value)
        {
            lock (_sync)
            {
                var path = GetPath(name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
                File.Move(temp, path, true);
            }
        }

        public void Update<T>(string name, Func<T> fallback, Action<T> mutate)
        {
            lock (_sync)
            {
                var value = Read(name, fallback);
                mutate(value);
                Write(name, value);
            }
        }

        public void Delete(string name)
        {
            lock (_sync)
            {
                var path = GetPath(name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                foreach (var entry in Directory.GetFileSystemEntries(Root))
                {
                    if (Directory.Exists(entry))
                    {
                        Directory.Delete(entry, true);
                    }
                    else
                    {
                        File.Delete(entry);
                    }
                }
            }
        }

        private string GetPath(string name)
        {
            var path = Path.GetFullPath(Path.Combine(Root, name));
            if (!path.StartsWith(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("不正なパスです。", nameof(name));
            }
            return path;
        }
    }
}
