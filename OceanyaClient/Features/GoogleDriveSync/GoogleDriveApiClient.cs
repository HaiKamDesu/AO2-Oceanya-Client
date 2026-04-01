using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OceanyaClient.Features.GoogleDriveSync
{
    public sealed class GoogleDriveApiClient : IGoogleDriveRemoteClient
    {
        private const string DriveApiBaseUrl = "https://www.googleapis.com/drive/v3";
        private const string DriveUploadBaseUrl = "https://www.googleapis.com/upload/drive/v3";
        private const string FolderMimeType = "application/vnd.google-apps.folder";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient httpClient;
        private readonly string accessToken;

        public GoogleDriveApiClient(HttpClient httpClient, string accessToken)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            this.accessToken = string.IsNullOrWhiteSpace(accessToken)
                ? throw new ArgumentException("An access token is required.", nameof(accessToken))
                : accessToken.Trim();
        }

        public async Task<GoogleDriveUserInfo> GetCurrentUserAsync(CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                $"{DriveApiBaseUrl}/about?fields=user(displayName,emailAddress)");
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            GoogleDriveAboutResponse? about = await DeserializeAsync<GoogleDriveAboutResponse>(response, cancellationToken);
            return new GoogleDriveUserInfo
            {
                DisplayName = about?.User?.DisplayName?.Trim() ?? string.Empty,
                EmailAddress = about?.User?.EmailAddress?.Trim() ?? string.Empty
            };
        }

        public async Task<string> GetFolderNameAsync(string folderId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(folderId))
            {
                throw new InvalidOperationException("A Google Drive folder ID is required.");
            }

            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                $"{DriveApiBaseUrl}/files/{Uri.EscapeDataString(folderId.Trim())}?supportsAllDrives=true&fields=id,name,mimeType");
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            GoogleDriveFileResponse? folder = await DeserializeAsync<GoogleDriveFileResponse>(response, cancellationToken);
            string folderName = folder?.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(folderName))
            {
                throw new InvalidOperationException("Google Drive did not return a folder name for the selected folder.");
            }

            return folderName;
        }

        public async Task<string> GetStartPageTokenAsync(CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                $"{DriveApiBaseUrl}/changes/startPageToken?supportsAllDrives=true");
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            GoogleDriveStartPageTokenResponse? tokenResponse =
                await DeserializeAsync<GoogleDriveStartPageTokenResponse>(response, cancellationToken);
            string token = tokenResponse?.StartPageToken?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Google Drive did not return a start page token.");
            }

            return token;
        }

        public async Task<GoogleDriveChangePage> GetChangesAsync(string pageToken, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(pageToken))
            {
                throw new InvalidOperationException("A Google Drive change page token is required.");
            }

            string url = $"{DriveApiBaseUrl}/changes"
                + $"?pageToken={Uri.EscapeDataString(pageToken.Trim())}"
                + "&pageSize=1000"
                + "&supportsAllDrives=true"
                + "&includeItemsFromAllDrives=true"
                + "&includeRemoved=true"
                + $"&fields={Uri.EscapeDataString("nextPageToken,newStartPageToken,changes(fileId,removed,file(id,parents,name,mimeType,trashed))")}";
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, url);
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            GoogleDriveChangesResponse? changesResponse =
                await DeserializeAsync<GoogleDriveChangesResponse>(response, cancellationToken);
            GoogleDriveChangePage page = new GoogleDriveChangePage
            {
                NextPageToken = changesResponse?.NextPageToken?.Trim() ?? string.Empty,
                NewStartPageToken = changesResponse?.NewStartPageToken?.Trim() ?? string.Empty
            };

            foreach (GoogleDriveChangeResponse change in changesResponse?.Changes ?? new List<GoogleDriveChangeResponse>())
            {
                string itemId = change.FileId?.Trim() ?? change.File?.Id?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                page.Changes.Add(new GoogleDriveChangeEntry
                {
                    ItemId = itemId,
                    Removed = change.Removed,
                    ParentIds = (change.File?.Parents ?? new List<string>())
                        .Where(parentId => !string.IsNullOrWhiteSpace(parentId))
                        .Select(parentId => parentId.Trim())
                        .ToList()
                });
            }

            return page;
        }

        public async Task<GoogleDriveSyncSnapshot> GetSnapshotAsync(string rootFolderId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(rootFolderId))
            {
                throw new InvalidOperationException("A Google Drive folder ID is required.");
            }

            GoogleDriveSyncSnapshot snapshot = new GoogleDriveSyncSnapshot();
            Queue<(string FolderId, string RelativePath)> pendingFolders = new Queue<(string FolderId, string RelativePath)>();
            pendingFolders.Enqueue((rootFolderId.Trim(), string.Empty));

            while (pendingFolders.Count > 0)
            {
                (string currentFolderId, string currentRelativePath) = pendingFolders.Dequeue();
                string? pageToken = null;

                do
                {
                    GoogleDriveFileListResponse childPage = await ListChildrenAsync(currentFolderId, pageToken, cancellationToken);
                    foreach (GoogleDriveFileResponse child in childPage.Files)
                    {
                        ValidateDriveItemName(child.Name);

                        string childRelativePath = string.IsNullOrWhiteSpace(currentRelativePath)
                            ? child.Name
                            : currentRelativePath + "/" + child.Name;
                        childRelativePath = GoogleDriveLocalSnapshotBuilder.NormalizeRelativePath(childRelativePath);

                        if (string.Equals(child.MimeType, FolderMimeType, StringComparison.OrdinalIgnoreCase))
                        {
                            GoogleDriveSyncFolderEntry folderEntry = new GoogleDriveSyncFolderEntry
                            {
                                RelativePath = childRelativePath,
                                ItemId = child.Id ?? string.Empty
                            };
                            AddFolder(snapshot, folderEntry);
                            pendingFolders.Enqueue((folderEntry.ItemId, childRelativePath));
                            continue;
                        }

                        if (child.MimeType?.StartsWith("application/vnd.google-apps.", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            continue;
                        }

                        GoogleDriveSyncFileEntry fileEntry = new GoogleDriveSyncFileEntry
                        {
                            RelativePath = childRelativePath,
                            ItemId = child.Id ?? string.Empty,
                            ParentId = currentFolderId,
                            Size = ParseLong(child.Size),
                            Hash = child.Md5Checksum?.Trim() ?? string.Empty
                        };
                        AddFile(snapshot, fileEntry);
                    }

                    pageToken = childPage.NextPageToken;
                }
                while (!string.IsNullOrWhiteSpace(pageToken));
            }

            return snapshot;
        }

        public async Task<GoogleDriveSyncFolderEntry> CreateFolderAsync(
            string? parentFolderId,
            string folderName,
            CancellationToken cancellationToken)
        {
            ValidateDriveItemName(folderName);

            Dictionary<string, object> metadata = new Dictionary<string, object>
            {
                ["name"] = folderName,
                ["mimeType"] = FolderMimeType
            };
            if (!string.IsNullOrWhiteSpace(parentFolderId))
            {
                metadata["parents"] = new[] { parentFolderId.Trim() };
            }

            string payload = JsonSerializer.Serialize(metadata);
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Post,
                $"{DriveApiBaseUrl}/files?supportsAllDrives=true&fields=id,name");
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            GoogleDriveFileResponse? created = await DeserializeAsync<GoogleDriveFileResponse>(response, cancellationToken);
            return new GoogleDriveSyncFolderEntry
            {
                RelativePath = folderName,
                ItemId = created?.Id?.Trim() ?? string.Empty
            };
        }

        public async Task<string> UploadFileAsync(
            string parentFolderId,
            string fileName,
            string localFilePath,
            string? existingFileId,
            CancellationToken cancellationToken)
        {
            ValidateDriveItemName(fileName);

            byte[] fileBytes = await File.ReadAllBytesAsync(localFilePath, cancellationToken);
            Dictionary<string, object> metadata = new Dictionary<string, object>
            {
                ["name"] = fileName
            };
            if (string.IsNullOrWhiteSpace(existingFileId))
            {
                metadata["parents"] = new[] { parentFolderId.Trim() };
            }

            string boundary = "oceanya_" + Guid.NewGuid().ToString("N");
            MultipartContent content = new MultipartContent("related", boundary);
            StringContent metadataContent = new StringContent(JsonSerializer.Serialize(metadata), Encoding.UTF8, "application/json");
            ByteArrayContent fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(metadataContent);
            content.Add(fileContent);

            string uploadUrl = string.IsNullOrWhiteSpace(existingFileId)
                ? $"{DriveUploadBaseUrl}/files?uploadType=multipart&supportsAllDrives=true&fields=id"
                : $"{DriveUploadBaseUrl}/files/{Uri.EscapeDataString(existingFileId.Trim())}?uploadType=multipart&supportsAllDrives=true&fields=id";
            HttpMethod method = string.IsNullOrWhiteSpace(existingFileId) ? HttpMethod.Post : HttpMethod.Patch;

            using HttpRequestMessage request = CreateRequest(method, uploadUrl);
            request.Content = content;

            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            GoogleDriveFileResponse? uploaded = await DeserializeAsync<GoogleDriveFileResponse>(response, cancellationToken);
            return uploaded?.Id?.Trim() ?? existingFileId?.Trim() ?? string.Empty;
        }

        public async Task DownloadFileAsync(string fileId, string destinationPath, CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
            Directory.CreateDirectory(directory);

            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Get,
                $"{DriveApiBaseUrl}/files/{Uri.EscapeDataString(fileId)}?alt=media&supportsAllDrives=true");
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            string tempPath = destinationPath + ".tmp";
            await using (Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream output = File.Create(tempPath))
            {
                await responseStream.CopyToAsync(output, cancellationToken);
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempPath, destinationPath);
        }

        public async Task DeleteItemAsync(string itemId, CancellationToken cancellationToken)
        {
            using HttpRequestMessage request = CreateRequest(
                HttpMethod.Delete,
                $"{DriveApiBaseUrl}/files/{Uri.EscapeDataString(itemId)}?supportsAllDrives=true");
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
        }

        private async Task<GoogleDriveFileListResponse> ListChildrenAsync(
            string parentFolderId,
            string? pageToken,
            CancellationToken cancellationToken)
        {
            string query = $"'{parentFolderId}' in parents and trashed = false";
            string url = $"{DriveApiBaseUrl}/files"
                + $"?q={Uri.EscapeDataString(query)}"
                + $"&fields={Uri.EscapeDataString("nextPageToken,files(id,name,mimeType,md5Checksum,size)")}"
                + "&pageSize=1000"
                + "&supportsAllDrives=true"
                + "&includeItemsFromAllDrives=true";
            if (!string.IsNullOrWhiteSpace(pageToken))
            {
                url += "&pageToken=" + Uri.EscapeDataString(pageToken);
            }

            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, url);
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            return await DeserializeAsync<GoogleDriveFileListResponse>(response, cancellationToken)
                ?? new GoogleDriveFileListResponse();
        }

        private HttpRequestMessage CreateRequest(HttpMethod method, string url)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return request;
        }

        private static void AddFolder(GoogleDriveSyncSnapshot snapshot, GoogleDriveSyncFolderEntry folderEntry)
        {
            if (snapshot.Folders.ContainsKey(folderEntry.RelativePath) || snapshot.Files.ContainsKey(folderEntry.RelativePath))
            {
                throw new InvalidOperationException(
                    $"Google Drive sync folder conflict detected at '{folderEntry.RelativePath}'. Duplicate paths are not supported.");
            }

            snapshot.Folders[folderEntry.RelativePath] = folderEntry;
        }

        private static void AddFile(GoogleDriveSyncSnapshot snapshot, GoogleDriveSyncFileEntry fileEntry)
        {
            if (snapshot.Files.ContainsKey(fileEntry.RelativePath) || snapshot.Folders.ContainsKey(fileEntry.RelativePath))
            {
                throw new InvalidOperationException(
                    $"Google Drive sync file conflict detected at '{fileEntry.RelativePath}'. Duplicate paths are not supported.");
            }

            snapshot.Files[fileEntry.RelativePath] = fileEntry;
        }

        private static long ParseLong(string? value)
        {
            return long.TryParse(value, out long parsed) ? parsed : 0L;
        }

        private static void ValidateDriveItemName(string? itemName)
        {
            string value = itemName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Google Drive returned an item with an empty name.");
            }

            if (value.Contains('/') || value.Contains('\\'))
            {
                throw new InvalidOperationException(
                    $"Google Drive item '{value}' contains path separator characters, which are not supported by the sync feature.");
            }
        }

        private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            string message = response.ReasonPhrase ?? "Unknown Google Drive error";
            try
            {
                GoogleDriveErrorEnvelope? error = JsonSerializer.Deserialize<GoogleDriveErrorEnvelope>(content, JsonOptions);
                if (!string.IsNullOrWhiteSpace(error?.Error?.Message))
                {
                    message = error.Error.Message;
                }
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(content))
                {
                    message = content;
                }
            }

            throw new HttpRequestException($"Google Drive request failed: {message}", null, response.StatusCode);
        }

        private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }

        private sealed class GoogleDriveAboutResponse
        {
            public GoogleDriveUserResponse? User { get; set; }
        }

        private sealed class GoogleDriveUserResponse
        {
            public string DisplayName { get; set; } = string.Empty;
            public string EmailAddress { get; set; } = string.Empty;
        }

        private sealed class GoogleDriveFileListResponse
        {
            public string NextPageToken { get; set; } = string.Empty;
            public List<GoogleDriveFileResponse> Files { get; set; } = new List<GoogleDriveFileResponse>();
        }

        private sealed class GoogleDriveStartPageTokenResponse
        {
            public string StartPageToken { get; set; } = string.Empty;
        }

        private sealed class GoogleDriveChangesResponse
        {
            public string NextPageToken { get; set; } = string.Empty;
            public string NewStartPageToken { get; set; } = string.Empty;
            public List<GoogleDriveChangeResponse> Changes { get; set; } = new List<GoogleDriveChangeResponse>();
        }

        private sealed class GoogleDriveChangeResponse
        {
            public string FileId { get; set; } = string.Empty;
            public bool Removed { get; set; }
            public GoogleDriveFileResponse? File { get; set; }
        }

        private sealed class GoogleDriveFileResponse
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string MimeType { get; set; } = string.Empty;
            public string Md5Checksum { get; set; } = string.Empty;
            public string Size { get; set; } = string.Empty;
            public List<string> Parents { get; set; } = new List<string>();
        }

        private sealed class GoogleDriveErrorEnvelope
        {
            public GoogleDriveErrorBody? Error { get; set; }
        }

        private sealed class GoogleDriveErrorBody
        {
            public string Message { get; set; } = string.Empty;
        }
    }
}
