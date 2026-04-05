using AO2AIBot.Clients;
using Moq;
using Moq.Protected;
using NUnit.Framework;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace UnitTests
{
    [TestFixture]
    [Category("NoAPICall")]
    public class OllamaClientTests
    {
        [Test]
        [Category("NoAPICall")]
        public async Task Test_OllamaClient_GetResponseAsync()
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
                          "message": {
                            "content": "Ollama says hello"
                          }
                        }
                        """)
                });

            HttpClient httpClient = new HttpClient(mockHandler.Object);
            OllamaClient client = new OllamaClient("http://127.0.0.1:11434", httpClient);
            client.SetSystemInstructions(new[] { "You are [[[assistant_name]]]." });
            client.SystemVariables["[[[assistant_name]]]"] = "TestBot";

            string response = await client.GetResponseAsync("Hello", "llama3.1:8b");

            Assert.That(response, Is.EqualTo("Ollama says hello"));
            mockHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(request =>
                    request.Method == HttpMethod.Post
                    && request.RequestUri != null
                    && request.RequestUri.ToString() == "http://127.0.0.1:11434/api/chat"),
                ItExpr.IsAny<CancellationToken>());
        }

        [Test]
        [Category("NoAPICall")]
        public void Test_OllamaClient_Initialization()
        {
            Assert.DoesNotThrow(() => new OllamaClient("http://127.0.0.1:11434"));
            Assert.Throws<ArgumentNullException>(() => new OllamaClient(null!));
        }

        [Test]
        [Category("NoAPICall")]
        public async Task Test_OllamaClient_GetResponseAsync_Streaming()
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
                        {"message":{"content":"Hello"}}
                        {"message":{"content":" there"}}
                        {"done":true}
                        """)
                });

            HttpClient httpClient = new HttpClient(mockHandler.Object);
            OllamaClient client = new OllamaClient("http://127.0.0.1:11434", httpClient);
            List<string> partialUpdates = new List<string>();

            string response = await client.GetResponseAsync(
                "Hello",
                "llama3.1:8b",
                partialResponseHandler: partialUpdates.Add);

            Assert.That(response, Is.EqualTo("Hello there"));
            Assert.That(partialUpdates, Is.EqualTo(new[] { "Hello", "Hello there" }));
        }
    }
}
