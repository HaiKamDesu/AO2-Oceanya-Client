using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AO2AIBot.Clients
{
    /// <summary>
    /// Ollama-backed chat completion client.
    /// </summary>
    public sealed class OllamaClient : TemplatedChatClientBase
    {
        private readonly HttpClient httpClient;
        private readonly string endpoint;
        private readonly JsonNode? jsonSchema;
        private readonly int numCtx;

        /// <summary>
        /// Initializes a new instance of the <see cref="OllamaClient"/> class.
        /// </summary>
        /// <param name="jsonSchema">
        /// Optional JSON schema to pass as the Ollama "format" field.
        /// When provided, Ollama uses grammar-constrained decoding to guarantee the model's output
        /// matches the schema structure and enum values exactly.
        /// </param>
        /// <param name="numCtx">
        /// Context window size in tokens. Ollama defaults to 2048 which is too small for most
        /// agent prompts. Gemma 4 natively supports 8192; larger values require more VRAM.
        /// </param>
        public OllamaClient(string endpoint, HttpClient? httpClient = null, JsonNode? jsonSchema = null, int numCtx = 8192)
        {
            this.endpoint = string.IsNullOrWhiteSpace(endpoint)
                ? throw new ArgumentNullException(nameof(endpoint))
                : endpoint.Trim().TrimEnd('/');
            this.httpClient = httpClient ?? new HttpClient();
            this.jsonSchema = jsonSchema;
            this.numCtx = numCtx > 0 ? numCtx : 8192;
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
            string normalizedModel = string.IsNullOrWhiteSpace(model) ? "llama3.1:8b" : model.Trim();

            Dictionary<string, object?> requestBody = new Dictionary<string, object?>
            {
                ["model"] = normalizedModel,
                ["messages"] = BuildMessages(prompt),
                ["stream"] = partialResponseHandler != null,
                ["options"] = new
                {
                    temperature = temperature,
                    num_predict = maxTokens,
                    num_ctx = numCtx
                }
            };

            if (jsonSchema != null)
            {
                // Grammar-constrained decoding: the model output is forced to match this schema exactly.
                // This eliminates invalid JSON and out-of-range enum values (e.g. textColor:"cyan" is valid,
                // textColor:"rainbow" is not possible because it's not in the enum).
                requestBody["format"] = jsonSchema;
            }

            string requestJson = JsonSerializer.Serialize(requestBody);
            using HttpRequestMessage request = new HttpRequestMessage(
                HttpMethod.Post,
                endpoint + "/api/chat")
            {
                Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
            };

            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                partialResponseHandler == null ? HttpCompletionOption.ResponseContentRead : HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            if (partialResponseHandler != null)
            {
                return await ReadStreamingResponseAsync(response, partialResponseHandler, cancellationToken);
            }

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseContentFromJson(responseJson);
        }

        private static string ParseContentFromJson(string responseJson)
        {
            using JsonDocument jsonDocument = JsonDocument.Parse(responseJson);

            if (jsonDocument.RootElement.TryGetProperty("message", out JsonElement messageElement)
                && messageElement.TryGetProperty("content", out JsonElement contentElement))
            {
                return contentElement.GetString() ?? string.Empty;
            }

            if (jsonDocument.RootElement.TryGetProperty("response", out JsonElement fallbackElement))
            {
                return fallbackElement.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static async Task<string> ReadStreamingResponseAsync(
            HttpResponseMessage response,
            Action<string> partialResponseHandler,
            CancellationToken cancellationToken)
        {
            using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using StreamReader reader = new StreamReader(responseStream, Encoding.UTF8);
            StringBuilder combinedResponse = new StringBuilder();

            while (!reader.EndOfStream)
            {
                string? line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using JsonDocument chunkDocument = JsonDocument.Parse(line);
                JsonElement root = chunkDocument.RootElement;

                if (root.TryGetProperty("message", out JsonElement messageElement)
                    && messageElement.TryGetProperty("content", out JsonElement contentElement))
                {
                    string chunk = contentElement.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        combinedResponse.Append(chunk);
                        partialResponseHandler(combinedResponse.ToString());
                    }
                }
                else if (root.TryGetProperty("response", out JsonElement responseElement))
                {
                    string chunk = responseElement.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(chunk))
                    {
                        combinedResponse.Append(chunk);
                        partialResponseHandler(combinedResponse.ToString());
                    }
                }
            }

            return combinedResponse.ToString();
        }
    }
}
