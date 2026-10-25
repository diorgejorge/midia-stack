using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace MidiaStack;

/// <summary>Ícone na bandeja: verde = tudo no ar, amarelo = trabalhando/parcial, cinza = desligado.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private record Servico(string Id, int Porta);

    private static readonly Servico[] Servicos =
    [
        new("Seerr", 5055),
        new("Sonarr", 8989),
        new("Radarr", 7878),
        new("Prowlarr", 9696),
        new("qBittorrent", 8080),
    ];

    private static string NomeServico(Servico s) => s.Id == "Seerr"
        ? T.S("Seerr (pedir séries e filmes)", "Seerr (request shows and movies)", "Seerr（请求剧集和电影）")
        : s.Id;

    private readonly Docker _docker;
    private Ajustes _ajustes;
    private readonly NotifyIcon _icone;
    private readonly ContextMenuStrip _menu = new();
    private ToolStripMenuItem _itemStatus = null!, _itemSubir = null!, _itemParar = null!, _itemAtualizar = null!;
    private readonly Dictionary<string, ToolStripMenuItem> _itensAbrir = [];
    private bool _fecharDocker = true;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Icon _verde = Bolinha(Color.FromArgb(46, 160, 67));
    private readonly Icon _amarelo = Bolinha(Color.FromArgb(210, 153, 34));
    private readonly Icon _cinza = Bolinha(Color.FromArgb(130, 130, 130));
    private bool _ocupado;
    private bool _dicaBabelSubsNaTela;

    public TrayApp(string pastaApp, Ajustes ajustes, bool subirAoIniciar)
    {
        _docker = new Docker(pastaApp);
        _ajustes = ajustes;
        MontarMenu();

        _icone = new NotifyIcon
        {
            Icon = _cinza,
            Text = T.App,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        // Clique esquerdo também abre o menu (por padrão só o direito abre).
        _icone.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                typeof(NotifyIcon).GetMethod("ShowContextMenu",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(_icone, null);
        };
        _icone.DoubleClick += (_, _) => AbrirUrl("http://localhost:5055");
        _icone.BalloonTipClicked += (_, _) =>
        {
            if (_dicaBabelSubsNaTela) BabelSubs.Mostrar();
        };
        _icone.BalloonTipClosed += (_, _) => _dicaBabelSubsNaTela = false;

        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += async (_, _) => await AtualizarStatusAsync();
        _timer.Start();
        _ = subirAoIniciar ? SubirAsync() : AtualizarStatusAsync();
    }

    /// <summary>Monta o menu no idioma atual (refeito quando o idioma muda em Configurações).</summary>
    private void MontarMenu()
    {
        _menu.Items.Clear();
        _itensAbrir.Clear();

        _itemStatus = new ToolStripMenuItem(T.S("Verificando…", "Checking…", "检查中…")) { Enabled = false };
        _itemSubir = new ToolStripMenuItem(T.S("▶  Subir tudo", "▶  Start all", "▶  全部启动"), null, async (_, _) => await SubirAsync());
        _itemParar = new ToolStripMenuItem(T.S("■  Parar tudo", "■  Stop all", "■  全部停止"), null, async (_, _) => await PararAsync());
        _menu.Items.AddRange([_itemStatus, new ToolStripSeparator(), _itemSubir, _itemParar, new ToolStripSeparator()]);

        foreach (var s in Servicos)
        {
            var item = new ToolStripMenuItem(T.S($"Abrir {NomeServico(s)}", $"Open {NomeServico(s)}", $"打开 {NomeServico(s)}"),
                null, (_, _) => AbrirUrl($"http://localhost:{s.Porta}"));
            _itensAbrir[s.Id] = item;
            _menu.Items.Add(item);
        }

        _itemAtualizar = new ToolStripMenuItem(
            T.S("Atualizar programas (baixar versões novas)", "Update programs (download new versions)", "更新程序（下载新版本）"),
            null, async (_, _) => await AtualizarAsync());
        var itemFecharDocker = new ToolStripMenuItem(T.S("Fechar o Docker ao parar", "Close Docker when stopping", "停止时关闭 Docker"))
        {
            Checked = _fecharDocker,
            CheckOnClick = true,
        };
        itemFecharDocker.CheckedChanged += (_, _) => _fecharDocker = itemFecharDocker.Checked;

        _menu.Items.AddRange(
        [
            new ToolStripSeparator(),
            new ToolStripMenuItem(T.S("Senha do qBittorrent", "qBittorrent password", "qBittorrent 密码"), null, async (_, _) => await MostrarSenhaQbitAsync()),
            new ToolStripMenuItem(T.S("Legendas automáticas (BabelSubs)…", "Automatic subtitles (BabelSubs)…", "自动字幕（BabelSubs）…"),
                null, (_, _) => BabelSubs.Mostrar()),
            new ToolStripMenuItem(T.S("Abrir pasta de downloads", "Open downloads folder", "打开下载文件夹"), null, (_, _) => AbrirUrl(_ajustes.PastaDownloads)),
            new ToolStripMenuItem(T.S($"Abrir pasta do {T.App}", $"Open {T.App} folder", $"打开 {T.App} 文件夹"), null, (_, _) => AbrirUrl(_docker.PastaCompose)),
            new ToolStripMenuItem(T.S("Ver log", "View log", "查看日志"), null, (_, _) => AbrirLog()),
            new ToolStripMenuItem(T.S("Configurações…", "Settings…", "设置…"), null, async (_, _) => await ConfiguracoesAsync()),
            new ToolStripMenuItem(T.S("Conferir configuração", "Check configuration", "检查配置"), null, async (_, _) => await ConferirAsync()),
            _itemAtualizar,
            itemFecharDocker,
            new ToolStripSeparator(),
            new ToolStripMenuItem(T.S("Sair", "Exit", "退出"), null, (_, _) => Sair()),
        ]);
    }

    /// <summary>Reabre o assistente; se algo estiver no ar, aplica na hora (o compose recria o que mudou).</summary>
    private async Task ConfiguracoesAsync()
    {
        if (_ocupado) return;
        if (Assistente.Abrir(_ajustes, primeiraVez: false) is not { } resposta) return;
        _ajustes = resposta.Ajustes;
        _ajustes.Salvar(_docker.PastaCompose);
        _ajustes.PrepararPasta(_docker.PastaCompose);
        MontarMenu();
        if (await AlgumNoArAsync()) await ConferirAsync();
        else
        {
            await AtualizarStatusAsync();
            Avisar(T.S("Configurações salvas", "Settings saved", "设置已保存"),
                T.S("Valem a partir do próximo \"Subir tudo\".", "They apply from the next \"Start all\".", "将在下次“全部启动”时生效。"));
        }
    }

    private async Task SubirAsync()
    {
        if (_ocupado) return;
        if (!_docker.Instalado)
        {
            var r = MessageBox.Show(
                T.S("O Docker Desktop não está instalado.\n\nEle é necessário para rodar os programas. " +
                    "Instalar agora pelo instalador oficial (winget)? Vai abrir um terminal e o Windows pede permissão de administrador; " +
                    "no fim, reinicie, abra o Docker Desktop uma vez e volte ao Mídia Stack.",
                    "Docker Desktop is not installed.\n\nIt is required to run the programs. " +
                    "Install it now with the official installer (winget)? A terminal will open and Windows will ask for administrator permission; " +
                    "at the end, restart, open Docker Desktop once and come back to Media Stack.",
                    "未安装 Docker Desktop。\n\n运行这些程序需要它。" +
                    "现在用官方安装程序（winget）安装吗？将打开一个终端，Windows 会请求管理员权限；" +
                    "完成后请重启，打开一次 Docker Desktop，再回到 Media Stack。"),
                T.App, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes) Docker.InstalarDesktop();
            return;
        }

        await Trabalhar(T.S("Subindo…", "Starting…", "启动中…"), async () =>
        {
            // Sobe a pilha e liga Prowlarr → Sonarr/Radarr → qBittorrent (só mexe no que falta).
            var configurador = new Configurador(_docker, _ajustes, msg =>
            {
                _itemStatus.Text = msg;
                _icone.Text = Dica(msg);
            });
            var (ok, mensagem) = await configurador.SubirEConfigurarAsync();
            // O qBittorrent gera uma senha nova a cada início (enquanto não houver senha fixa): já mostra e copia.
            if (ok && await _docker.SenhaTemporariaQbitAsync() is { } senha)
            {
                CopiarTexto(senha);
                mensagem += T.S($"\nqBittorrent: usuário admin, senha {senha} (copiada)",
                                $"\nqBittorrent: user admin, password {senha} (copied)",
                                $"\nqBittorrent：用户 admin，密码 {senha}（已复制）");
            }
            Avisar(ok ? T.S("Tudo no ar", "All running", "全部运行中") : T.S("Problema ao subir", "Problem starting", "启动出错"),
                mensagem, ok ? ToolTipIcon.Info : ToolTipIcon.Error);
            if (ok) _ = DicaBabelSubsAsync();
        });
    }

    /// <summary>Uma vez só, depois da primeira subida: sugere o BabelSubs se ele não estiver no Jellyfin.</summary>
    private async Task DicaBabelSubsAsync()
    {
        if (_ajustes.DicaBabelSubsMostrada) return;
        _ajustes.DicaBabelSubsMostrada = true;
        _ajustes.Salvar(_docker.PastaCompose);
        if (BabelSubs.Instalado) return;

        await Task.Delay(TimeSpan.FromSeconds(8)); // depois do aviso de "Tudo no ar"
        _dicaBabelSubsNaTela = true;
        _icone.ShowBalloonTip(15000,
            T.S("Dica: legendas automáticas", "Tip: automatic subtitles", "提示：自动字幕"),
            T.S("O plugin BabelSubs baixa ou traduz legendas no seu idioma ao dar play no Jellyfin. Clique para ver como instalar.",
                "The BabelSubs plugin downloads or translates subtitles in your language when you press play in Jellyfin. Click to see how to install it.",
                "BabelSubs 插件会在 Jellyfin 播放时下载或翻译你所用语言的字幕。点击查看安装方法。"),
            ToolTipIcon.Info);
    }

    private async Task MostrarSenhaQbitAsync()
    {
        var titulo = $"{T.App} — qBittorrent";
        var senha = _docker.Instalado ? await _docker.SenhaTemporariaQbitAsync() : null;
        if (senha == null)
        {
            MessageBox.Show(
                T.S("Nenhuma senha temporária nesta sessão.\n\nOu o qBittorrent está desligado (use \"Subir tudo\"), " +
                    "ou você já definiu uma senha fixa em Ferramentas → Opções → Web UI.",
                    "No temporary password in this session.\n\nEither qBittorrent is off (use \"Start all\"), " +
                    "or you already set a permanent password in Tools → Options → Web UI.",
                    "本次会话没有临时密码。\n\nqBittorrent 可能未运行（请使用“全部启动”），" +
                    "或者你已在 工具 → 选项 → Web UI 中设置了固定密码。"),
                titulo, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        CopiarTexto(senha);
        MessageBox.Show(
            T.S($"Endereço: http://localhost:8080\nUsuário: admin\nSenha: {senha}\n\n" +
                "A senha já foi copiada (Ctrl+V). Ela muda a cada vez que o qBittorrent sobe; para uma senha fixa, " +
                "entre e defina em Ferramentas → Opções → Web UI.",
                $"Address: http://localhost:8080\nUser: admin\nPassword: {senha}\n\n" +
                "The password was copied (Ctrl+V). It changes every time qBittorrent starts; for a permanent one, " +
                "log in and set it in Tools → Options → Web UI.",
                $"地址：http://localhost:8080\n用户：admin\n密码：{senha}\n\n" +
                "密码已复制（Ctrl+V）。每次 qBittorrent 启动时都会变化；如需固定密码，" +
                "请登录后在 工具 → 选项 → Web UI 中设置。"),
            titulo, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void CopiarTexto(string texto)
    {
        try { Clipboard.SetText(texto); } catch (Exception e) { Log.Escrever($"copiar: {e.Message}"); }
    }

    // Mesmo fluxo do Subir, útil quando já está tudo no ar (o Subir fica desabilitado).
    private async Task ConferirAsync()
    {
        if (_ocupado) return;
        if (!_docker.Instalado) { await SubirAsync(); return; }
        await Trabalhar(T.S("Conferindo…", "Checking…", "检查中…"), async () =>
        {
            var (ok, mensagem) = await new Configurador(_docker, _ajustes, msg => _itemStatus.Text = msg).SubirEConfigurarAsync();
            Avisar(ok ? T.S("Configuração ok", "Configuration ok", "配置正常") : T.S("Problema na configuração", "Configuration problem", "配置出错"),
                mensagem, ok ? ToolTipIcon.Info : ToolTipIcon.Error);
        });
    }

    private async Task PararAsync()
    {
        if (_ocupado) return;
        await Trabalhar(T.S("Parando…", "Stopping…", "停止中…"), async () =>
        {
            if (await _docker.EsperarMotorAsync(TimeSpan.FromSeconds(5)))
            {
                var (codigo, saida) = await _docker.DerrubarAsync();
                if (codigo != 0)
                {
                    Avisar(T.S("Falha ao parar", "Failed to stop", "停止失败"), UltimaLinha(saida), ToolTipIcon.Error);
                    return;
                }
            }
            if (_fecharDocker && _docker.DesktopAberto)
                await _docker.FecharDesktopAsync();
            Avisar(T.S("Tudo parado", "All stopped", "已全部停止"), _fecharDocker
                ? T.S("Programas e Docker desligados.", "Programs and Docker are off.", "程序和 Docker 已关闭。")
                : T.S("Programas desligados.", "Programs are off.", "程序已关闭。"));
        });
    }

    private async Task AtualizarAsync()
    {
        if (_ocupado) return;
        if (!_docker.Instalado || !await _docker.EsperarMotorAsync(TimeSpan.FromSeconds(5)))
        {
            Avisar(T.S("Docker desligado", "Docker is off", "Docker 未运行"),
                T.S("Use \"Subir tudo\" primeiro.", "Use \"Start all\" first.", "请先使用“全部启动”。"), ToolTipIcon.Warning);
            return;
        }
        await Trabalhar(T.S("Atualizando…", "Updating…", "更新中…"), async () =>
        {
            var (codigo, saida) = await _docker.AtualizarImagensAsync();
            if (codigo != 0) { Avisar(T.S("Falha ao atualizar", "Update failed", "更新失败"), UltimaLinha(saida), ToolTipIcon.Error); return; }
            // Recria só os containers cuja imagem mudou, se a pilha estiver no ar.
            if (await AlgumNoArAsync()) await _docker.SubirAsync();
            Avisar(T.S("Atualizado", "Updated", "已更新"),
                T.S("Programas na versão mais recente.", "Programs are on the latest version.", "程序已是最新版本。"));
        });
    }

    private async Task Trabalhar(string rotulo, Func<Task> acao)
    {
        _ocupado = true;
        _itemSubir.Enabled = _itemParar.Enabled = _itemAtualizar.Enabled = false;
        _itemStatus.Text = rotulo;
        _icone.Icon = _amarelo;
        _icone.Text = Dica(rotulo);
        try { await acao(); }
        catch (Exception e)
        {
            Log.Escrever($"erro: {e}");
            Avisar(T.S("Erro", "Error", "错误"), e.Message, ToolTipIcon.Error);
        }
        finally
        {
            _ocupado = false;
            await AtualizarStatusAsync();
        }
    }

    private async Task AtualizarStatusAsync()
    {
        if (_ocupado) return;
        var noAr = await Task.WhenAll(Servicos.Select(s => Docker.PortaAbertaAsync(s.Porta)));
        if (_ocupado) return;

        var qtd = noAr.Count(x => x);
        for (var i = 0; i < Servicos.Length; i++)
            _itensAbrir[Servicos[i].Id].Enabled = noAr[i];

        var ligados = string.Join(", ", Servicos.Where((_, i) => noAr[i]).Select(s => s.Id));
        (_icone.Icon, _itemStatus.Text) = qtd switch
        {
            0 => (_cinza, T.S("Desligado", "Off", "已关闭")),
            _ when qtd == Servicos.Length => (_verde, T.S("Tudo no ar", "All running", "全部运行中")),
            _ => (_amarelo, T.S($"Parcial: {ligados}", $"Partial: {ligados}", $"部分运行：{ligados}")),
        };
        _icone.Text = Dica(_itemStatus.Text);
        _itemSubir.Enabled = qtd < Servicos.Length;
        _itemParar.Enabled = qtd > 0 || _docker.DesktopAberto;
        _itemAtualizar.Enabled = true;
    }

    /// <summary>Texto do ícone ao passar o mouse; o Windows limita a 127 caracteres.</summary>
    private static string Dica(string estado)
    {
        var texto = $"{T.App} — {estado}";
        return texto.Length > 127 ? texto[..127] : texto;
    }

    private static async Task<bool> AlgumNoArAsync() =>
        (await Task.WhenAll(Servicos.Select(s => Docker.PortaAbertaAsync(s.Porta)))).Any(x => x);

    private void Avisar(string titulo, string texto, ToolTipIcon tipo = ToolTipIcon.Info) =>
        _icone.ShowBalloonTip(4000, titulo, string.IsNullOrWhiteSpace(texto) ? " " : texto, tipo);

    private static string UltimaLinha(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() is { Length: > 0 } l
            ? (l.Length > 200 ? l[..200] : l)
            : T.S("Veja o log para detalhes.", "See the log for details.", "详情请查看日志。");

    private static void AbrirUrl(string alvo)
    {
        try { Process.Start(new ProcessStartInfo(alvo) { UseShellExecute = true }); }
        catch (Exception e) { Log.Escrever($"abrir {alvo}: {e.Message}"); }
    }

    private static void AbrirLog()
    {
        if (!File.Exists(Log.Arquivo)) File.WriteAllText(Log.Arquivo, "");
        AbrirUrl(Log.Arquivo);
    }

    private static Icon Bolinha(Color cor)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pincel = new SolidBrush(cor);
            g.FillEllipse(pincel, 3, 3, 26, 26);
            using var borda = new Pen(Color.FromArgb(200, 255, 255, 255), 2);
            g.DrawEllipse(borda, 3, 3, 26, 26);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void Sair()
    {
        _timer.Stop();
        _icone.Visible = false;
        _icone.Dispose();
        ExitThread();
    }
}
