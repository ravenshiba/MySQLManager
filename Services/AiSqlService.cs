using System.Collections.Generic;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MySQLManager.Services;

/// <summary>
/// AI 輔助 SQL 服務
/// 使用 Groq 免費 API (llama-3.3-70b-versatile)
/// 使用者也可設定自己的 API Key
/// </summary>
public class AiSqlService
{
    private static readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.groq.com/openai/v1/"),
        Timeout     = TimeSpan.FromSeconds(30)
    };

    public string ApiKey { get; set; } = string.Empty;
    public bool   IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>
    /// 將自然語言描述轉換為 SQL
    /// </summary>
    /// <param name="prompt">自然語言描述，例如「列出所有年齡大於30的用戶」</param>
    /// <param name="schemaHint">資料庫 Schema 提示（可選），例如 "users(id,name,age), orders(id,user_id,total)"</param>
    /// <param name="database">目前資料庫名稱</param>
    public async Task<AiSqlResult> GenerateSqlAsync(
        string prompt, string? schemaHint = null, string? database = null)
    {
        if (!IsConfigured)
            return new AiSqlResult { Error = "請先設定 Groq API Key（免費）\n前往 https://console.groq.com 取得" };

        var systemPrompt = BuildSystemPrompt(schemaHint, database);

        try
        {
            var request = new GroqRequest
            {
                Model = "llama-3.3-70b-versatile",
                Messages = new[]
                {
                    new GroqMessage { Role = "system", Content = systemPrompt },
                    new GroqMessage { Role = "user",   Content = prompt }
                },
                MaxTokens = 512,
                Temperature = 0.1f  // 低溫讓輸出更確定性
            };

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");

            var response = await _http.PostAsJsonAsync("chat/completions", request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                return new AiSqlResult { Error = $"API 錯誤 {(int)response.StatusCode}: {err[..Math.Min(200, err.Length)]}" };
            }

            var result = await response.Content.ReadFromJsonAsync<GroqResponse>();
            var content = result?.Choices?[0]?.Message?.Content ?? "";

            // 提取 SQL（移除 markdown code block）
            var sql = ExtractSql(content);
            return new AiSqlResult { Sql = sql, RawResponse = content };
        }
        catch (TaskCanceledException)
        {
            return new AiSqlResult { Error = "請求逾時（30 秒），請重試" };
        }
        catch (Exception ex)
        {
            return new AiSqlResult { Error = $"連線失敗：{ex.Message}" };
        }
    }

    /// <summary>解釋 SQL 的功能</summary>
    public async Task<AiSqlResult> ExplainSqlAsync(string sql, string? database = null)
    {
        if (!IsConfigured)
            return new AiSqlResult { Error = "請先設定 Groq API Key" };

        var prompt = $"請用繁體中文簡短說明以下 SQL 的功能和效果（1-3句話）：\n\n```sql\n{sql}\n```";
        var systemPrompt = "你是 MySQL DBA 助手，請用繁體中文精簡說明 SQL 的功能。只說明功能，不要加多餘的說明。";

        try
        {
            var request = new GroqRequest
            {
                Model       = "llama-3.3-70b-versatile",
                Messages    = new[]
                {
                    new GroqMessage { Role = "system", Content = systemPrompt },
                    new GroqMessage { Role = "user",   Content = prompt }
                },
                MaxTokens   = 256,
                Temperature = 0.3f
            };

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");

            var response = await _http.PostAsJsonAsync("chat/completions", request);
            var result   = await response.Content.ReadFromJsonAsync<GroqResponse>();
            var content  = result?.Choices?[0]?.Message?.Content ?? "";
            return new AiSqlResult { Sql = content, RawResponse = content };
        }
        catch (Exception ex)
        {
            return new AiSqlResult { Error = ex.Message };
        }
    }


    /// <summary>
    /// 多輪對話：傳入完整歷史訊息列表
    /// </summary>
    public async Task<AiSqlResult> ChatAsync(
        List<AiChatMessage> history,
        string? schemaHint = null,
        string? database   = null)
    {
        if (!IsConfigured)
            return new AiSqlResult { Error = "請先設定 Groq API Key" };

        var systemPrompt = BuildChatSystemPrompt(schemaHint, database);
        var messages = new List<GroqMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };
        messages.AddRange(history.Select(m => new GroqMessage
        {
            Role    = m.IsUser ? "user" : "assistant",
            Content = m.Content
        }));

        try
        {
            var request = new GroqRequest
            {
                Model       = "llama-3.3-70b-versatile",
                Messages    = messages.ToArray(),
                MaxTokens   = 1024,
                Temperature = 0.3f
            };

            _http.DefaultRequestHeaders.Clear();
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");

            var response = await _http.PostAsJsonAsync("chat/completions", request);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                return new AiSqlResult { Error = $"API 錯誤 {(int)response.StatusCode}: {err[..Math.Min(200, err.Length)]}" };
            }

            var result  = await response.Content.ReadFromJsonAsync<GroqResponse>();
            var content = result?.Choices?[0]?.Message?.Content ?? "";
            return new AiSqlResult { Sql = content, RawResponse = content };
        }
        catch (TaskCanceledException)
        {
            return new AiSqlResult { Error = "請求逾時（30 秒），請重試" };
        }
        catch (Exception ex)
        {
            return new AiSqlResult { Error = $"連線失敗：{ex.Message}" };
        }
    }

    private static string BuildChatSystemPrompt(string? schemaHint, string? database)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("你是 MySQL 資料庫助手，使用繁體中文回答。能力包括：");
        sb.AppendLine("- 將自然語言轉換成 MySQL SQL");
        sb.AppendLine("- 解釋 SQL 語法與效能問題");
        sb.AppendLine("- 給出資料庫設計建議");
        sb.AppendLine("- 協助排查錯誤訊息");
        sb.AppendLine("回答規則：");
        sb.AppendLine("1. 若回答包含 SQL，請用 ```sql ... ``` 包裹");
        sb.AppendLine("2. 保持回答簡潔，重點清楚");
        sb.AppendLine("3. 繁體中文回答，技術詞彙可保留英文");

        if (!string.IsNullOrWhiteSpace(database))
            sb.AppendLine($"\n目前資料庫：{database}");
        if (!string.IsNullOrWhiteSpace(schemaHint))
            sb.AppendLine($"\n資料庫 Schema：\n{schemaHint}");

        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────

    private static string BuildSystemPrompt(string? schemaHint, string? database)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("你是 MySQL 專家，根據使用者的自然語言描述產生標準 MySQL SQL 查詢。");
        sb.AppendLine("規則：");
        sb.AppendLine("1. 只輸出 SQL，不要任何解釋或說明");
        sb.AppendLine("2. 不要用 markdown 格式包裝（不要 ```sql）");
        sb.AppendLine("3. 表名和欄位名用反引號括住");
        sb.AppendLine("4. 預設加 LIMIT 1000 除非有指定");
        sb.AppendLine("5. 使用標準 MySQL 語法");

        if (!string.IsNullOrWhiteSpace(database))
            sb.AppendLine($"\n目前資料庫：{database}");

        if (!string.IsNullOrWhiteSpace(schemaHint))
            sb.AppendLine($"\n資料庫 Schema：\n{schemaHint}");

        return sb.ToString();
    }

    private static string ExtractSql(string content)
    {
        content = content.Trim();
        // 移除 markdown code block
        if (content.StartsWith("```"))
        {
            var firstLine = content.IndexOf('\n');
            var lastBlock = content.LastIndexOf("```");
            if (firstLine > 0 && lastBlock > firstLine)
                content = content[(firstLine + 1)..lastBlock].Trim();
        }
        return content;
    }

    // ── DTO ───────────────────────────────────────────────────

    private class GroqRequest
    {
        [JsonPropertyName("model")]       public string Model    { get; set; } = "";
        [JsonPropertyName("messages")]    public GroqMessage[] Messages { get; set; } = Array.Empty<GroqMessage>();
        [JsonPropertyName("max_tokens")]  public int MaxTokens  { get; set; }
        [JsonPropertyName("temperature")] public float Temperature { get; set; }
    }

    private class GroqMessage
    {
        [JsonPropertyName("role")]    public string Role    { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private class GroqResponse
    {
        [JsonPropertyName("choices")] public GroqChoice[]? Choices { get; set; }
    }

    private class GroqChoice
    {
        [JsonPropertyName("message")] public GroqMessage? Message { get; set; }
    }
}

public class AiSqlResult
{
    public string? Sql         { get; set; }
    public string? Error       { get; set; }
    public string? RawResponse { get; set; }
    public bool    Success     => Error == null && !string.IsNullOrWhiteSpace(Sql);
}

public class AiChatMessage
{
    public string  Content   { get; set; } = "";
    public bool    IsUser    { get; set; }
    public DateTime Time     { get; set; } = DateTime.Now;
    public bool    IsError   { get; set; }
    public string? SqlBlock  { get; set; }   // 擷取的 SQL 片段（若有）

    // 解析回應中的 SQL code block
    public static AiChatMessage FromAssistant(string content)
    {
        var msg = new AiChatMessage { Content = content, IsUser = false };
        var m = System.Text.RegularExpressions.Regex.Match(
            content, @"```sql\s*([\s\S]+?)```",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) msg.SqlBlock = m.Groups[1].Value.Trim();
        return msg;
    }
}
