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
using System.Windows;
using System.Windows.Resources;

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
            string? loginHint,
            CancellationToken cancellationToken)
        {
            ValidateConfiguration(configuration);

            string state = Guid.NewGuid().ToString("N");
            string codeVerifier = CreateCodeVerifier();
            string codeChallenge = CreateCodeChallenge(codeVerifier);

            using LoopbackOAuthReceiver receiver = new LoopbackOAuthReceiver();
            string authorizationUrl = BuildAuthorizationUrl(
                configuration.ClientId,
                receiver.RedirectUri,
                codeChallenge,
                state,
                loginHint);

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

        public async Task VerifyClientConfigurationAsync(
            GoogleDriveOAuthClientConfiguration configuration,
            CancellationToken cancellationToken)
        {
            ValidateConfiguration(configuration);

            Dictionary<string, string> formValues = new Dictionary<string, string>
            {
                ["client_id"] = configuration.ClientId.Trim(),
                ["code"] = "oceanya_verify_invalid_code",
                ["code_verifier"] = "oceanya_verify_invalid_code_verifier",
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = "http://localhost"
            };
            if (!string.IsNullOrWhiteSpace(configuration.ClientSecret))
            {
                formValues["client_secret"] = configuration.ClientSecret.Trim();
            }

            using FormUrlEncodedContent content = new FormUrlEncodedContent(formValues);
            using HttpResponseMessage response = await httpClient.PostAsync(TokenEndpoint, content, cancellationToken);
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            string oauthError = ExtractOAuthError(payload);
            string normalized = oauthError.Trim().ToLowerInvariant();
            if (normalized.Contains("invalid_client")
                || normalized.Contains("unauthorized_client")
                || normalized.Contains("client secret")
                || normalized.Contains("deleted_client"))
            {
                throw new InvalidOperationException("Google rejected the client ID or client secret: " + oauthError);
            }

            if (normalized.Contains("redirect_uri_mismatch"))
            {
                throw new InvalidOperationException(
                    "Google rejected the Desktop app OAuth setup for this client. Make sure you created a Desktop app OAuth client in Google Cloud.");
            }

            if (normalized.Contains("invalid_grant")
                || normalized.Contains("invalid grant")
                || normalized.Contains("malformed auth code")
                || normalized.Contains("authorization code")
                || normalized.Contains("code verifier"))
            {
                return;
            }

            throw new InvalidOperationException("Could not verify the Google Cloud credentials: " + oauthError);
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
            string state,
            string? loginHint)
        {
            Dictionary<string, string> values = new Dictionary<string, string>
            {
                ["client_id"] = clientId.Trim(),
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = DriveScope,
                ["access_type"] = "offline",
                ["include_granted_scopes"] = "true",
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["state"] = state
            };
            string normalizedLoginHint = loginHint?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedLoginHint))
            {
                values["login_hint"] = normalizedLoginHint;
            }

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

                string responseHtml = BuildCallbackResponseHtml(error);
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

        private static string BuildCallbackResponseHtml(string? error)
        {
            bool succeeded = string.IsNullOrWhiteSpace(error);
            string accentColor = succeeded ? "#87f07a" : "#ff8c8c";
            string title = succeeded ? "Google Drive Sign-In Complete" : "Google Drive Sign-In Failed";
            string subtitle = succeeded
                ? "Oceanya received your Google sign-in and can continue in the client."
                : "Google did not complete the sign-in request.";
            string detail = succeeded
                ? "You can close this tab and return to Oceanya."
                : "Return to Oceanya, fix the issue, then try the sign-in again.";
            string footer = succeeded
                ? "Browsers often refuse automatic tab closing after OAuth. Leaving this page visible is normal."
                : "This page will stay open so you can read the result instead of being dumped onto about:blank.";
            string errorMarkup = succeeded
                ? string.Empty
                : "<div class=\"detail-box\"><strong>Google said:</strong> "
                    + WebUtility.HtmlEncode(error?.Trim() ?? string.Empty)
                    + "</div>";
            string logoDataUri = TryLoadPackResourceDataUri(
                "pack://application:,,,/OceanyaClient;component/Resources/OceanyaFullLogo.png",
                "image/png");
            string backgroundDataUri = TryLoadPackResourceDataUri(
                "pack://application:,,,/OceanyaClient;component/Resources/LogBG.png",
                "image/png");
            string logoMarkup = string.IsNullOrWhiteSpace(logoDataUri)
                ? "<div class=\"text-logo\">OCEANYA</div>"
                : "<img class=\"brand-logo\" src=\"" + logoDataUri + "\" alt=\"Oceanya\" />";
            string backgroundStyle = string.IsNullOrWhiteSpace(backgroundDataUri)
                ? "linear-gradient(180deg, rgba(7, 12, 18, 0.94), rgba(4, 8, 12, 0.98))"
                : "linear-gradient(180deg, rgba(7, 12, 18, 0.80), rgba(4, 8, 12, 0.96)), url('"
                    + backgroundDataUri
                    + "')";

            return """
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8" />
                    <meta name="viewport" content="width=device-width, initial-scale=1" />
                    <title>Oceanya Google Drive Sign-In</title>
                    <style>
                        :root {
                            color-scheme: dark;
                        }

                        * {
                            box-sizing: border-box;
                        }

                        body {
                            margin: 0;
                            min-height: 100vh;
                            display: flex;
                            align-items: center;
                            justify-content: center;
                            padding: 28px;
                            font-family: "Segoe UI", Arial, sans-serif;
                            color: #f3f7fb;
                            background: BACKGROUND_STYLE;
                            background-position: center;
                            background-size: cover;
                        }

                        .shell {
                            width: min(760px, 100%);
                            border: 1px solid rgba(143, 214, 255, 0.18);
                            border-radius: 24px;
                            overflow: hidden;
                            background: linear-gradient(180deg, rgba(13, 22, 31, 0.92), rgba(8, 13, 18, 0.96));
                            box-shadow: 0 28px 90px rgba(0, 0, 0, 0.42);
                            backdrop-filter: blur(12px);
                        }

                        .hero {
                            position: relative;
                            padding: 28px 32px 18px;
                            background:
                                radial-gradient(circle at top right, rgba(113, 198, 255, 0.18), transparent 40%),
                                radial-gradient(circle at bottom left, rgba(81, 255, 191, 0.12), transparent 34%);
                        }

                        .hero::after {
                            content: "";
                            position: absolute;
                            inset: auto -12% -48px auto;
                            width: 240px;
                            height: 240px;
                            border-radius: 50%;
                            background: radial-gradient(circle, ACCENT_COLOR22 0%, transparent 66%);
                            pointer-events: none;
                        }

                        .brand-logo {
                            display: block;
                            max-width: min(320px, 72vw);
                            max-height: 92px;
                            margin-bottom: 20px;
                            filter: drop-shadow(0 8px 20px rgba(0, 0, 0, 0.32));
                        }

                        .text-logo {
                            margin-bottom: 20px;
                            font-size: 30px;
                            font-weight: 800;
                            letter-spacing: 0.32em;
                            color: #daf4ff;
                        }

                        .status-pill {
                            display: inline-flex;
                            align-items: center;
                            gap: 10px;
                            padding: 8px 14px;
                            border-radius: 999px;
                            border: 1px solid rgba(255, 255, 255, 0.08);
                            background: rgba(255, 255, 255, 0.05);
                            color: #d8f4ff;
                            font-size: 12px;
                            font-weight: 700;
                            letter-spacing: 0.12em;
                            text-transform: uppercase;
                        }

                        .status-pill::before {
                            content: "";
                            width: 10px;
                            height: 10px;
                            border-radius: 50%;
                            background: ACCENT_COLOR;
                            box-shadow: 0 0 18px ACCENT_COLOR88;
                        }

                        .title {
                            margin: 18px 0 10px;
                            font-size: clamp(30px, 4vw, 42px);
                            line-height: 1.06;
                            letter-spacing: 0.01em;
                        }

                        .subtitle {
                            margin: 0;
                            max-width: 54ch;
                            color: rgba(235, 243, 250, 0.84);
                            font-size: 16px;
                            line-height: 1.6;
                        }

                        .body {
                            padding: 0 32px 32px;
                        }

                        .detail-box {
                            margin-top: 20px;
                            padding: 16px 18px;
                            border-radius: 16px;
                            border: 1px solid rgba(255, 255, 255, 0.08);
                            background: rgba(255, 255, 255, 0.045);
                            color: #f7d3d3;
                            line-height: 1.6;
                        }

                        .footer {
                            margin-top: 18px;
                            color: rgba(211, 223, 233, 0.72);
                            font-size: 14px;
                            line-height: 1.6;
                        }

                        .actions {
                            display: flex;
                            gap: 12px;
                            flex-wrap: wrap;
                            margin-top: 26px;
                        }

                        .button {
                            appearance: none;
                            border: 0;
                            border-radius: 999px;
                            padding: 12px 18px;
                            background: linear-gradient(135deg, ACCENT_COLOR, #6ed2ff);
                            color: #071019;
                            font-size: 14px;
                            font-weight: 800;
                            cursor: pointer;
                        }

                        .button.secondary {
                            background: rgba(255, 255, 255, 0.08);
                            color: #f3f7fb;
                        }

                        @media (max-width: 640px) {
                            body {
                                padding: 16px;
                            }

                            .hero,
                            .body {
                                padding-left: 20px;
                                padding-right: 20px;
                            }
                        }
                    </style>
                </head>
                <body>
                    <main class="shell">
                        <section class="hero">
                            LOGO_MARKUP
                            <div class="status-pill">Google Drive OAuth</div>
                            <h1 class="title">TITLE_TEXT</h1>
                            <p class="subtitle">SUBTITLE_TEXT</p>
                        </section>
                        <section class="body">
                            <div class="detail-box">DETAIL_TEXT</div>
                            ERROR_MARKUP
                            <div class="actions">
                                <button class="button" type="button" onclick="window.close()">Close Tab</button>
                            </div>
                            <p class="footer">FOOTER_TEXT</p>
                        </section>
                    </main>
                </body>
                </html>
                """
                .Replace("BACKGROUND_STYLE", backgroundStyle, StringComparison.Ordinal)
                .Replace("ACCENT_COLOR22", accentColor + "22", StringComparison.Ordinal)
                .Replace("ACCENT_COLOR88", accentColor + "88", StringComparison.Ordinal)
                .Replace("ACCENT_COLOR", accentColor, StringComparison.Ordinal)
                .Replace("LOGO_MARKUP", logoMarkup, StringComparison.Ordinal)
                .Replace("TITLE_TEXT", WebUtility.HtmlEncode(title), StringComparison.Ordinal)
                .Replace("SUBTITLE_TEXT", WebUtility.HtmlEncode(subtitle), StringComparison.Ordinal)
                .Replace("DETAIL_TEXT", WebUtility.HtmlEncode(detail), StringComparison.Ordinal)
                .Replace("ERROR_MARKUP", errorMarkup, StringComparison.Ordinal)
                .Replace("FOOTER_TEXT", WebUtility.HtmlEncode(footer), StringComparison.Ordinal);
        }

        private static string TryLoadPackResourceDataUri(string packUri, string contentType)
        {
            try
            {
                StreamResourceInfo? resource = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute));
                if (resource?.Stream == null)
                {
                    return string.Empty;
                }

                using Stream stream = resource.Stream;
                using MemoryStream memoryStream = new MemoryStream();
                stream.CopyTo(memoryStream);
                return $"data:{contentType};base64,{Convert.ToBase64String(memoryStream.ToArray())}";
            }
            catch
            {
                return string.Empty;
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
