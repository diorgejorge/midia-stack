using System.Diagnostics;

namespace MidiaStack;

/// <summary>
/// Sugere o plugin BabelSubs para o Jellyfin (legendas no idioma do usuário, baixadas ou traduzidas ao dar play).
/// Instalar plugin exige a conta de administrador do Jellyfin, então o app só explica, copia o endereço do
/// repositório e abre a página onde ele é colado.
/// </summary>
internal static class BabelSubs
{
    public const string Manifest = "https://raw.githubusercontent.com/diorgejorge/jellyfin-plugin-babelsubs/main/manifest.json";
    public const string Projeto = "https://github.com/diorgejorge/jellyfin-plugin-babelsubs";
    private const string Jellyfin = "http://localhost:8096";

    /// <summary>Jellyfin instalado no Windows (instalador oficial) guarda os plugins aqui.</summary>
    private static readonly string PastaPlugins =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Jellyfin", "Server", "plugins");

    public static bool Instalado
    {
        get
        {
            try { return Directory.Exists(PastaPlugins) && Directory.GetDirectories(PastaPlugins, "BabelSubs*").Length > 0; }
            catch { return false; }
        }
    }

    public static void Mostrar()
    {
        var titulo = T.S("Legendas automáticas — BabelSubs", "Automatic subtitles — BabelSubs", "自动字幕 — BabelSubs");
        if (Instalado)
        {
            MessageBox.Show(
                T.S("O BabelSubs já está instalado no seu Jellyfin ✓\n\nAs atualizações aparecem em Painel → Plugins.",
                    "BabelSubs is already installed in your Jellyfin ✓\n\nUpdates show up in Dashboard → Plugins.",
                    "你的 Jellyfin 已安装 BabelSubs ✓\n\n更新会显示在 控制台 → 插件 中。"),
                titulo, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var r = MessageBox.Show(
            T.S("Sugestão para o Jellyfin: o plugin BabelSubs.\n\n" +
                "Quando um vídeo começa sem legenda no seu idioma, ele traduz a legenda em inglês do próprio vídeo ou " +
                "baixa a melhor dos provedores instalados (ex.: OpenSubtitles), traduzindo se preciso, e já liga no player.\n\n" +
                "Para instalar (precisa da conta de administrador do Jellyfin):\n" +
                "1. Painel → Plugins → Repositórios → +\n" +
                "2. Cole o endereço (já copiado ao clicar em Sim) e salve\n" +
                "3. Catálogo → BabelSubs → Instalar, e reinicie o Jellyfin\n\n" +
                "Copiar o endereço e abrir o Jellyfin agora?",
                "A suggestion for Jellyfin: the BabelSubs plugin.\n\n" +
                "When a video starts without a subtitle in your language, it translates the video's own English subtitle or " +
                "downloads the best one from the installed providers (e.g. OpenSubtitles), translating it if needed, and turns it on in the player.\n\n" +
                "To install (requires the Jellyfin administrator account):\n" +
                "1. Dashboard → Plugins → Repositories → +\n" +
                "2. Paste the address (copied when you click Yes) and save\n" +
                "3. Catalog → BabelSubs → Install, then restart Jellyfin\n\n" +
                "Copy the address and open Jellyfin now?",
                "给 Jellyfin 的建议：BabelSubs 插件。\n\n" +
                "当视频开始播放却没有你所用语言的字幕时，它会翻译视频自带的英文字幕，" +
                "或从已安装的字幕提供方（如 OpenSubtitles）下载最佳字幕并在需要时翻译，然后在播放器中自动开启。\n\n" +
                "安装方法（需要 Jellyfin 管理员账号）：\n" +
                "1. 控制台 → 插件 → 存储库 → +\n" +
                "2. 粘贴地址（点击“是”时已复制）并保存\n" +
                "3. 目录 → BabelSubs → 安装，然后重启 Jellyfin\n\n" +
                "现在复制地址并打开 Jellyfin 吗？"),
            titulo, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (r != DialogResult.Yes) return;

        try { Clipboard.SetText(Manifest); } catch (Exception e) { Log.Escrever($"copiar: {e.Message}"); }
        Abrir($"{Jellyfin}/web/#/dashboard/plugins/repositories");
    }

    private static void Abrir(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception e) { Log.Escrever($"abrir {url}: {e.Message}"); }
    }
}
