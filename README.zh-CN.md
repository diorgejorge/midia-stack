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
   - **Docker Desktop**：如果未安装，按钮会通过 `winget` 安装（需要管理员权限，结束时可能需要重启；请先打开一次 Docker Desktop 接受条款）。
3. 点击**完成**。它会启动所有程序并自动把它们互相连接。

之后只有两件事需要你自己做一次：

- **索引器**：在 Prowlarr 中，*Indexers → Add Indexer*（保存前先点 **Test**）。它们会自动同步到 Sonarr 和 Radarr。
- **Seerr**：打开 http://localhost:5055，选择 **Jellyfin**，地址填 `host.docker.internal`，端口 `8096`（不要加 `http://`，不要用 `localhost`），用你的 Jellyfin 账号登录。然后在图标菜单中点击**检查配置**：它会把 Seerr 连接到 Sonarr 和 Radarr。

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

## 编译

需要 .NET 10 SDK。

```bash
dotnet publish src/MidiaStack -c Release -o publish
```

`publish\MidiaStack.exe` 是自包含的（单个文件，内置 .NET）。

## 许可证

[MIT](LICENSE)
