using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace OceanyaClient.Features.GoogleDriveSync
{
    public sealed class GoogleDriveOAuthService
    {
        private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string DriveScope = "https://www.googleapis.com/auth/drive";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient httpClient;

        public GoogleDriveOAuthService(HttpClient? httpClient = null)
        {
            this.httpClient = httpClient ?? new HttpClient();
        }

        public async Task<GoogleDriveTokenSet> SignInInteractiveAsync(
            GoogleDriveOAuthClientConfiguration configuration,
            CancellationToken cancellationToken)
        {
            ValidateConfiguration(configuration);

            string state = Guid.NewGuid().ToString("N");
            string codeVerifier = CreateCodeVerifier();
            string codeChallenge = CreateCodeChallenge(codeVerifier);

            using LoopbackOAuthReceiver receiver = new LoopbackOAuthReceiver();
            string authorizationUrl = BuildAuthorizationUrl(configuration.ClientId, receiver.RedirectUri, codeChallenge, state);

            TryLaunchBrowser(authorizationUrl);
            LoopbackAuthorizationResponse callback = await receiver.WaitForCallbackAsync(state, cancellationToken);
            if (!string.IsNullOrWhiteSpace(callback.Error))
            {
                throw new InvalidOperationException($"Google sign-in failed: {callback.Error}");
            }

            if (string.IsNullOrWhiteSpace(callback.Code))
            {
                throw new InvalidOperationException("Google sign-in did not return an authorization code.");
            }

            GoogleDriveTokenSet tokenSet = await ExchangeAuthorizationCodeAsync(
                configuration,
                callback.Code,
                codeVerifier,
                receiver.RedirectUri,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(tokenSet.RefreshToken))
            {
                throw new InvalidOperationException("Google sign-in did not return a refresh token.");
            }

            return tokenSet;
        }

        public async Task<GoogleDriveTokenSet> RefreshAccessTokenAsync(
            GoogleDriveOAuthClientConfiguration configuration,
            GoogleDriveTokenSet currentTokens,
            CancellationToken cancellationToken)
        {
            ValidateConfiguration(configuration);
            if (string.IsNullOrWhiteSpace(currentTokens.RefreshToken))
            {
                throw new InvalidOperationException("A stored refresh token is required.");
            }

            Dictionary<string, string> formValues = new Dictionary<string, string>
            {
                ["client_id"] = configuration.ClientId.Trim(),
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = currentTokens.RefreshToken.Trim()
            };
            if (!string.IsNullOrWhiteSpace(configuration.ClientSecret))
            {
                formValues["client_secret"] = configuration.ClientSecret.Trim();
            }

            using FormUrlEncodedContent content = new FormUrlEncodedContent(formValues);
            using HttpResponseMessage response = await httpClient.PostAsync(TokenEndpoint, content, cancellationToken);
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Google token refresh failed: " + ExtractOAuthError(payload));
            }

            GoogleOAuthTokenResponse? tokenResponse = JsonSerializer.Deserialize<GoogleOAuthTokenResponse>(payload, JsonOptions);
            if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
            {
                throw new InvalidOperationException("Google token refresh returned an empty access token.");
            }

            return new GoogleDriveTokenSet
            {
                AccessToken = tokenResponse.AccessToken.Trim(),
                RefreshToken = currentTokens.RefreshToken.Trim(),
                AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, tokenResponse.ExpiresIn))
            };
        }

        private async Task<GoogleDriveTokenSet> ExchangeAuthorizationCodeAsync(
            GoogleDriveOAuthClientConfiguration configuration,
            string code,
            string codeVerifier,
            string redirectUri,
            CancellationToken cancellationToken)
        {
            Dictionary<string, string> formValues = new Dictionary<string, string>
            {
                ["client_id"] = configuration.ClientId.Trim(),
                ["code"] = code.Trim(),
                ["code_verifier"] = codeVerifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            };
            if (!string.IsNullOrWhiteSpace(configuration.ClientSecret))
            {
                formValues["client_secret"] = configuration.ClientSecret.Trim();
            }

            using FormUrlEncodedContent content = new FormUrlEncodedContent(formValues);
            using HttpResponseMessage response = await httpClient.PostAsync(TokenEndpoint, content, cancellationToken);
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Google sign-in token exchange failed: " + ExtractOAuthError(payload));
            }

            GoogleOAuthTokenResponse? tokenResponse = JsonSerializer.Deserialize<GoogleOAuthTokenResponse>(payload, JsonOptions);
            if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
            {
                throw new InvalidOperationException("Google sign-in returned an empty access token.");
            }

            return new GoogleDriveTokenSet
            {
                AccessToken = tokenResponse.AccessToken.Trim(),
                RefreshToken = tokenResponse.RefreshToken?.Trim() ?? string.Empty,
                AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, tokenResponse.ExpiresIn))
            };
        }

        private static string BuildAuthorizationUrl(
            string clientId,
            string redirectUri,
            string codeChallenge,
            string state)
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                ["client_id"] = clientId.Trim(),
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = DriveScope,
                ["access_type"] = "offline",
                ["include_granted_scopes"] = "true",
                ["prompt"] = "consent",
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["state"] = state
            };

            StringBuilder builder = new StringBuilder(AuthorizationEndpoint);
            builder.Append('?');
            builder.Append(string.Join("&", values.Select(pair =>
                Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
            return builder.ToString();
        }

        private static string CreateCodeVerifier()
        {
            byte[] randomBytes = RandomNumberGenerator.GetBytes(48);
            return ToBase64Url(randomBytes);
        }

        private static string CreateCodeChallenge(string codeVerifier)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier));
            return ToBase64Url(hash);
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private static string ExtractOAuthError(string payload)
        {
            try
            {
                GoogleOAuthErrorResponse? response = JsonSerializer.Deserialize<GoogleOAuthErrorResponse>(payload, JsonOptions);
                if (!string.IsNullOrWhiteSpace(response?.ErrorDescription))
                {
                    return response.ErrorDescription;
                }

                if (!string.IsNullOrWhiteSpace(response?.Error))
                {
                    return response.Error;
                }
            }
            catch
            {
            }

            return string.IsNullOrWhiteSpace(payload) ? "Unknown OAuth error." : payload;
        }

        private static void ValidateConfiguration(GoogleDriveOAuthClientConfiguration configuration)
        {
            if (string.IsNullOrWhiteSpace(configuration.ClientId))
            {
                throw new InvalidOperationException("A Google OAuth client ID is required.");
            }
        }

        private static void TryLaunchBrowser(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Could not launch the browser for Google sign-in. Open this URL manually: " + url,
                    ex);
            }
        }

        private sealed class LoopbackOAuthReceiver : IDisposable
        {
            private readonly HttpListener listener;

            public LoopbackOAuthReceiver()
            {
                int port = ReserveLoopbackPort();
                RedirectUri = $"http://localhost:{port}/";
                listener = new HttpListener();
                listener.Prefixes.Add(RedirectUri);
                listener.Start();
            }

            public string RedirectUri { get; }

            public async Task<LoopbackAuthorizationResponse> WaitForCallbackAsync(string expectedState, CancellationToken cancellationToken)
            {
                Task<HttpListenerContext> contextTask = listener.GetContextAsync();
                Task completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
                if (completed != contextTask)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                HttpListenerContext context = await contextTask;
                string code = context.Request.QueryString["code"] ?? string.Empty;
                string state = context.Request.QueryString["state"] ?? string.Empty;
                string error = context.Request.QueryString["error"] ?? string.Empty;

                string responseHtml = string.IsNullOrWhiteSpace(error)
                    ? """
                      <html>
                      <body style="font-family:Segoe UI,Arial,sans-serif;background:#14181c;color:#f0f0f0;padding:24px;">
                      <h2>Oceanya Google Drive sign-in completed.</h2>
                      <p>Returning to Oceanya. This tab should close automatically.</p>
                      <script>
                      (function () {
                        function finish() {
                          try {
                            window.open('', '_self', '');
                            window.close();
                          } catch (e) {
                          }
                          setTimeout(function () {
                            try {
                              window.location.replace('about:blank');
                            } catch (e) {
                            }
                          }, 250);
                        }
                        window.addEventListener('load', function () {
                          setTimeout(finish, 100);
                        });
                      })();
                      </script>
                      <p>If this tab stays open, you can close it manually.</p>
                      </body>
                      </html>
                      """
                    : """
                      <html>
                      <body style="font-family:Segoe UI,Arial,sans-serif;background:#14181c;color:#f0f0f0;padding:24px;">
                      <h2>Google Drive sign-in failed.</h2>
                      <p>You can close this tab and return to Oceanya.</p>
                      </body>
                      </html>
                      """;
                byte[] bytes = Encoding.UTF8.GetBytes(responseHtml);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.LongLength;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
                context.Response.OutputStream.Close();

                if (!string.Equals(state, expectedState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Google sign-in returned an invalid OAuth state.");
                }

                return new LoopbackAuthorizationResponse
                {
                    Code = code,
                    Error = error
                };
            }

            public void Dispose()
            {
                if (listener.IsListening)
                {
                    listener.Stop();
                }

                listener.Close();
            }

            private static int ReserveLoopbackPort()
            {
                using TcpListener tcpListener = new TcpListener(IPAddress.Loopback, 0);
                tcpListener.Start();
                int port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
                tcpListener.Stop();
                return port;
            }
        }

        private sealed class LoopbackAuthorizationResponse
        {
            public string Code { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
        }

        private sealed class GoogleOAuthTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [JsonPropertyName("refresh_token")]
            public string RefreshToken { get; set; } = string.Empty;

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }

        private sealed class GoogleOAuthErrorResponse
        {
            [JsonPropertyName("error")]
            public string Error { get; set; } = string.Empty;

            [JsonPropertyName("error_description")]
            public string ErrorDescription { get; set; } = string.Empty;
        }
    }
}
