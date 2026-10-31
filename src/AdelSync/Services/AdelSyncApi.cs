using System.Net.Http;
using System.Text.Json;

namespace AdelSync.Services;

public sealed class AdelSyncApi
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly AppSettings _settings;

    public AdelSyncApi(AppSettings settings)
    {
        _settings = settings;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AdelSync/1.0 (+Adel Gainullin)");
    }

    public async Task<SyncStatusDto> FetchAsync(CancellationToken ct = default)
    {
        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/wp-json/adelsync/v1/status?key={Uri.EscapeDataString(_settings.ApiKey)}";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"API {((int)resp.StatusCode)}: {Trim(body)}");

        var dto = JsonSerializer.Deserialize<SyncStatusDto>(body);
        if (dto is null || !dto.Ok)
            throw new InvalidOperationException("Пустой ответ AdelSync API");
        return dto;
    }

    private static string Trim(string s) =>
        s.Length <= 180 ? s : s[..180] + "…";
}
