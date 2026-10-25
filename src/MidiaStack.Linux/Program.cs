using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MidiaStack;

/// <summary>
/// Mídia Stack para Linux, no terminal: na primeira execução faz o assistente, instala o Docker se faltar,
/// sobe a pilha e liga os programas (mesmo Configurador do Windows). Depois, menu ou comandos diretos:
/// midia-stack [subir|parar|status|senha|configurar|atualizar] [--idioma pt|en|zh]
/// </summary>
internal static class Programa
{
    private record Servico(string Nome, int Porta);

    private static readonly Servico[] Servicos =
    [
        new("Seerr", 5055), new("Sonarr", 8989), new("Radarr", 7878), new("Prowlarr", 9696), new("qBittorrent", 8080),
    ];

    private static string _pasta = "";
    private static Docker _docker = null!;
    private static Ajustes _ajustes = null!;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        _pasta = AppContext.BaseDirectory.TrimEnd('/');
        _docker = new Docker(_pasta);

        var salvos = Ajustes.Carregar(_pasta);
        var forcado = args.SkipWhile(a => a != "--idioma").Skip(1).FirstOrDefault();
        T.Definir(forcado ?? salvos?.Idioma);
        var comando = args.Where(a => !a.StartsWith("--") && a != forcado).FirstOrDefault()?.ToLowerInvariant();

        Titulo($"{T.App} {typeof(Programa).Assembly.GetName().Version?.ToString(3)}");

        // Primeira execução nesta pasta: assistente, Docker e subida.
        if (salvos == null)
        {
            Console.WriteLine(T.S(
                "Primeira execução: responda algumas perguntas (Enter aceita o valor entre colchetes).",
                "First run: answer a few questions (Enter accepts the value in brackets).",
                "首次运行：请回答几个问题（按 Enter 接受方括号中的值）。"));
            _ajustes = Assistente(Ajustes.Novos(T.Atual), primeiraVez: true);
            _ajustes.Salvar(_pasta);
            _ajustes.PrepararPasta(_pasta);
            if (!GarantirDocker()) return 1;
            return await SubirAsync() ? 0 : 1;
        }

        _ajustes = salvos;
        switch (comando)
        {
            case "subir" or "up" or "start": return await SubirAsync() ? 0 : 1;
            case "parar" or "down" or "stop": return await PararAsync() ? 0 : 1;
            case "status": await StatusAsync(); return 0;
            case "senha" or "password": await SenhaAsync(); return 0;
            case "configurar" or "setup" or "settings": await ConfigurarAsync(); return 0;
            case "atualizar" or "update": return await AtualizarAsync() ? 0 : 1;
            case null: await MenuAsync(); return 0;
            default:
                Console.WriteLine(T.S(
                    "Uso: midia-stack [subir|parar|status|senha|configurar|atualizar] [--idioma pt|en|zh]",
                    "Usage: midia-stack [up|down|status|password|setup|update] [--idioma pt|en|zh]",
                    "用法：midia-stack [up|down|status|password|setup|update] [--idioma pt|en|zh]"));
                return 2;
        }
    }

    // ---------- menu ----------

    private static async Task MenuAsync()
    {
        while (true)
        {
            Console.WriteLine();
            await StatusAsync();
            Console.WriteLine();
            Console.WriteLine(T.S("  1) Subir tudo / conferir configuração", "  1) Start all / check configuration", "  1) 全部启动 / 检查配置"));
            Console.WriteLine(T.S("  2) Parar tudo", "  2) Stop all", "  2) 全部停止"));
            Console.WriteLine(T.S("  3) Senha do qBittorrent", "  3) qBittorrent password", "  3) qBittorrent 密码"));
            Console.WriteLine(T.S("  4) Configurações", "  4) Settings", "  4) 设置"));
            Console.WriteLine(T.S("  5) Atualizar programas", "  5) Update programs", "  5) 更新程序"));
            Console.WriteLine(T.S("  0) Sair", "  0) Exit", "  0) 退出"));
            switch (Perguntar(T.S("Escolha", "Choose", "请选择"), "0"))
            {
                case "1": await SubirAsync(); break;
                case "2": await PararAsync(); break;
                case "3": await SenhaAsync(); break;
                case "4": await ConfigurarAsync(); break;
                case "5": await AtualizarAsync(); break;
                case "0": return;
            }
        }
    }

    // ---------- ações ----------

    private static async Task<bool> SubirAsync()
    {
        if (!GarantirDocker()) return false;
        var configurador = new Configurador(_docker, _ajustes, msg => Console.WriteLine("  … " + msg));
        var (ok, mensagem) = await configurador.SubirEConfigurarAsync();
        Console.WriteLine();
        Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine((ok ? "✓ " : "✗ ") + mensagem.Trim());
        Console.ResetColor();
        if (!ok) return false;

        Console.WriteLine();
        Enderecos();
        if (await _docker.SenhaTemporariaQbitAsync() is { } senha)
            Console.WriteLine(T.S($"\nqBittorrent: usuário admin, senha temporária {senha}",
                                  $"\nqBittorrent: user admin, temporary password {senha}",
                                  $"\nqBittorrent：用户 admin，临时密码 {senha}"));
        DicaBabelSubs();
        return true;
    }

    private static async Task<bool> PararAsync()
    {
        if (!await _docker.EsperarMotorAsync(TimeSpan.FromSeconds(10))) return true;
        var (codigo, saida) = await _docker.DerrubarAsync();
        Console.WriteLine(codigo == 0
            ? T.S("✓ Tudo parado.", "✓ All stopped.", "✓ 已全部停止。")
            : "✗ " + saida.Split('\n').LastOrDefault());
        return codigo == 0;
    }

    private static async Task StatusAsync()
    {
        var ip = IpDaRede();
        var noAr = await Task.WhenAll(Servicos.Select(s => Docker.PortaAbertaAsync(s.Porta)));
        for (var i = 0; i < Servicos.Length; i++)
        {
            Console.ForegroundColor = noAr[i] ? ConsoleColor.Green : ConsoleColor.DarkGray;
            Console.Write(noAr[i] ? "  ● " : "  ○ ");
            Console.ResetColor();
            Console.WriteLine($"{Servicos[i].Nome,-12} http://{ip}:{Servicos[i].Porta}");
        }
    }

    private static async Task SenhaAsync()
    {
        var senha = Docker.DesktopInstalado ? await _docker.SenhaTemporariaQbitAsync() : null;
        Console.WriteLine(senha != null
            ? T.S($"qBittorrent (http://{IpDaRede()}:8080): usuário admin, senha temporária {senha}\n" +
                  "Ela muda a cada início; para uma fixa: Ferramentas → Opções → Web UI.",
                  $"qBittorrent (http://{IpDaRede()}:8080): user admin, temporary password {senha}\n" +
                  "It changes on every start; for a permanent one: Tools → Options → Web UI.",
                  $"qBittorrent（http://{IpDaRede()}:8080）：用户 admin，临时密码 {senha}\n" +
                  "每次启动都会变化；如需固定密码：工具 → 选项 → Web UI。")
            : T.S("Nenhuma senha temporária: o qBittorrent está desligado ou já tem senha fixa.",
                  "No temporary password: qBittorrent is off or already has a permanent password.",
                  "没有临时密码：qBittorrent 未运行或已设置固定密码。"));
    }

    private static async Task ConfigurarAsync()
    {
        _ajustes = Assistente(_ajustes, primeiraVez: false);
        _ajustes.Salvar(_pasta);
        _ajustes.PrepararPasta(_pasta);
        if (await Task.WhenAll(Servicos.Select(s => Docker.PortaAbertaAsync(s.Porta))) is var noAr && noAr.Any(x => x))
            await SubirAsync();
        else
            Console.WriteLine(T.S("✓ Salvo. Vale a partir do próximo \"subir\".", "✓ Saved. Applies from the next \"up\".", "✓ 已保存。下次启动时生效。"));
    }

    private static async Task<bool> AtualizarAsync()
    {
        if (!GarantirDocker() || !await _docker.EsperarMotorAsync(TimeSpan.FromMinutes(1))) return false;
        Console.WriteLine(T.S("  … baixando versões novas", "  … downloading new versions", "  … 正在下载新版本"));
        var (codigo, saida) = await _docker.AtualizarImagensAsync();
        if (codigo != 0) { Console.WriteLine("✗ " + saida.Split('\n').LastOrDefault()); return false; }
        await _docker.SubirAsync();
        Console.WriteLine(T.S("✓ Programas na versão mais recente.", "✓ Programs are on the latest version.", "✓ 程序已是最新版本。"));
        return true;
    }

    // ---------- Docker ----------

    /// <summary>Instala o Docker Engine pelo script oficial se faltar (com a permissão do usuário).</summary>
    private static bool GarantirDocker()
    {
        if (Docker.DesktopInstalado) return true;
        Console.WriteLine();
        Console.WriteLine(T.S("O Docker não está instalado; ele é necessário para rodar os programas.",
                              "Docker is not installed; it is required to run the programs.",
                              "未安装 Docker；运行这些程序需要它。"));
        if (!SimNao(T.S("Instalar agora pelo script oficial (get.docker.com)? Pede a senha do sudo",
                        "Install it now with the official script (get.docker.com)? Asks for the sudo password",
                        "现在用官方脚本（get.docker.com）安装吗？需要 sudo 密码"), true))
            return false;

        var usuario = Environment.UserName;
        var p = Process.Start(new ProcessStartInfo("sh",
            $"-c \"curl -fsSL https://get.docker.com | sudo sh && sudo usermod -aG docker {usuario}\"")
        { UseShellExecute = false });
        p!.WaitForExit();
        if (p.ExitCode != 0 || !Docker.DesktopInstalado)
        {
            Console.WriteLine(T.S("✗ A instalação do Docker falhou; veja as mensagens acima.",
                                  "✗ Docker installation failed; see the messages above.",
                                  "✗ Docker 安装失败；请查看上面的信息。"));
            return false;
        }
        Console.WriteLine(T.S("✓ Docker instalado. Até você sair e entrar de novo na sessão, os comandos usam sudo.",
                              "✓ Docker installed. Until you log out and back in, commands use sudo.",
                              "✓ Docker 已安装。在你注销并重新登录之前，命令会使用 sudo。"));
        return true;
    }

    // ---------- assistente ----------

    private static Ajustes Assistente(Ajustes atuais, bool primeiraVez)
    {
        Console.WriteLine();
        var idiomas = string.Join("  ", T.Idiomas.Select((i, n) => $"{n + 1}) {i.Nome}"));
        var atual = Array.FindIndex(T.Idiomas, i => i.Codigo == T.Atual) + 1;
        var escolha = Perguntar($"{T.S("Idioma", "Language", "语言")} ({idiomas})", atual.ToString());
        if (int.TryParse(escolha, out var n) && n >= 1 && n <= T.Idiomas.Length && n != atual)
        {
            var novo = T.Idiomas[n - 1].Codigo;
            // Subpastas ainda com o nome padrão do idioma anterior acompanham a troca.
            var (sAntes, fAntes) = Ajustes.SubpastasPadrao(T.Atual);
            var (sNovo, fNovo) = Ajustes.SubpastasPadrao(novo);
            if (atuais.SubpastaSeries == sAntes) atuais.SubpastaSeries = sNovo;
            if (atuais.SubpastaFilmes == fAntes) atuais.SubpastaFilmes = fNovo;
            T.Definir(novo);
        }

        Secao(T.S("Pastas (as mesmas bibliotecas do Jellyfin)", "Folders (the same Jellyfin libraries)", "文件夹（与 Jellyfin 媒体库相同）"));
        string pasta;
        while (true)
        {
            pasta = Perguntar(T.S("Pasta de mídia", "Media folder", "媒体文件夹"), atuais.PastaMidia).TrimEnd('/');
            if (pasta.StartsWith('/')) break;
            Console.WriteLine(T.S("  Use um caminho completo, como /srv/midia.", "  Use a full path, such as /srv/media.", "  请使用完整路径，例如 /srv/media。"));
        }
        string series, filmes, downloads;
        while (true)
        {
            series = Perguntar(T.S("Subpasta de séries", "TV shows subfolder", "剧集子文件夹"), atuais.SubpastaSeries);
            filmes = Perguntar(T.S("Subpasta de filmes", "Movies subfolder", "电影子文件夹"), atuais.SubpastaFilmes);
            downloads = Perguntar(T.S("Subpasta de downloads", "Downloads subfolder", "下载子文件夹"), atuais.SubpastaDownloads);
            var nomes = new[] { series, filmes, downloads };
            if (nomes.All(x => x.Length > 0 && !x.Contains('/')) && nomes.Distinct().Count() == 3) break;
            Console.WriteLine(T.S("  Use nomes simples, sem barras, e diferentes entre si.",
                                  "  Use simple names, without slashes, all different.",
                                  "  请使用不含斜杠且互不相同的简单名称。"));
        }

        Secao(T.S("Downloads (0 = sem limite)", "Downloads (0 = unlimited)", "下载（0 = 不限制）"));
        var simultaneos = Numero(T.S("Downloads ao mesmo tempo", "Simultaneous downloads", "同时下载数"), atuais.DownloadsSimultaneos, 50);
        var seeds = Numero(T.S("Mínimo de seeds", "Minimum seeders", "最少做种数"), atuais.MinimoSeeds, 1000);
        var dias = Numero(T.S("Dias de seed (0 = para sempre)", "Seeding days (0 = forever)", "做种天数（0 = 永久）"), atuais.DiasDeSeed, 365);
        var fuso = Perguntar(T.S("Fuso horário", "Time zone", "时区"), atuais.FusoHorario);

        Secao(T.S("Jellyfin (opcional)", "Jellyfin (optional)", "Jellyfin（可选）"));
        Console.WriteLine(T.S(
            "  Chave de API: Painel do Jellyfin → Avançado → Chaves de API → +. Com ela, o que terminar de baixar aparece na hora.",
            "  API key: Jellyfin Dashboard → Advanced → API Keys → +. With it, finished downloads show up right away.",
            "  API 密钥：Jellyfin 控制台 → 高级 → API 密钥 → +。有了它，下载完成的内容会立即出现。"));
        var url = Perguntar(T.S("Endereço do Jellyfin", "Jellyfin address", "Jellyfin 地址"), atuais.JellyfinUrl).TrimEnd('/');
        var chave = Oculto(T.S("Chave de API (Enter mantém a atual / pula)", "API key (Enter keeps current / skips)", "API 密钥（按 Enter 保留当前值 / 跳过）"), atuais.JellyfinChave);

        return new Ajustes
        {
            Idioma = T.Atual,
            PastaMidia = pasta,
            SubpastaSeries = series,
            SubpastaFilmes = filmes,
            SubpastaDownloads = downloads,
            FusoHorario = fuso,
            DownloadsSimultaneos = simultaneos,
            MinimoSeeds = seeds,
            DiasDeSeed = dias,
            JellyfinUrl = url,
            JellyfinChave = chave,
            JellyfinAplicado = atuais.JellyfinAplicado && atuais.JellyfinUrl == url && atuais.JellyfinChave == chave,
            DicaBabelSubsMostrada = atuais.DicaBabelSubsMostrada,
        };
    }

    // ---------- terminal ----------

    private static void Titulo(string texto)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"== {texto} ==");
        Console.ResetColor();
    }

    private static void Secao(string texto)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(texto);
        Console.ResetColor();
    }

    private static string Perguntar(string rotulo, string padrao)
    {
        Console.Write($"  {rotulo} [{padrao}]: ");
        var r = Console.ReadLine();
        return string.IsNullOrWhiteSpace(r) ? padrao : r.Trim();
    }

    private static int Numero(string rotulo, int padrao, int maximo)
    {
        while (true)
        {
            if (int.TryParse(Perguntar(rotulo, padrao.ToString()), out var v) && v >= 0 && v <= maximo) return v;
            Console.WriteLine(T.S($"  Número de 0 a {maximo}.", $"  A number from 0 to {maximo}.", $"  0 到 {maximo} 之间的数字。"));
        }
    }

    private static bool SimNao(string pergunta, bool padrao)
    {
        var opcoes = T.S(padrao ? "S/n" : "s/N", padrao ? "Y/n" : "y/N", padrao ? "Y/n" : "y/N");
        Console.Write($"{pergunta} [{opcoes}]: ");
        var r = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        return r.Length == 0 ? padrao : r is "s" or "sim" or "y" or "yes" or "是";
    }

    /// <summary>Lê sem mostrar na tela (chave de API). Enter vazio mantém o valor atual.</summary>
    private static string Oculto(string rotulo, string atual)
    {
        Console.Write($"  {rotulo}{(atual.Length > 0 ? " [••••]" : "")}: ");
        if (Console.IsInputRedirected) return (Console.ReadLine() ?? "").Trim() is { Length: > 0 } lido ? lido : atual;
        var texto = new System.Text.StringBuilder();
        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.Backspace) { if (texto.Length > 0) { texto.Length--; Console.Write("\b \b"); } continue; }
            if (!char.IsControl(k.KeyChar)) { texto.Append(k.KeyChar); Console.Write('•'); }
        }
        Console.WriteLine();
        return texto.Length > 0 ? texto.ToString().Trim() : atual;
    }

    private static void Enderecos()
    {
        var ip = IpDaRede();
        Console.WriteLine(T.S("Endereços (deste ou de outro aparelho da rede):", "Addresses (from this or another device on the network):", "地址（本机或局域网中的其他设备）："));
        foreach (var s in Servicos) Console.WriteLine($"  {s.Nome,-12} http://{ip}:{s.Porta}");
    }

    /// <summary>Uma vez só: sugere o plugin BabelSubs para o Jellyfin.</summary>
    private static void DicaBabelSubs()
    {
        if (_ajustes.DicaBabelSubsMostrada) return;
        _ajustes.DicaBabelSubsMostrada = true;
        _ajustes.Salvar(_pasta);
        Console.WriteLine(T.S(
            "\nDica: o plugin BabelSubs baixa ou traduz legendas no seu idioma ao dar play no Jellyfin.\n" +
            "  Jellyfin → Painel → Plugins → Repositórios → +, cole o endereço abaixo, instale pelo Catálogo e reinicie:",
            "\nTip: the BabelSubs plugin downloads or translates subtitles in your language when you press play in Jellyfin.\n" +
            "  Jellyfin → Dashboard → Plugins → Repositories → +, paste the address below, install from the Catalog and restart:",
            "\n提示：BabelSubs 插件会在 Jellyfin 播放时下载或翻译你所用语言的字幕。\n" +
            "  Jellyfin → 控制台 → 插件 → 存储库 → +，粘贴下面的地址，从目录安装后重启："));
        Console.WriteLine("  https://raw.githubusercontent.com/diorgejorge/jellyfin-plugin-babelsubs/main/manifest.json");
    }

    /// <summary>IPv4 da rede local (para abrir as telas de outro aparelho); sem rede, localhost.</summary>
    private static string IpDaRede()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && !n.Name.StartsWith("docker") && !n.Name.StartsWith("br-") && !n.Name.StartsWith("veth"))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "localhost";
        }
        catch { return "localhost"; }
    }
}
