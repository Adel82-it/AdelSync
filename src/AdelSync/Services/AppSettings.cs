using System.IO;
using System.Text.Json;

namespace AdelSync.Services;

public sealed class AppSettings
{
    public string BaseUrl { get; set; } = "https://zip-market.biz";
    public string ApiKey { get; set; } = "WJoyWkfEO-EBqLwHI2CJMYxov-Dv71yr";
    public int PollSeconds { get; set; } = 5;

    private static string PathFile =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdelSync",
            "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                var json = File.ReadAllText(PathFile);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // ignore
        }

        var s = new AppSettings();
        s.Save();
        return s;
    }

    public void Save()
    {
        var dir = System.IO.Path.GetDirectoryName(PathFile);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(PathFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
