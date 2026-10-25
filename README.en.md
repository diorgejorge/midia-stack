# Media Stack

[Português](README.md) · **English** · [中文](README.zh-CN.md)

A Windows tray icon that starts and stops, with one click, a stack of Docker programs that keep track of
TV shows and movies for your **Jellyfin** library:

| Program | What for | Address |
|---|---|---|
| **Seerr** (formerly Jellyseerr) | catalog to request shows and movies | http://localhost:5055 |
| **Sonarr** | TV shows: searches, downloads, renames and organizes | http://localhost:8989 |
| **Radarr** | movies: the same, for movies | http://localhost:7878 |
| **Prowlarr** | manages indexers and passes them to Sonarr/Radarr | http://localhost:9696 |
| **qBittorrent** | does the downloading | http://localhost:8080 |

When stopped, nothing keeps using memory: the app also closes Docker Desktop.

## Installing on a new machine

1. Download `MidiaStack.exe` from the [releases page](../../releases) and put it in its own folder
   (e.g. `C:\MediaStack`). It ships with .NET inside; nothing else to install besides Docker.
2. Open `MidiaStack.exe`. The first time, the **setup wizard** appears:
   - interface **language**: Portuguese, English or Chinese (defaults to the Windows language);
   - **media folder**: the same one that holds your Jellyfin libraries (e.g. `D:\media`), and the names of
     the TV shows, movies and downloads subfolders (created if missing);
   - **simultaneous downloads**, **minimum seeders** and **seeding days** (0 = unlimited for any of them;
     with 0 days a torrent seeds forever and is never removed from the list automatically);
   - **Jellyfin** (optional): address and an API key (*Jellyfin Dashboard → API Keys → +*; the **Test**
     button checks it). With it, finished downloads show up in Jellyfin right away;
   - **Docker Desktop**: if it is not installed, the button installs it through `winget` (asks for
     administrator rights and, at the end, a restart; open Docker Desktop once to accept its terms).
3. Click **Finish**. It starts everything and connects the programs to each other on its own.

## First-time setup, step by step

After the wizard, what only you can do (once; passwords and keys stay with you):

### 1. Indexers (Prowlarr — http://localhost:9696)

*Indexers → Add Indexer*, filter by the **TV** and/or **Movies** category, click the indexer, **Test** and,
if it shows ✓, **Save**. They reach Sonarr and Radarr automatically (movie-only ones go to Radarr only).
To prefer an indexer, change **Indexer Priority** (1 = highest; default is 25) — it only breaks ties
between releases of the same quality.

### 2. Seerr (http://localhost:5055)

1. Choose **Jellyfin**.
2. **Jellyfin URL**: `host.docker.internal` — **without** `http://` (the prefix is already shown next to the
   field) and **not** `localhost` (inside Docker, `localhost` is Seerr itself). **Port**: `8096`.
3. Jellyfin e-mail, user and password → **Sign In** → **Sync Libraries**, tick shows and movies →
   **Continue** → **Finish Setup** (the services step can be skipped).
4. In the Media Stack icon: **Check configuration** — connects Seerr to Sonarr and Radarr.

Seerr creates a Jellyfin API key named **Seerr**: do not delete it.

### 3. Jellyfin API key for Media Stack (optional, recommended)

With it, finished downloads show up in Jellyfin right away.

1. In Jellyfin (http://localhost:8096), as administrator: **Dashboard → Advanced → API Keys → + New API
   key**, name it `Media Stack` (shortcut: http://localhost:8096/web/#/dashboard/keys).
2. In the Media Stack icon: **Settings…** → **Jellyfin** → paste into **API key** → **Test**
   (*connected ✓*) → **Save**.

Use a key just for Media Stack (don't reuse Seerr's). Changing the key in Settings rewrites the connection
in Sonarr and Radarr automatically.

### 4. Permanent qBittorrent password (http://localhost:8080)

1. In the icon: **qBittorrent password** (copies this session's temporary password).
2. Log in with user `admin` and paste the password.
3. *Tools → Options → Web UI → Authentication*: type the new password → **Save**.

Sonarr and Radarr keep logging in without a password (whitelisted IPs).

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Seerr sign-in fails even with the right password | The address became `http://http://…` or `localhost`. Use just `host.docker.internal`, port `8096`. |
| Jellyfin **Test** fails | Wrong key or Jellyfin is off. Check *Dashboard → API Keys*. |
| Nothing downloads / empty queue | No indexers: see *System → Status* in Sonarr/Radarr. Add indexers in Prowlarr. |
| Search finds results but rejects all | Quality profile (e.g. "HD-720p" rejects 1080p), minimum seeders or language. Interactive search (person icon on the episode) shows why each one was rejected. |
| Indexer fails its **Test** | Site blocked by your connection or behind anti-bot protection (Cloudflare). Use another one. |
| Slow downloads | Few connected seeders or many torrents queued. Adjust "Simultaneous downloads" in Settings. |
| Two subtitles show at once | Turn off the **secondary subtitle** in the Jellyfin player; or the file marks English as default. |
| A finished torrent stays in the list | That's seeding: it leaves after the "Seeding days" (only counted while Media Stack is on; 0 = never). |
| Windows says "Windows protected your PC" when opening the .exe | The .exe is not digitally signed: *More info → Run anyway*. |

## Suggestion: automatic subtitles with BabelSubs

[BabelSubs](https://github.com/diorgejorge/jellyfin-plugin-babelsubs) is a Jellyfin plugin: when a video starts
without a subtitle in your language, it translates the video's own English subtitle or downloads the best one
from the installed providers (e.g. OpenSubtitles), and turns it on in the player. It pairs well with Media
Stack, since many releases only come with English subtitles.

In Jellyfin (administrator account): *Dashboard → Plugins → Repositories → +*, paste
`https://raw.githubusercontent.com/diorgejorge/jellyfin-plugin-babelsubs/main/manifest.json`, then
*Catalog → BabelSubs → Install* and restart Jellyfin. The **Automatic subtitles (BabelSubs)…** menu item copies
the address and opens that page; if it is not installed yet, the app also shows the tip once, after the first
start.

## Usage

Click the icon:

| Color | Meaning |
|---|---|
| gray | everything off |
| yellow | working, or only part is running |
| green | everything running |

- **▶ Start all** / **■ Stop all**; double-click opens Seerr.
- **qBittorrent password**: shows and copies the temporary password (it changes on every start until you
  set a permanent one in *Tools → Options → Web UI*).
- **Settings…**: reopens the wizard; if something is running, applies the changes right away.
- **Check configuration**: runs the check again with everything running.
- **Update programs**: downloads new image versions.

## What "Start all" configures on its own

On every start it checks and fixes only what is missing:

- TV shows and movies folders in Sonarr and Radarr;
- qBittorrent as the download client of both, **without a password** only for them (fixed IPs
  `172.30.50.10` and `172.30.50.11` whitelisted in qBittorrent; the browser still asks for a login);
- qBittorrent: downloads folder, port 6881, queue (N simultaneous downloads, slow torrents don't take a
  slot) and **seeding for N days**, after which the torrent is removed from the list;
- import by **hardlink** (the file in the show folder and the seeding torrent use disk space only once);
- Prowlarr connected to Sonarr and Radarr, with the **minimum seeders** and indexers synced;
- with the Jellyfin API key: Sonarr and Radarr notify Jellyfin on import, rename or delete (the connection
  is tested before it is saved; `/data/` is mapped to the Windows media folder);
- Sonarr and Radarr registered in Seerr ("HD - 720p/1080p" profile), after the first sign-in there;
- the programs' *Allowed Hosts*, if set, include `localhost` and the container name.

Headless: `MidiaStack.exe --subir` (the result goes to `midia-stack.log`).
Force the language when opening: `MidiaStack.exe --idioma pt|en|zh`.

## Files in the app folder

| File | What it is |
|---|---|
| `MidiaStack.exe` | the app |
| `midia-stack.json` | the wizard answers |
| `docker-compose.yml`, `.env` | generated by the app from what is embedded in the .exe |
| `config\` | settings and databases of the programs — **take this folder along** when moving to another machine to keep indexers, history and requests |
| `midia-stack.log` | log of what the app ran (no passwords) |

## Linux (Raspberry Pi, mini PC, server)

On an always-on device the stack starts by itself at boot. Works on x64 and ARM64 (Raspberry Pi 4/5).

### With one command (recommended)

```bash
curl -fsSL https://raw.githubusercontent.com/diorgejorge/midia-stack/main/linux/instalar.sh | bash
```

Downloads the right `midia-stack` program for the processor into `~/midia-stack` and opens the same wizard as
on Windows, in the terminal: folders, downloads, seeders, Jellyfin and language (follows `LANG`). If Docker is
not installed, it offers to install it (asks for the `sudo` password). Then it starts everything and connects
the programs on its own, like the `.exe` — including file owner (PUID/PGID) and `host.docker.internal`. At the
end it shows the addresses to open from another device on the network and the qBittorrent password.

Afterwards, `midia-stack` opens a menu; or directly:

```bash
midia-stack up
```

Other commands: `down`, `status`, `password`, `setup`, `update` (also in Portuguese: `subir`, `parar`,
`senha`, `configurar`, `atualizar`). The steps only you can do are the same as in the
[step by step](#first-time-setup-step-by-step), opening the addresses by the device's IP.

### Without the program (Docker Compose only)

**1. Docker** (official script; then log out and back in):

```bash
curl -fsSL https://get.docker.com | sh
```

```bash
sudo usermod -aG docker $USER
```

**2. Stack folder and files:**

```bash
mkdir -p ~/midia-stack && cd ~/midia-stack
```

```bash
curl -fsSLO https://raw.githubusercontent.com/diorgejorge/midia-stack/main/docker-compose.yml
```

```bash
curl -fsSL -o docker-compose.override.yml https://raw.githubusercontent.com/diorgejorge/midia-stack/main/linux/docker-compose.override.yml
```

```bash
curl -fsSL -o .env https://raw.githubusercontent.com/diorgejorge/midia-stack/main/linux/.env.example
```

[`docker-compose.override.yml`](linux/docker-compose.override.yml) adds what Linux needs: file owner
(PUID/PGID), `host.docker.internal` pointing to the server itself, and `restart: unless-stopped`.

**3. Edit `.env`** (`nano .env`): `PASTA_MIDIA` (e.g. `/srv/media`), `PUID`/`PGID` (output of the `id`
command) and `TZ`. Create the folders (here with English names — use the same names in Sonarr/Radarr):

```bash
mkdir -p /srv/media/tv /srv/media/movies /srv/media/downloads
```

**4. Start, stop, update:**

```bash
docker compose up -d
```

```bash
docker compose down
```

```bash
docker compose pull && docker compose up -d
```

**5. Configuration** — on Windows the `.exe` connects the programs; on Linux there are two ways:

- **Bring the Windows configuration (easiest):** stop the stack on Windows and copy the `config\` folder to
  `~/midia-stack/config`. Indexers, connections, history and requests come along. Just change, in Sonarr
  and Radarr, *Settings → Connect → Jellyfin → Map Paths To* to the media folder as Jellyfin sees it on
  Linux (e.g. `/srv/media/`). The subfolder names must match the ones used on Windows.
- **From scratch:** do by hand what the `.exe` does on its own, plus the [step by step](#first-time-setup-step-by-step):

| Program | Where | What |
|---|---|---|
| qBittorrent | *Options → Web UI* | "Bypass authentication for clients in whitelisted IP subnets": `172.30.50.10/32, 172.30.50.11/32` |
| qBittorrent | *Options → Downloads / Connection / BitTorrent* | default path `/data/downloads`, port `6881`, queue and seeding limit (e.g. 7 days, then stop) |
| Sonarr | *Settings → Media Management* | Root Folder `/data/tv` |
| Sonarr | *Settings → Download Clients* | qBittorrent: host `qbittorrent`, port `8080`, category `tv-sonarr` |
| Radarr | the same | Root Folder `/data/movies`, category `radarr` |
| Prowlarr | *Settings → Apps* | Sonarr (`http://prowlarr:9696`, `http://sonarr:8989`, Sonarr API key) and Radarr (`http://radarr:7878`) |
| Prowlarr | *Settings → Apps → Sync Profiles* | minimum seeders |
| Sonarr / Radarr | *Settings → Connect → Emby / Jellyfin* | host `host.docker.internal`, port `8096`, API key, Update Library; Map Paths `/data/` → Jellyfin's media folder |

If your user is not 1000, Seerr (which runs as 1000) needs its folder:
`mkdir -p config/seerr && sudo chown 1000:1000 config/seerr`.

## Building

Requires the .NET 10 SDK.

```bash
dotnet publish src/MidiaStack -c Release -o publish
```

`publish\MidiaStack.exe` is self-contained (a single file with .NET inside). The Linux version (terminal,
same configurator):

```bash
dotnet publish src/MidiaStack.Linux -c Release -r linux-arm64 -o publish-linux
```

(or `-r linux-x64`).

## License

[MIT](LICENSE)
