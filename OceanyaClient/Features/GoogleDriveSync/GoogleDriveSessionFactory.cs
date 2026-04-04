using System;
using System.Net.Http;

namespace OceanyaClient.Features.GoogleDriveSync
{
    public sealed class GoogleDriveSessionFactory
    {
        private readonly HttpClient httpClient;
        private readonly GoogleDriveSecureTokenStore tokenStore;
        private readonly GoogleDriveSecureClientCredentialStore credentialStore;
        private readonly GoogleDriveOAuthService oauthService;

        public GoogleDriveSessionFactory(
            HttpClient? httpClient = null,
            GoogleDriveSecureTokenStore? tokenStore = null,
            GoogleDriveSecureClientCredentialStore? credentialStore = null,
            GoogleDriveOAuthService? oauthService = null)
        {
            this.httpClient = httpClient ?? new HttpClient();
            this.tokenStore = tokenStore ?? new GoogleDriveSecureTokenStore();
            this.credentialStore = credentialStore ?? new GoogleDriveSecureClientCredentialStore();
            this.oauthService = oauthService ?? new GoogleDriveOAuthService(this.httpClient);
        }

        public async Task<GoogleDriveUserInfo> SignInAsync(
            GoogleDriveSyncSettings settings,
            CancellationToken cancellationToken)
        {
            GoogleDriveOAuthClientConfiguration configuration = BuildConfiguration(settings);
            GoogleDriveTokenSet tokens = await oauthService.SignInInteractiveAsync(
                configuration,
                settings.LastSignedInEmail,
                cancellationToken);
            tokenStore.Save(settings.TokenStoreKey, tokens);

            GoogleDriveApiClient apiClient = new GoogleDriveApiClient(httpClient, tokens.AccessToken);
            GoogleDriveUserInfo user = await apiClient.GetCurrentUserAsync(cancellationToken);
            settings.LastSignedInDisplayName = user.DisplayName;
            settings.LastSignedInEmail = user.EmailAddress;
            return user;
        }

        public async Task<IGoogleDriveRemoteClient> CreateAuthorizedClientAsync(
            GoogleDriveSyncSettings settings,
            CancellationToken cancellationToken)
        {
            GoogleDriveOAuthClientConfiguration configuration = BuildConfiguration(settings);
            GoogleDriveTokenSet tokens = tokenStore.Load(settings.TokenStoreKey)
                ?? throw new InvalidOperationException("Google Drive is not signed in for this client profile.");

            bool accessTokenExpired = string.IsNullOrWhiteSpace(tokens.AccessToken)
                || tokens.AccessTokenExpiresUtc <= DateTimeOffset.UtcNow.AddMinutes(1);
            if (accessTokenExpired)
            {
                tokens = await oauthService.RefreshAccessTokenAsync(configuration, tokens, cancellationToken);
                tokenStore.Save(settings.TokenStoreKey, tokens);
            }

            return new GoogleDriveApiClient(httpClient, tokens.AccessToken);
        }

        public void SignOut(GoogleDriveSyncSettings settings)
        {
            tokenStore.Delete(settings.TokenStoreKey);
            settings.LastSignedInDisplayName = string.Empty;
            settings.LastSignedInEmail = string.Empty;
        }

        private GoogleDriveOAuthClientConfiguration BuildConfiguration(GoogleDriveSyncSettings settings)
        {
            if (!GoogleDriveConnectionCredentialSupport.TryBuildConfiguration(
                    settings,
                    out GoogleDriveOAuthClientConfiguration configuration,
                    out string errorMessage,
                    credentialStore,
                    allowLegacyFallback: false))
            {
                throw new InvalidOperationException(errorMessage);
            }

            return configuration;
        }
    }
}
