using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Pinwheel;

public sealed class TranslationResult
{
    public bool IsSuccess { get; set; }
    public string OriginalText { get; set; } = "";
    public string TranslatedText { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ErrorMessage { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public static class TranslationService
{
    private static readonly HttpClient HttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public static async Task<TranslationResult> TranslateAsync(string text, TranslationConfig config, CancellationToken cancellationToken = default)
    {
        text = text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TranslationResult
            {
                IsSuccess = false,
                OriginalText = text,
                ErrorMessage = "翻译文本不能为空。"
            };
        }

        // 检查配置
        bool isFreeMode = string.Equals(config.Provider, "Free", StringComparison.OrdinalIgnoreCase);
        bool hasApiKey = !string.IsNullOrWhiteSpace(config.ApiKey);

        if (!hasApiKey && !isFreeMode)
        {
            return new TranslationResult
            {
                IsSuccess = false,
                OriginalText = text,
                ErrorMessage = "尚未配置翻译 API Key，请点击右上角 ⚙️ 进行配置。"
            };
        }

        if (isFreeMode)
        {
            return await TranslateFreeAsync(text, cancellationToken);
        }

        return await TranslateWithLlmAsync(text, config, cancellationToken);
    }

    private static async Task<TranslationResult> TranslateWithLlmAsync(string text, TranslationConfig config, CancellationToken cancellationToken)
    {
        try
        {
            string baseUrl = config.BaseUrl.TrimEnd('/');
            if (!baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl += "/chat/completions";
            }

            var requestBody = new
            {
                model = string.IsNullOrWhiteSpace(config.Model) ? "deepseek-chat" : config.Model.Trim(),
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = "You are a professional, accurate translator. Detect the source text language. If it is Chinese, translate it into natural English. If it is not Chinese (English, Japanese, etc.), translate it into fluent Simplified Chinese. Output ONLY the translated content directly without any preamble, notes, or quotation marks."
                    },
                    new
                    {
                        role = "user",
                        content = text
                    }
                },
                temperature = 0.3
            };

            string jsonContent = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("Authorization", $"Bearer {config.ApiKey.Trim()}");

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new TranslationResult
                {
                    IsSuccess = false,
                    OriginalText = text,
                    Provider = config.Provider,
                    ErrorMessage = $"API 请求失败 ({(int)response.StatusCode})：{ExtractErrorMessage(responseBody)}"
                };
            }

            using var doc = JsonDocument.Parse(responseBody);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() > 0)
            {
                string translated = choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
                return new TranslationResult
                {
                    IsSuccess = true,
                    OriginalText = text,
                    TranslatedText = translated,
                    Provider = config.Provider
                };
            }

            return new TranslationResult
            {
                IsSuccess = false,
                OriginalText = text,
                Provider = config.Provider,
                ErrorMessage = "API 返回结果解析为空。"
            };
        }
        catch (OperationCanceledException)
        {
            return new TranslationResult
            {
                IsSuccess = false,
                OriginalText = text,
                Provider = config.Provider,
                ErrorMessage = "翻译请求超时，请检查网络连接。"
            };
        }
        catch (Exception ex)
        {
            return new TranslationResult
            {
                IsSuccess = false,
                OriginalText = text,
                Provider = config.Provider,
                ErrorMessage = $"翻译发生异常：{ex.Message}"
            };
        }
    }

    private static async Task<TranslationResult> TranslateFreeAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            // 自动判断目标语言：若主要是中文则翻译为 en，否则翻译为 zh-CN
            bool isChinese = ContainsChinese(text);
            string targetLang = isChinese ? "en" : "zh-CN";

            string encoded = Uri.EscapeDataString(text);
            string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetLang}&dt=t&q={encoded}";

            using var response = await HttpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new TranslationResult
                {
                    IsSuccess = false,
                    OriginalText = text,
                    Provider = "Free",
                    ErrorMessage = $"免费通道请求失败 ({(int)response.StatusCode})，建议配置大模型 API。"
                };
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var segments = root[0];

            var sb = new StringBuilder();
            foreach (var seg in segments.EnumerateArray())
            {
                if (seg.GetArrayLength() > 0)
                {
                    sb.Append(seg[0].GetString());
                }
            }

            string translated = sb.ToString().Trim();
            return new TranslationResult
            {
                IsSuccess = true,
                OriginalText = text,
                TranslatedText = translated,
                Provider = "Free"
            };
        }
        catch (Exception ex)
        {
            return new TranslationResult
            {
                IsSuccess = false,
                OriginalText = text,
                Provider = "Free",
                ErrorMessage = $"免费通道网络异常：{ex.Message}，建议配置大模型 API。"
            };
        }
    }

    private static bool ContainsChinese(string input)
    {
        foreach (char c in input)
        {
            if (c >= 0x4e00 && c <= 0x9fa5) return true;
        }
        return false;
    }

    private static string ExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorEl))
            {
                if (errorEl.TryGetProperty("message", out var msgEl))
                {
                    return msgEl.GetString() ?? responseBody;
                }
            }
        }
        catch { }

        if (responseBody.Length > 200) return responseBody.Substring(0, 200) + "...";
        return responseBody;
    }
}

