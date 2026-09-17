"""Значки для README: сколько раз скачали установщик и какая версия последняя.

Считаем только USB-of_on-Setup.exe во всех релизах: его качают и люди,
и сама программа при обновлении — то есть число показывает, сколько раз
программу поставили или обновили.
"""
import json
import os
import urllib.request

REPO = "AveHarrisan/USB-of_on"
ASSET = "USB-of_on-Setup.exe"
OUT = os.path.join(os.path.dirname(__file__), "..", "docs", "badges")


def get(url):
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "USB-of_on"}
    token = os.environ.get("GH_TOKEN")
    if token:
        headers["Authorization"] = "Bearer " + token
    with urllib.request.urlopen(urllib.request.Request(url, headers=headers)) as r:
        return json.load(r)


def badge(name, label, message, color):
    with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
        json.dump({"schemaVersion": 1, "label": label, "message": message, "color": color},
                  f, ensure_ascii=False, indent=2)
        f.write("\n")


def main():
    releases, page = [], 1
    while True:
        chunk = get(f"https://api.github.com/repos/{REPO}/releases?per_page=100&page={page}")
        releases += chunk
        if len(chunk) < 100:
            break
        page += 1

    downloads = sum(a["download_count"] for r in releases for a in r.get("assets", []) if a["name"] == ASSET)
    published = [r for r in releases if not r["draft"] and not r["prerelease"]
                 and any(a["name"] == ASSET for a in r.get("assets", []))]
    version = published[0]["tag_name"] if published else "—"

    os.makedirs(OUT, exist_ok=True)
    badge("downloads.json", "Загрузок", f"{downloads:,}".replace(",", " "), "brightgreen")
    badge("version.json", "Версия", version, "blue")
    print(f"загрузок: {downloads}, версия: {version}")


if __name__ == "__main__":
    main()
