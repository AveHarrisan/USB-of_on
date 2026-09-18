using System;
using System.Diagnostics;
using System.Reflection;

namespace USBofon
{
    internal static class AppInfo
    {
        public const string Name = "USB-of_on";
        public const string Repository = "AveHarrisan/USB-of_on";
        public const string SetupAsset = "USB-of_on-Setup.exe";

        public static readonly Version Version = Assembly.GetExecutingAssembly().GetName().Version;
        public static string VersionText => Version.ToString(3);

        public sealed class Link
        {
            public string Title;
            public string Hint;
            public string Url;
            public string Image;
        }

        public static readonly Link[] Support =
        {
            new Link { Title = "Boosty", Hint = "разово или подпиской", Url = "https://boosty.to/aveharrisan", Image = "boosty" },
            new Link { Title = "DonationAlerts", Hint = "разовый донат", Url = "https://www.donationalerts.com/r/aveharrisan", Image = "donationalerts" },
        };

        public static readonly Link[] FindMe =
        {
            new Link { Title = "AveHarrisan", Hint = "телеграм автора", Url = "https://t.me/aveharrisan", Image = "aveharrisan" },
            new Link { Title = "GitHub", Hint = "страница программы и все версии", Url = "https://github.com/" + Repository, Image = "github" },
            new Link { Title = "Котамарин", Hint = "канал про игры и раздачи", Url = "https://t.me/kotamarine", Image = "kotamarine" },
            new Link { Title = "lvl.su", Hint = "гайды и вики", Url = "https://lvl.su/", Image = "lvl" },
            new Link { Title = "Discord", Hint = "вопросы и ошибки", Url = "https://discord.com/invite/XYBvdvfv8t", Image = "discord" },
        };

        public static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }
    }
}
