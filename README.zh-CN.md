# Media Stack

[Português](README.md) · [English](README.en.md) · **中文**

一个 Windows 托盘图标，一键启动和停止一组运行在 Docker 中的程序，为你的 **Jellyfin** 媒体库追踪剧集和电影：

| 程序 | 用途 | 地址 |
|---|---|---|
| **Seerr**（原 Jellyseerr） | 请求剧集和电影的目录 | http://localhost:5055 |
| **Sonarr** | 剧集：搜索、下载、重命名并整理 | http://localhost:8989 |
| **Radarr** | 电影：同上，用于电影 | http://localhost:7878 |
| **Prowlarr** | 管理索引器并同步给 Sonarr/Radarr | http://localhost:9696 |
| **qBittorrent** | 负责下载 | http://localhost:8080 |

停止后不再占用任何内存：程序也会关闭 Docker Desktop。

## 在新电脑上安装

1. 从[发布页面](../../releases)下载 `MidiaStack.exe`，放到单独的文件夹中（例如 `C:\MediaStack`）。
   它已内置 .NET；除了 Docker 之外无需安装其他东西。
2. 打开 `MidiaStack.exe`。第一次运行时会出现**安装向导**：
   - 界面**语言**：葡萄牙语、英语或中文（默认跟随 Windows 语言）；
   - **媒体文件夹**：与 Jellyfin 媒体库相同的文件夹（例如 `D:\media`），以及剧集、电影和下载子文件夹的名称（不存在时会自动创建）；
   - **同时下载数**、**最少做种数**和**做种天数**（任一项填 0 = 不限制；做种天数为 0 时种子会永久做种，不会自动从列表中移除）；
   - **Jellyfin**（可选）：地址和 API 密钥（*Jellyfin 控制台 → API 密钥 → +*；**测试**按钮可以检查）。有了它，下载完成的内容会立即出现在 Jellyfin 中；
   - **Docker Desktop**：如果未安装，按钮会通过 `winget` 安装（需要管理员权限，结束时可能需要重启；请先打开一次 Docker Desktop 接受条款）。
3. 点击**完成**。它会启动所有程序并自动把它们互相连接。

## 首次配置步骤

向导完成后，以下事项只能由你自己完成（一次即可；密码和密钥由你保管）：

### 1. 索引器（Prowlarr — http://localhost:9696）

*Indexers → Add Indexer*，按 **TV** 和/或 **Movies** 分类筛选，点击索引器，**Test**，显示 ✓ 后 **Save**。它们会自动同步到 Sonarr 和 Radarr（只有电影的索引器只同步到 Radarr）。如需优先使用某个索引器，修改 **Indexer Priority**（1 = 最高；默认 25）——它只在质量相同的资源之间起决定作用。

### 2. Seerr（http://localhost:5055）

1. 选择 **Jellyfin**。
2. **Jellyfin URL**：`host.docker.internal` —— **不要**加 `http://`（输入框旁已显示该前缀），也**不要**用 `localhost`（在 Docker 内，`localhost` 指的是 Seerr 自己）。**Port**：`8096`。
3. 填写 Jellyfin 的邮箱、用户名和密码 → **Sign In** → **Sync Libraries**，勾选剧集和电影 → **Continue** → **Finish Setup**（服务步骤可以跳过）。
4. 在 Media Stack 图标中：**检查配置** —— 把 Seerr 连接到 Sonarr 和 Radarr。

Seerr 会在 Jellyfin 中创建一个名为 **Seerr** 的 API 密钥：请不要删除。

### 3. 为 Media Stack 创建 Jellyfin API 密钥（可选，推荐）

有了它，下载完成的内容会立即出现在 Jellyfin 中。

1. 在 Jellyfin（http://localhost:8096）中以管理员身份：**控制台 → 高级 → API 密钥 → + 新建 API 密钥**，命名为 `Media Stack`（快捷方式：http://localhost:8096/web/#/dashboard/keys）。
2. 在 Media Stack 图标中：**设置…** → **Jellyfin** → 粘贴到 **API 密钥** → **测试**（*已连接 ✓*）→ **保存**。

请为 Media Stack 单独使用一个密钥（不要复用 Seerr 的）。在设置中更换密钥后，会自动重写 Sonarr 和 Radarr 中的连接。

### 4. qBittorrent 固定密码（http://localhost:8080）

1. 在图标中：**qBittorrent 密码**（复制本次会话的临时密码）。
2. 用用户 `admin` 登录并粘贴密码。
3. *工具 → 选项 → Web UI → 验证*：输入新密码 → **保存**。

Sonarr 和 Radarr 仍然无需密码登录（白名单 IP）。

## 常见问题

| 现象 | 原因与解决 |
|---|---|
| 密码正确但无法登录 Seerr | 地址变成了 `http://http://…` 或 `localhost`。只填 `host.docker.internal`，端口 `8096`。 |
| Jellyfin **测试**失败 | 密钥错误或 Jellyfin 未运行。请在 *控制台 → API 密钥* 中检查。 |
| 没有任何下载 / 队列为空 | 没有索引器：查看 Sonarr/Radarr 的 *System → Status*。在 Prowlarr 中添加索引器。 |
| 搜索到结果却全部被拒绝 | 质量配置（例如 "HD-720p" 会拒绝 1080p）、最少做种数或语言。交互式搜索（剧集旁的人形图标）会显示每个结果被拒绝的原因。 |
| 索引器 **Test** 失败 | 网站被你的网络屏蔽，或有反机器人保护（Cloudflare）。请换一个。 |
| 下载很慢 | 连接的做种者太少，或队列中种子太多。在设置中调整“同时下载数”。 |
| 同时出现两条字幕 | 在 Jellyfin 播放器中关闭**次要字幕**；或者文件把英文字幕设为默认。 |
| 已完成的种子没有从列表移除 | 这是在做种：超过“做种天数”后才会移除（只在 Media Stack 运行时计时；0 = 永不移除）。 |
| 打开 .exe 时 Windows 提示“Windows 已保护你的电脑” | 该 .exe 没有数字签名：*更多信息 → 仍要运行*。 |

## 推荐：用 BabelSubs 自动获取字幕

[BabelSubs](https://github.com/diorgejorge/jellyfin-plugin-babelsubs) 是一个 Jellyfin 插件：当视频开始播放却没有你所用语言的字幕时，它会翻译视频自带的英文字幕，或从已安装的字幕提供方（如 OpenSubtitles）下载最佳字幕，并在播放器中自动开启。它与 Media Stack 很搭配，因为很多资源只带英文字幕。

在 Jellyfin 中（管理员账号）：*控制台 → 插件 → 存储库 → +*，粘贴
`https://raw.githubusercontent.com/diorgejorge/jellyfin-plugin-babelsubs/main/manifest.json`，然后
*目录 → BabelSubs → 安装*，并重启 Jellyfin。菜单中的**自动字幕（BabelSubs）…**会复制该地址并打开这个页面；如果尚未安装，程序也会在首次启动后提示一次。

## 使用

点击图标：

| 颜色 | 含义 |
|---|---|
| 灰色 | 全部关闭 |
| 黄色 | 正在处理，或只有部分在运行 |
| 绿色 | 全部运行中 |

- **▶ 全部启动** / **■ 全部停止**；双击打开 Seerr。
- **qBittorrent 密码**：显示并复制临时密码（每次启动都会变化，直到你在 *工具 → 选项 → Web UI* 中设置固定密码）。
- **设置…**：重新打开向导；如果有程序在运行，会立即应用更改。
- **检查配置**：在全部运行时重新检查一遍。
- **更新程序**：下载新版本的镜像。

## “全部启动”会自动配置的内容

每次启动时只检查并修复缺少的部分：

- Sonarr 和 Radarr 中的剧集和电影文件夹；
- qBittorrent 作为两者的下载客户端，**仅对它们免密码**（qBittorrent 白名单中的固定 IP `172.30.50.10` 和 `172.30.50.11`；浏览器访问仍需登录）；
- qBittorrent：下载文件夹、端口 6881、队列（N 个同时下载，慢速种子不占名额）以及**做种 N 天**，之后种子会从列表中移除；
- 通过**硬链接**导入（剧集文件夹中的文件和正在做种的文件只占用一份磁盘空间）；
- Prowlarr 连接 Sonarr 和 Radarr，同步**最少做种数**和索引器；
- 填写 Jellyfin API 密钥后：Sonarr 和 Radarr 在导入、重命名或删除时通知 Jellyfin（保存前会先测试连接；`/data/` 会映射到 Windows 媒体文件夹）；
- 首次登录 Seerr 后，把 Sonarr 和 Radarr 注册到 Seerr（“HD - 720p/1080p”配置）；
- 各程序的 *Allowed Hosts* 如已设置，会包含 `localhost` 和容器名称。

无界面运行：`MidiaStack.exe --subir`（结果写入 `midia-stack.log`）。
打开时强制指定语言：`MidiaStack.exe --idioma pt|en|zh`。

## 程序文件夹中的文件

| 文件 | 说明 |
|---|---|
| `MidiaStack.exe` | 程序本身 |
| `midia-stack.json` | 向导的回答 |
| `docker-compose.yml`、`.env` | 由程序根据 .exe 内嵌内容生成 |
| `config\` | 各程序的设置和数据库 — 换电脑时**一起带走**，即可保留索引器、历史记录和请求 |
| `midia-stack.log` | 程序执行记录（不含密码） |

## Linux（树莓派、迷你主机、服务器）

`MidiaStack.exe` 仅适用于 Windows。在 Linux 上，同一套程序直接用 Docker Compose 运行 —— 在常开设备上会随开机自动启动。镜像提供 ARM64 版本（树莓派 4/5）。

**1. Docker**（官方脚本；之后注销并重新登录）：

```bash
curl -fsSL https://get.docker.com | sh
```

```bash
sudo usermod -aG docker $USER
```

**2. 程序文件夹和文件：**

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

[`docker-compose.override.yml`](linux/docker-compose.override.yml) 补充了 Linux 所需的内容：文件所有者（PUID/PGID）、指向服务器本身的 `host.docker.internal`，以及 `restart: unless-stopped`。

**3. 编辑 `.env`**（`nano .env`）：`PASTA_MIDIA`（例如 `/srv/media`）、`PUID`/`PGID`（`id` 命令的输出）和 `TZ`。创建文件夹（此处使用英文名称 —— 在 Sonarr/Radarr 中使用相同名称）：

```bash
mkdir -p /srv/media/tv /srv/media/movies /srv/media/downloads
```

**4. 启动、停止、更新：**

```bash
docker compose up -d
```

```bash
docker compose down
```

```bash
docker compose pull && docker compose up -d
```

**5. 配置** —— 在 Windows 上由 `.exe` 连接各程序；在 Linux 上有两种方式：

- **沿用 Windows 的配置（最简单）：** 在 Windows 上停止程序，把 `config\` 文件夹复制到 `~/midia-stack/config`。索引器、连接、历史记录和请求都会一起迁移。只需在 Sonarr 和 Radarr 中把 *Settings → Connect → Jellyfin → Map Paths To* 改为 Jellyfin 在 Linux 上看到的媒体文件夹（例如 `/srv/media/`）。子文件夹名称必须与 Windows 上使用的一致。
- **从零开始：** 手动完成 `.exe` 自动做的事情，另外参考[首次配置步骤](#首次配置步骤)：

| 程序 | 位置 | 内容 |
|---|---|---|
| qBittorrent | *选项 → Web UI* | “对白名单 IP 子网中的客户端跳过身份验证”：`172.30.50.10/32, 172.30.50.11/32` |
| qBittorrent | *选项 → 下载 / 连接 / BitTorrent* | 默认路径 `/data/downloads`，端口 `6881`，队列和做种限制（例如 7 天后停止） |
| Sonarr | *Settings → Media Management* | Root Folder `/data/tv` |
| Sonarr | *Settings → Download Clients* | qBittorrent：主机 `qbittorrent`，端口 `8080`，分类 `tv-sonarr` |
| Radarr | 同上 | Root Folder `/data/movies`，分类 `radarr` |
| Prowlarr | *Settings → Apps* | Sonarr（`http://prowlarr:9696`、`http://sonarr:8989`、Sonarr 的 API 密钥）和 Radarr（`http://radarr:7878`） |
| Prowlarr | *Settings → Apps → Sync Profiles* | 最少做种数 |
| Sonarr / Radarr | *Settings → Connect → Emby / Jellyfin* | 主机 `host.docker.internal`，端口 `8096`，API 密钥，Update Library；Map Paths `/data/` → Jellyfin 的媒体文件夹 |

如果你的用户 ID 不是 1000，Seerr（以 1000 运行）需要拥有它的文件夹：
`mkdir -p config/seerr && sudo chown 1000:1000 config/seerr`。

## 编译

需要 .NET 10 SDK。

```bash
dotnet publish src/MidiaStack -c Release -o publish
```

`publish\MidiaStack.exe` 是自包含的（单个文件，内置 .NET）。

## 许可证

[MIT](LICENSE)
