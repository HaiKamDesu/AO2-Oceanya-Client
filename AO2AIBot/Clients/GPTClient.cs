using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AO2AIBot.Clients
{
    /// <summary>
    /// OpenAI-backed chat completion client.
    /// </summary>
    public sealed class GPTClient : TemplatedChatClientBase
    {
        private const string ApiUrl = "https://api.openai.com/v1/chat/completions";
        private readonly HttpClient httpClient;
        private readonly string apiKey;

        /// <summary>
        /// Initializes a new instance of the <see cref="GPTClient"/> class.
        /// </summary>
        public GPTClient(string apiKey, HttpClient? httpClient = null)
        {
            this.apiKey = string.IsNullOrWhiteSpace(apiKey)
                ? throw new ArgumentNullException(nameof(apiKey))
                : apiKey.Trim();
            this.httpClient = httpClient ?? new HttpClient();
        }

        /// <inheritdoc/>
        public override async Task<string> GetResponseAsync(
            string prompt,
            string model,
            double temperature = 0.2,
            int maxTokens = 150,
            Action<string>? partialResponseHandler = null,
            CancellationToken cancellationToken = default)
        {
            var requestBody = new
            {
                model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model.Trim(),
                messages = BuildMessages(prompt),
                temperature = temperature,
                max_tokens = maxTokens
            };

            string requestJson = JsonSerializer.Serialize(requestBody);
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using JsonDocument jsonDocument = JsonDocument.Parse(responseJson);
            return jsonDocument.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
    }
}
