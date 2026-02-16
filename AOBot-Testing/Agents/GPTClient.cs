using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AOBot_Testing.Agents
{
    public class GPTClient(string apiKey)
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

        public Dictionary<string, string> systemVariables = new Dictionary<string, string>();
        List<string> systemInstruction = new List<string>();

        public GPTClient SetSystemInstructions(List<string> instructions)
        {
            systemInstruction = instructions;
            return this;
        }

        public async Task<string> GetResponseAsync(string prompt, string model = "gpt-4", double temperature = 0.2, int maxTokens = 150)
        {
            var messages = new List<object>();
            foreach (string item in systemInstruction)
            {
                string result = item;
                foreach (var kvp in systemVariables)
                {
                    result = result.Replace(kvp.Key, kvp.Value);
                }
                messages.Add(new { role = "system", content = result });
            }
            messages.Add(new { role = "user", content = prompt });

            var requestBody = new
            {
                model = model,
                messages = messages,
                temperature = temperature,
                max_tokens = maxTokens
            };

            var requestJson = JsonSerializer.Serialize(requestBody);
            var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            using var response = await _httpClient.PostAsync(ApiUrl, requestContent);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var jsonDoc = JsonDocument.Parse(responseJson);
            return jsonDoc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }
    }
}
