using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS0649 // поля заполняет разбор JSON

namespace USBofon
{
    public sealed class ReleaseInfo
    {
        public Version Version;
        public string Notes;
        public string PageUrl;
        public string SetupUrl;
        public long SetupSize;
    }

    /// <summary>
    /// Обновление из GitHub Releases: берём последний выпуск, качаем установщик во временную папку
    /// и запускаем его в тихом режиме. Установщик сам закрывает программу, ставит новую версию и запускает её.
    /// </summary>
    internal static class Updater
    {
        private static readonly HttpClient Http;

        public static string DownloadDir => Path.Combine(Path.GetTempPath(), "USB-of_on-update");

        static Updater()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            Http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.Name + "/" + AppInfo.VersionText);
        }

        [DataContract]
        private sealed class GhRelease
        {
            [DataMember(Name = "tag_name")] public string TagName;
            [DataMember(Name = "body")] public string Body;
            [DataMember(Name = "html_url")] public string HtmlUrl;
            [DataMember(Name = "draft")] public bool Draft;
            [DataMember(Name = "prerelease")] public bool Prerelease;
            [DataMember(Name = "assets")] public GhAsset[] Assets;
        }

        [DataContract]
        private sealed class GhAsset
        {
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "browser_download_url")] public string Url;
            [DataMember(Name = "size")] public long Size;
        }

        /// <summary>Свежий выпуск, если он новее установленного; иначе null.</summary>
        public static async Task<ReleaseInfo> CheckAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/" + AppInfo.Repository + "/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using (var response = await Http.SendAsync(request).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new InvalidOperationException("Сведения о версиях на GitHub недоступны.");
                response.EnsureSuccessStatusCode();
                GhRelease release;
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    release = (GhRelease)new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(stream);

                var asset = release.Assets?.FirstOrDefault(a => string.Equals(a.Name, AppInfo.SetupAsset, StringComparison.OrdinalIgnoreCase));
                if (release.Draft || release.Prerelease || asset == null) return null;
                if (!Version.TryParse((release.TagName ?? "").TrimStart('v', 'V'), out var version)) return null;
                if (Normalize(version) <= Normalize(AppInfo.Version)) return null;

                return new ReleaseInfo
                {
                    Version = version,
                    Notes = release.Body,
                    PageUrl = release.HtmlUrl,
                    SetupUrl = asset.Url,
                    SetupSize = asset.Size,
                };
            }
        }

        public static async Task<string> DownloadAsync(ReleaseInfo release, IProgress<int> progress, CancellationToken ct)
        {
            Cleanup();
            Directory.CreateDirectory(DownloadDir);
            var path = Path.Combine(DownloadDir, "USB-of_on-Setup-" + release.Version.ToString(3) + ".exe");
            try
            {
                using (var response = await Http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? release.SetupSize;
                    using (var src = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = File.Create(path))
                    {
                        var buffer = new byte[81920];
                        long done = 0;
                        int read;
                        while ((read = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                            done += read;
                            if (total > 0) progress?.Report((int)(done * 100 / total));
                        }
                    }
                }
                if (release.SetupSize > 0 && new FileInfo(path).Length != release.SetupSize)
                    throw new IOException("Файл скачался не полностью. Попробуйте ещё раз.");
                return path;
            }
            catch
            {
                Cleanup();
                throw;
            }
        }

        /// <summary>Запускает скачанный установщик: он закроет программу, обновит её и откроет снова.</summary>
        public static void RunInstaller(string setupPath)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setupPath,
                "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH")
            {
                UseShellExecute = true,
            });
        }

        /// <summary>Убирает скачанные установщики. Зовётся при запуске: к этому моменту обновление уже поставлено.</summary>
        public static void Cleanup()
        {
            try
            {
                if (Directory.Exists(DownloadDir)) Directory.Delete(DownloadDir, true);
            }
            catch
            {
                // Установщик ещё может быть открыт — уберём при следующем запуске.
            }
        }

        private static Version Normalize(Version v) =>
            new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }
}
