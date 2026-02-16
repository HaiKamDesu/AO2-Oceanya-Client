using AOBot_Testing.Agents;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{
    [TestFixture]
    [Category("NoAPICall")]
    public class GPTClientTests
    {
        // IMPORTANT: These tests use mocking to avoid making ANY actual API calls to OpenAI
        // This ensures we don't incur any costs during testing
        
        private class MockableGPTClient : GPTClient
        {
            public MockableGPTClient(string apiKey, HttpClient client) : base(apiKey)
            {
                _httpClient = client;
            }
            
            private readonly HttpClient _httpClient;
            
            // Override the method to use our mocked HTTP client instead of the real one
            public new async Task<string> GetResponseAsync(string prompt, string model = "gpt-4", double temperature = 0.2, int maxTokens = 150)
            {
                // SAFETY CHECK: This ensures we are only using our mock HTTP client
                if (_httpClient == null)
                {
                    throw new InvalidOperationException("Attempted to make a real API call: Mock HTTP client is null");
                }
                
                var messages = new List<object>();
                
                // Get the systemInstruction field via reflection since it's private
                var systemInstructionField = typeof(GPTClient).GetField("systemInstruction", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (systemInstructionField != null)
                {
                    var instructions = systemInstructionField.GetValue(this) as List<string>;
                    if (instructions != null)
                    {
                        foreach (string item in instructions)
                        {
                            string result = item;
                            foreach (var kvp in systemVariables)
                            {
                                result = result.Replace(kvp.Key, kvp.Value);
                            }
                            messages.Add(new { role = "system", content = result });
                        }
                    }
                }
                
                // If no instructions, add a default one
                if (messages.Count == 0)
                {
                    messages.Add(new { role = "system", content = "You are a helpful assistant." });
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
                _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer fake_test_api_key");

                // The mock handler will intercept this request and return our predefined response
                // No actual network call will be made
                using var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", requestContent);
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
        
        [Test]
        [Category("NoAPICall")]
        public void Test_GPTClient_Initialization()
        {
            // WARNING: This method shouldn't make any network calls, but it does create a real client
            // That's why we use a fake API key to be safe
            
            // Test initialization with fake API key (no actual API calls are made)
            Assert.DoesNotThrow(() => new GPTClient("fake_test_api_key_for_unit_tests_only"), "Should initialize with valid API key");
            
            // Test initialization with null API key (should throw)
            Assert.Throws<ArgumentNullException>(() => new GPTClient(null!), "Should throw when API key is null");
        }
        
        [Test]
        [Category("NoAPICall")]
        public void Test_GPTClient_SetSystemInstructions()
        {
            // WARNING: This method shouldn't make any network calls, but it does create a real client
            // That's why we use a fake API key to be safe
            
            // Create client with fake API key (no actual API calls are made)
            var client = new GPTClient("fake_test_api_key_for_unit_tests_only");
            
            // Set system instructions
            var instructions = new List<string> { "Instruction 1", "Instruction 2" };
            var result = client.SetSystemInstructions(instructions);
            
            // Should return self for method chaining
            Assert.That(result, Is.SameAs(client), "SetSystemInstructions should return self for method chaining");
        }
        
        [Test]
        [Category("NoAPICall")]
        public void Test_GPTClient_SystemVariables()
        {
            // WARNING: This method shouldn't make any network calls, but it does create a real client
            // That's why we use a fake API key to be safe
            
            // Create client with fake API key (no actual API calls are made)
            var client = new GPTClient("fake_test_api_key_for_unit_tests_only");
            
            // Add system variables
            client.systemVariables["[[[test_var]]]"] = "TestValue";
            client.systemVariables["[[[character]]]"] = "Phoenix";
            
            // Verify they were set
            Assert.That(client.systemVariables["[[[test_var]]]"], Is.EqualTo("TestValue"), "System variable should be set correctly");
            Assert.That(client.systemVariables["[[[character]]]"], Is.EqualTo("Phoenix"), "System variable should be set correctly");
        }
        
        [Test]
        [Category("NoAPICall")]
        public async Task Test_GPTClient_GetResponseAsync()
        {
            // This test uses a mock HTTP handler to intercept all requests
            // No actual API calls will be made
            
            // Create mock HTTP message handler
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(@"{
                        ""choices"": [
                            {
                                ""message"": {
                                    ""content"": ""This is a test response""
                                }
                            }
                        ]
                    }")
                });
            
            // Create HTTP client with mock handler
            var httpClient = new HttpClient(mockHandler.Object);
            
            // Create GPT client with mock HTTP client (uses fake API key)
            var client = new MockableGPTClient("fake_test_api_key_for_unit_tests_only", httpClient);
            
            // Set system instructions with variables
            client.SetSystemInstructions(new List<string> { "You are a helpful assistant called [[[assistant_name]]]" });
            client.systemVariables["[[[assistant_name]]]"] = "TestBot";
            
            // Get response (this will use the mocked handler)
            string response = await client.GetResponseAsync("Hello");
            
            // Verify response
            Assert.That(response, Is.EqualTo("This is a test response"), "Should return expected response");
            
            // Verify request was made with correct parameters
            mockHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req => 
                    req.Method == HttpMethod.Post && 
                    req.RequestUri != null &&
                    req.RequestUri.ToString() == "https://api.openai.com/v1/chat/completions" &&
                    req.Headers.Contains("Authorization")
                ),
                ItExpr.IsAny<CancellationToken>()
            );
        }
        
        [Test]
        [Category("NoAPICall")]
        public async Task Test_GPTClient_EmptyResponse()
        {
            // This test uses a mock HTTP handler to intercept all requests
            // No actual API calls will be made
            
            // Create mock HTTP message handler
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(@"{
                        ""choices"": [
                            {
                                ""message"": {
                                    ""content"": """"
                                }
                            }
                        ]
                    }")
                });
            
            // Create HTTP client with mock handler
            var httpClient = new HttpClient(mockHandler.Object);
            
            // Create GPT client with mock HTTP client (uses fake API key)
            var client = new MockableGPTClient("fake_test_api_key_for_unit_tests_only", httpClient);
            client.SetSystemInstructions(new List<string> { "Test instruction" });
            
            // Get response (this will use the mocked handler)
            string response = await client.GetResponseAsync("Hello");
            
            // Verify response is empty string, not null
            Assert.That(response, Is.EqualTo(string.Empty), "Should return empty string for empty response");
        }
        
        [Test]
        [Category("NoAPICall")]
        public void Test_GPTClient_HttpError()
        {
            // This test uses a mock HTTP handler to intercept all requests
            // No actual API calls will be made
            
            // Create mock HTTP message handler that returns an error
            var mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    Content = new StringContent(@"{""error"": ""invalid_request""}")
                });
            
            // Create HTTP client with mock handler
            var httpClient = new HttpClient(mockHandler.Object);
            
            // Create GPT client with mock HTTP client (uses fake API key)
            var client = new MockableGPTClient("fake_test_api_key_for_unit_tests_only", httpClient);
            client.SetSystemInstructions(new List<string> { "Test instruction" });
            
            // Should throw an exception when the API returns an error
            Assert.ThrowsAsync<HttpRequestException>(async () => 
                await client.GetResponseAsync("Hello")
            );
        }
    }
}
