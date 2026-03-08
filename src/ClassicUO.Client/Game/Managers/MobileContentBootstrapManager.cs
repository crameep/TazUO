// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClassicUO.Configuration;
using ClassicUO.Utility.Logging;
using ClassicUO.Utility.Platforms;
using NSec.Cryptography;

namespace ClassicUO.Game.Managers
{
    internal static class MobileContentBootstrapManager
    {
        private const string DEFAULT_MANIFEST_NAME = "manifest.json";
        private const int MAX_DOWNLOAD_ATTEMPTS = 3;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        private static string _lastSuccessfulKey = string.Empty;

        internal readonly struct BootstrapResult
        {
            public BootstrapResult(bool success, string error)
            {
                Success = success;
                ErrorMessage = error ?? string.Empty;
            }

            public bool Success { get; }
            public string ErrorMessage { get; }
        }

        public static BootstrapResult EnsureContentReady()
        {
            if (!PlatformHelper.IsMobile)
            {
                return new BootstrapResult(true, string.Empty);
            }

            Settings settings = Settings.GlobalSettings;
            settings.NormalizeAndValidate();

            string key = BuildBootstrapKey(settings);

            if (string.Equals(_lastSuccessfulKey, key, StringComparison.Ordinal))
            {
                return new BootstrapResult(true, string.Empty);
            }

            if (string.IsNullOrWhiteSpace(settings.UpdateUrl))
            {
                return new BootstrapResult(false, "Update URL is required for mobile content bootstrap.");
            }

            if (string.IsNullOrWhiteSpace(settings.UpdatePublicKey))
            {
                return new BootstrapResult(false, "Update public key is required for manifest signature verification.");
            }

            if (string.IsNullOrWhiteSpace(settings.UltimaOnlineDirectory))
            {
                return new BootstrapResult(false, "Ultima Online directory is empty and cannot store downloaded content.");
            }

            try
            {
                Uri manifestUri = BuildManifestUri(settings.UpdateUrl);
                ManifestPayload payload = DownloadAndParseManifest(manifestUri, settings.UpdatePublicKey);
                string targetRoot = settings.UltimaOnlineDirectory;

                Directory.CreateDirectory(targetRoot);

                foreach (ManifestEntry entry in payload.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.RelativePath))
                    {
                        continue;
                    }

                    string relativePath = NormalizeRelativePath(entry.RelativePath);
                    string destinationPath = ResolveDestinationPath(targetRoot, relativePath);

                    if (IsFileCurrent(destinationPath, entry.Size, entry.Sha256))
                    {
                        continue;
                    }

                    Uri fileUri = BuildFileUri(manifestUri, payload.BaseUrl, entry);
                    DownloadWithResume(fileUri, destinationPath, entry);
                }

                _lastSuccessfulKey = key;

                return new BootstrapResult(true, string.Empty);
            }
            catch (Exception ex)
            {
                Log.Error($"Mobile content bootstrap failed: {ex}");
                return new BootstrapResult(false, ex.Message);
            }
        }

        private static string BuildBootstrapKey(Settings settings)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{settings.UpdateUrl}|{settings.UpdateHost}|{settings.UpdatePort}|{settings.UltimaOnlineDirectory}");
        }

        private static Uri BuildManifestUri(string updateUrl)
        {
            if (!Uri.TryCreate(updateUrl, UriKind.Absolute, out Uri updateUri))
            {
                throw new InvalidOperationException($"Invalid update URL: '{updateUrl}'.");
            }

            if (updateUri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return updateUri;
            }

            string separator = updateUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? string.Empty : "/";
            return new Uri(updateUri.AbsoluteUri + separator + DEFAULT_MANIFEST_NAME, UriKind.Absolute);
        }

        private static ManifestPayload DownloadAndParseManifest(Uri manifestUri, string updatePublicKey)
        {
            using HttpResponseMessage response = _httpClient.GetAsync(manifestUri).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Manifest request failed ({(int) response.StatusCode}) at {manifestUri}.");
            }

            byte[] manifestBytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            byte[] signatureBytes = DownloadManifestSignature(manifestUri);
            VerifyManifestSignature(manifestBytes, signatureBytes, updatePublicKey);

            string json = Encoding.UTF8.GetString(manifestBytes);

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            string baseUrl = TryGetString(root, "base_url") ?? TryGetString(root, "baseUrl") ?? string.Empty;

            if (!TryGetProperty(root, "files", out JsonElement filesElement) || filesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Manifest is missing a 'files' array.");
            }

            var entries = new List<ManifestEntry>();

            foreach (JsonElement file in filesElement.EnumerateArray())
            {
                string relativePath =
                    TryGetString(file, "relative_path") ??
                    TryGetString(file, "relativePath") ??
                    TryGetString(file, "path") ??
                    TryGetString(file, "file") ??
                    string.Empty;

                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                long size = TryGetLong(file, "size");
                string sha256 =
                    TryGetString(file, "sha256") ??
                    TryGetString(file, "hash") ??
                    TryGetString(file, "checksum") ??
                    string.Empty;

                string directUrl =
                    TryGetString(file, "url") ??
                    TryGetString(file, "download_url") ??
                    TryGetString(file, "downloadUrl") ??
                    string.Empty;

                entries.Add(new ManifestEntry(relativePath, size, sha256, directUrl));
            }

            if (entries.Count == 0)
            {
                throw new InvalidOperationException("Manifest file list is empty.");
            }

            return new ManifestPayload(baseUrl, entries);
        }

        private static byte[] DownloadManifestSignature(Uri manifestUri)
        {
            Uri signatureUri = new Uri(new Uri(manifestUri, "./"), "manifest.sig");
            using HttpResponseMessage response = _httpClient.GetAsync(signatureUri).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Manifest signature request failed ({(int) response.StatusCode}) at {signatureUri}.");
            }

            return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }

        private static void VerifyManifestSignature(byte[] manifestBytes, byte[] signatureBytes, string updatePublicKey)
        {
            byte[] publicKeyBytes = ParseHex(updatePublicKey, 32, "update public key");
            byte[] normalizedSignature = NormalizeSignature(signatureBytes);

            PublicKey key = PublicKey.Import(SignatureAlgorithm.Ed25519, publicKeyBytes, KeyBlobFormat.RawPublicKey);
            bool verified = SignatureAlgorithm.Ed25519.Verify(key, manifestBytes, normalizedSignature);

            if (!verified)
            {
                throw new InvalidOperationException("Manifest signature verification failed.");
            }
        }

        private static byte[] NormalizeSignature(byte[] signatureBytes)
        {
            if (signatureBytes.Length == 64)
            {
                return signatureBytes;
            }

            string asText = Encoding.UTF8.GetString(signatureBytes).Trim();
            if (asText.Length == 128)
            {
                return ParseHex(asText, 64, "manifest signature");
            }

            throw new InvalidOperationException($"Manifest signature must be 64 raw bytes (or 128-char hex), got {signatureBytes.Length} bytes.");
        }

        private static byte[] ParseHex(string value, int expectedBytes, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{fieldName} is empty.");
            }

            string normalized = value.Trim();

            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[2..];
            }

            if (normalized.Length != expectedBytes * 2)
            {
                throw new InvalidOperationException($"{fieldName} must be {expectedBytes * 2} hex characters.");
            }

            try
            {
                return Convert.FromHexString(normalized);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{fieldName} is not valid hex: {ex.Message}");
            }
        }

        private static void DownloadWithResume(Uri sourceUri, string destinationPath, ManifestEntry entry)
        {
            string tempPath = destinationPath + ".part";
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            for (int attempt = 1; attempt <= MAX_DOWNLOAD_ATTEMPTS; attempt++)
            {
                try
                {
                    long existingLength = File.Exists(tempPath) ? new FileInfo(tempPath).Length : 0;
                    using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);

                    if (existingLength > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(existingLength, null);
                    }

                    using HttpResponseMessage response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);

                    if (existingLength > 0 && response.StatusCode == HttpStatusCode.OK)
                    {
                        existingLength = 0;
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }

                    if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PartialContent)
                    {
                        throw new InvalidOperationException($"Download failed ({(int) response.StatusCode}) for {sourceUri}.");
                    }

                    using Stream sourceStream = response.Content.ReadAsStream();
                    using (FileStream destinationStream = new FileStream(tempPath, FileMode.Append, FileAccess.Write, FileShare.None))
                    {
                        sourceStream.CopyTo(destinationStream);
                    }

                    if (!IsFileCurrent(tempPath, entry.Size, entry.Sha256))
                    {
                        throw new InvalidOperationException($"Downloaded file verification failed for {entry.RelativePath}.");
                    }

                    if (File.Exists(destinationPath))
                    {
                        File.Delete(destinationPath);
                    }

                    File.Move(tempPath, destinationPath);
                    return;
                }
                catch when (attempt < MAX_DOWNLOAD_ATTEMPTS)
                {
                    Log.Warn($"Retrying download ({attempt}/{MAX_DOWNLOAD_ATTEMPTS}) for {entry.RelativePath}");
                }
            }

            throw new InvalidOperationException($"Failed to download {entry.RelativePath} after {MAX_DOWNLOAD_ATTEMPTS} attempts.");
        }

        private static Uri BuildFileUri(Uri manifestUri, string baseUrl, ManifestEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.DirectUrl) && Uri.TryCreate(entry.DirectUrl, UriKind.Absolute, out Uri absoluteDirect))
            {
                return absoluteDirect;
            }

            if (!string.IsNullOrWhiteSpace(entry.DirectUrl) && Uri.TryCreate(manifestUri, entry.DirectUrl, out Uri relativeDirect))
            {
                return relativeDirect;
            }

            if (!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri baseUri))
            {
                return new Uri(baseUri, NormalizeRelativePath(entry.RelativePath));
            }

            // UltimaForge content-addressed style: /files/{sha256}
            // Use this when no explicit file URL/base_url is provided.
            if (!string.IsNullOrWhiteSpace(entry.Sha256))
            {
                Uri updateRoot = new Uri(manifestUri, "./");
                return new Uri(updateRoot, $"files/{entry.Sha256.Trim()}");
            }

            return new Uri(manifestUri, NormalizeRelativePath(entry.RelativePath));
        }

        private static string ResolveDestinationPath(string rootPath, string relativePath)
        {
            string fullRoot = Path.GetFullPath(rootPath);
            string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));

            if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Manifest path '{relativePath}' resolves outside root path.");
            }

            return fullPath;
        }

        private static bool IsFileCurrent(string filePath, long expectedSize, string expectedSha256)
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            var fileInfo = new FileInfo(filePath);

            if (expectedSize > 0 && fileInfo.Length != expectedSize)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                string currentHash = ComputeSha256(filePath);
                return string.Equals(currentHash, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }

        private static string NormalizeRelativePath(string path)
        {
            return path.Replace('\\', '/').TrimStart('/');
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string TryGetString(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return value.ToString();
        }

        private static long TryGetLong(JsonElement element, string name)
        {
            if (!TryGetProperty(element, name, out JsonElement value))
            {
                return 0;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long asLong))
            {
                return asLong;
            }

            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out long parsed))
            {
                return parsed;
            }

            return 0;
        }

        private readonly struct ManifestPayload
        {
            public ManifestPayload(string baseUrl, List<ManifestEntry> entries)
            {
                BaseUrl = baseUrl ?? string.Empty;
                Entries = entries;
            }

            public string BaseUrl { get; }
            public List<ManifestEntry> Entries { get; }
        }

        private readonly struct ManifestEntry
        {
            public ManifestEntry(string relativePath, long size, string sha256, string directUrl)
            {
                RelativePath = relativePath ?? string.Empty;
                Size = size;
                Sha256 = sha256 ?? string.Empty;
                DirectUrl = directUrl ?? string.Empty;
            }

            public string RelativePath { get; }
            public long Size { get; }
            public string Sha256 { get; }
            public string DirectUrl { get; }
        }
    }
}
