using System.Text.Json.Serialization;

namespace AdelSync.Services;

public sealed class SyncStatusDto
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("product")] public string Product { get; set; } = "AdelSync";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("site")] public string Site { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "idle";
    [JsonPropertyName("status_label")] public string StatusLabel { get; set; } = "";
    [JsonPropertyName("cooldown_seconds")] public int CooldownSeconds { get; set; }
    [JsonPropertyName("cooldown_total")] public int CooldownTotal { get; set; }
    [JsonPropertyName("next_exchange_label")] public string NextExchangeLabel { get; set; } = "";
    [JsonPropertyName("stats")] public SyncStatsDto Stats { get; set; } = new();
    [JsonPropertyName("events")] public List<SyncEventDto> Events { get; set; } = new();
    [JsonPropertyName("version")] public string Version { get; set; } = "";
}

public sealed class SyncStatsDto
{
    [JsonPropertyName("skips_today")] public int SkipsToday { get; set; }
    [JsonPropertyName("skips_total")] public int SkipsTotal { get; set; }
    [JsonPropertyName("imports_today")] public int ImportsToday { get; set; }
    [JsonPropertyName("cpu_saved_score")] public int CpuSavedScore { get; set; }
    [JsonPropertyName("cpu_saved_label")] public string CpuSavedLabel { get; set; } = "";
    [JsonPropertyName("last_import_at")] public string? LastImportAt { get; set; }
    [JsonPropertyName("last_skip_at")] public string? LastSkipAt { get; set; }
}

public sealed class SyncEventDto
{
    [JsonPropertyName("ts")] public long Ts { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}
