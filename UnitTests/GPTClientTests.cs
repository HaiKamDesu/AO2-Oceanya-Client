using AO2AIBot.Clients;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{
    [TestFixture]
    [Category("NoAPICall")]
    public class GPTClientTests
    {
        [Test]
        [Category("NoAPICall")]
        public void Test_GPTClient_Initialization()
        {
            Assert.DoesNotThrow(() => new GPTClient("fake_test_api_key_for_unit_tests_only"));
            Assert.Throws<ArgumentNullException>(() => new GPTClient(null!));
        }

        [Test]
        [Category("NoAPICall")]
        public void Test_GPTClient_SetSystemInstructions()
        {
            GPTClient client = new GPTClient("fake_test_api_key_for_unit_tests_only");
            IChatPromptClient result = client.SetSystemInstructions(new[] { "Instruction 1", "Instruction 2" });
            Assert.That(result, Is.SameAs(client));
        }

        [Test]
        [Category("NoAPICall")]
        public void Test_GPTClient_SystemVariables()
        {
            GPTClient client = new GPTClient("fake_test_api_key_for_unit_tests_only");
            client.SystemVariables["[[[test_var]]]"] = "TestValue";
            client.SystemVariables["[[[character]]]"] = "Phoenix";

            Assert.That(client.SystemVariables["[[[test_var]]]"], Is.EqualTo("TestValue"));
            Assert.That(client.SystemVariables["[[[character]]]"], Is.EqualTo("Phoenix"));
        }

        [Test]
        [Category("NoAPICall")]
        public async Task Test_GPTClient_GetResponseAsync()
        {
            Mock<HttpMessageHandler> mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(
                        """
                        {
                          "choices": [
                            {
                              "message": {
                                "content": "This is a test response"
                              }
                            }
                          ]
                        }
                        """)
                });

            HttpClient httpClient = new HttpClient(mockHandler.Object);
            GPTClient client = new GPTClient("fake_test_api_key_for_unit_tests_only", httpClient);
            client.SetSystemInstructions(new[] { "You are [[[assistant_name]]]." });
            client.SystemVariables["[[[assistant_name]]]"] = "TestBot";

            string response = await client.GetResponseAsync("Hello", "gpt-4o-mini");

            Assert.That(response, Is.EqualTo("This is a test response"));
            mockHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.Method == HttpMethod.Post
                    && request.RequestUri != null
                    && request.RequestUri.ToString() == "https://api.openai.com/v1/chat/completions"
                    && request.Headers.Authorization != null
                    && request.Headers.Authorization.Scheme == "Bearer"),
                ItExpr.IsAny<CancellationToken>());
        }

        [Test]
        [Category("NoAPICall")]
        public async Task Test_GPTClient_EmptyResponse()
        {
            Mock<HttpMessageHandler> mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(
                        """
                        {
                          "choices": [
                            {
                              "message": {
                                "content": ""
                              }
                            }
                          ]
                        }
                        """)
                });

            HttpClient httpClient = new HttpClient(mockHandler.Object);
            GPTClient client = new GPTClient("fake_test_api_key_for_unit_tests_only", httpClient);
            client.SetSystemInstructions(new[] { "Test instruction" });

            string response = await client.GetResponseAsync("Hello", "gpt-4o-mini");

            Assert.That(response, Is.EqualTo(string.Empty));
        }

        [Test]
        [Category("NoAPICall")]
        public void Test_GPTClient_HttpError()
        {
            Mock<HttpMessageHandler> mockHandler = new Mock<HttpMessageHandler>();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    Content = new StringContent(@"{""error"": ""invalid_request""}")
                });

            HttpClient httpClient = new HttpClient(mockHandler.Object);
            GPTClient client = new GPTClient("fake_test_api_key_for_unit_tests_only", httpClient);
            client.SetSystemInstructions(new[] { "Test instruction" });

            Assert.ThrowsAsync<HttpRequestException>(async () => await client.GetResponseAsync("Hello", "gpt-4o-mini"));
        }
    }
}
