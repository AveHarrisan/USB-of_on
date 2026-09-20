using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
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



        /// <summary>
        /// Свежий выпуск, если он новее установленного; иначе null.
        /// Версию читаем из файла в репозитории, а не через API GitHub: у API есть ограничение
        /// на число обращений с одного адреса, из-за которого проверка падала с ошибкой 403.
        /// </summary>
        public static async Task<ReleaseInfo> CheckAsync()
        {
            // Сначала спрашиваем API — в нём есть описание выпуска и размер файла.
            try
            {
                var byApi = await CheckByApiAsync().ConfigureAwait(false);
                if (byApi != null) return Normalize(byApi.Version) > Normalize(AppInfo.Version) ? byApi : null;
            }
            catch
            {
                // API недоступен или исчерпан предел обращений — идём запасным путём.
            }

            var version = await CheckByFileAsync().ConfigureAwait(false);
            if (version == null || Normalize(version) <= Normalize(AppInfo.Version)) return null;

            return new ReleaseInfo
            {
                Version = version,
                Notes = await NotesAsync(version).ConfigureAwait(false),
                PageUrl = "https://github.com/" + AppInfo.Repository + "/releases/latest",
                SetupUrl = "https://github.com/" + AppInfo.Repository + "/releases/latest/download/" + AppInfo.SetupAsset,
                SetupSize = 0,
            };
        }

        /// <summary>Проверка через API GitHub: даёт описание выпуска и размер установщика.</summary>
        private static async Task<ReleaseInfo> CheckByApiAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/" + AppInfo.Repository + "/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using (var response = await Http.SendAsync(request).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                GhRelease release;
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    release = (GhRelease)new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(stream);

                var asset = release.Assets?.FirstOrDefault(a =>
                    string.Equals(a.Name, AppInfo.SetupAsset, StringComparison.OrdinalIgnoreCase));
                if (release.Draft || release.Prerelease || asset == null) return null;
                if (!Version.TryParse((release.TagName ?? "").TrimStart('v', 'V'), out var version)) return null;

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

        /// <summary>Запасной путь: номер версии лежит файлом в репозитории, предела обращений там нет.</summary>
        private static async Task<Version> CheckByFileAsync()
        {
            var text = await GetTextAsync(Raw + "docs/badges/version.json").ConfigureAwait(false);
            var match = Regex.Match(text ?? "", @"""message""\s*:\s*""v?([0-9]+(?:\.[0-9]+){1,3})""");
            return match.Success && Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
        }

        /// <summary>Что нового — берём раздел этой версии из CHANGELOG.md.</summary>
        private static async Task<string> NotesAsync(Version version)
        {
            var text = await GetTextAsync(Raw + "CHANGELOG.md").ConfigureAwait(false);
            if (string.IsNullOrEmpty(text)) return null;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            var notes = new System.Text.StringBuilder();
            var inside = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("## "))
                {
                    if (inside) break;
                    inside = line.Substring(3).Trim() == version.ToString(3);
                    continue;
                }
                if (inside && line.Trim().Length > 0) notes.AppendLine(line.Trim());
            }
            return notes.Length > 0 ? notes.ToString().Trim() : null;
        }

        private static async Task<string> GetTextAsync(string url)
        {
            try
            {
                using (var response = await Http.GetAsync(url).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException(Explain(response.StatusCode));
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException("Нет связи с GitHub. " + ex.Message);
            }
        }

        private static string Explain(HttpStatusCode code)
        {
            switch ((int)code)
            {
                case 403:
                case 429: return "GitHub временно ограничил число обращений — попробуйте позже.";
                case 404: return "Файл с версией не найден в репозитории.";
                default: return "GitHub ответил: " + (int)code + ".";
            }
        }

        private const string Raw = "https://raw.githubusercontent.com/AveHarrisan/USB-of_on/main/";

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
