﻿using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using TableCloth.Models.Catalog;
using TableCloth.Resources;

namespace TableCloth.Components
{
    public sealed class ResourceResolver
    {
        public ResourceResolver(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        private readonly IHttpClientFactory _httpClientFactory;

        public async Task<string> GetLatestVersion(string owner, string repoName)
        {
            var targetUri = new Uri($"https://api.github.com/repos/{owner}/{repoName}/releases/latest", UriKind.Absolute);
            var httpClient = _httpClientFactory.CreateTableClothHttpClient();

            using var licenseDescription = await httpClient.GetStreamAsync(targetUri);
            var jsonDocument = await JsonDocument.ParseAsync(licenseDescription).ConfigureAwait(false);
            return jsonDocument.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
        }

        public async Task<Uri> GetDownloadUrl(string owner, string repoName)
        {
            var targetUri = new Uri($"https://api.github.com/repos/{owner}/{repoName}/releases/latest", UriKind.Absolute);
            var httpClient = _httpClientFactory.CreateTableClothHttpClient();

            using var licenseDescription = await httpClient.GetStreamAsync(targetUri);
            var jsonDocument = await JsonDocument.ParseAsync(licenseDescription).ConfigureAwait(false);

            if (Uri.TryCreate(jsonDocument.RootElement.GetProperty("html_url").GetString(), UriKind.Absolute, out var result))
                return result;
            else
                return new Uri($"https://github.com/{owner}/{repoName}/releases", UriKind.Absolute);
        }

        public async Task<string> GetLicenseDescriptionForGitHub(string owner, string repoName)
        {
            var targetUri = new Uri($"https://api.github.com/repos/{owner}/{repoName}/license", UriKind.Absolute);
            var httpClient = _httpClientFactory.CreateTableClothHttpClient();

            using var licenseDescription = await httpClient.GetStreamAsync(targetUri);
            var jsonDocument = await JsonDocument.ParseAsync(licenseDescription).ConfigureAwait(false);
            return jsonDocument.RootElement.GetProperty("license").GetProperty("name").GetString();
        }

        public async Task LoadSiteImages(IHttpClientFactory httpClientFactory, List<CatalogInternetService> services, string imageDirectoryPath)
        {
            if (!Directory.Exists(imageDirectoryPath))
                Directory.CreateDirectory(imageDirectoryPath);

            var httpClient = httpClientFactory.CreateTableClothHttpClient();

            foreach (var eachSite in services)
            {
                var targetFilePath = Path.Combine(imageDirectoryPath, eachSite.Id + ".png");

                if (!File.Exists(targetFilePath))
                {
                    try
                    {
                        var targetUrl = $"{StringResources.ImageUrlPrefix}/{eachSite.Category}/{eachSite.Id}.png";
                        var imageStream = await httpClient.GetStreamAsync(targetUrl);

                        using var fileStream = File.OpenWrite(targetFilePath);
                        await imageStream.CopyToAsync(fileStream);
                    }
                    catch
                    {
                        try { File.WriteAllBytes(targetFilePath, Properties.Resources.SandboxIcon); }
                        catch { }
                    }
                }

                var targetIconFilePath = Path.Combine(
                    Path.GetDirectoryName(targetFilePath),
                    Path.GetFileNameWithoutExtension(targetFilePath) + ".ico");

                if (!File.Exists(targetIconFilePath))
                {
                    try
                    {
                        await File.WriteAllBytesAsync(targetIconFilePath, ConvertImageToIcon(targetFilePath));
                    }
                    catch
                    {
                        var memStream = new MemoryStream();
                        Properties.Resources.SandboxIconWin32.Save(memStream);
                        memStream.Seek(0L, SeekOrigin.Begin);

                        try { File.WriteAllBytes(targetIconFilePath, memStream.ToArray()); }
                        catch { }
                    }
                }
            }
        }

        // https://stackoverflow.com/questions/21387391/how-to-convert-an-image-to-an-icon-without-losing-transparency
        private byte[] ConvertImageToIcon(string imageFilePath)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            using (var fs = File.OpenRead(imageFilePath))
            using (var img = System.Drawing.Image.FromStream(fs))
            {
                // Header
                bw.Write((short)0);   // 0 : reserved
                bw.Write((short)1);   // 2 : 1=ico, 2=cur
                bw.Write((short)1);   // 4 : number of images

                // Image directory
                var w = img.Width;
                if (w >= 256) w = 0;
                bw.Write((byte)w);    // 0 : width of image

                var h = img.Height;
                if (h >= 256) h = 0;
                bw.Write((byte)h);    // 1 : height of image

                bw.Write((byte)0);    // 2 : number of colors in palette
                bw.Write((byte)0);    // 3 : reserved
                bw.Write((short)0);   // 4 : number of color planes
                bw.Write((short)0);   // 6 : bits per pixel

                var sizeHere = ms.Position;
                bw.Write(0);     // 8 : image size

                var start = (int)ms.Position + 4;
                bw.Write(start);      // 12: offset of image data

                // Image data
                img.Save(ms, ImageFormat.Png);
                var imageSize = (int)ms.Position - start;
                ms.Seek(sizeHere, SeekOrigin.Begin);
                bw.Write(imageSize);
                ms.Seek(0L, SeekOrigin.Begin);

                return ms.ToArray();
            }
        }
    }
}
