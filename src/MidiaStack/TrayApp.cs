using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace MidiaStack;

/// <summary>Ícone na bandeja: verde = tudo no ar, amarelo = trabalhando/parcial, cinza = desligado.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private record Servico(string Nome, int Porta);

    private static readonly Servico[] Servicos =
    [
        new("Seerr (pedir séries e filmes)", 5055),
        new("Sonarr", 8989),
        new("Radarr", 7878),
        new("Prowlarr", 9696),
        new("qBittorrent", 8080),
    ];

    private readonly Docker _docker;
    private Ajustes _ajustes;
    private readonly NotifyIcon _icone;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _itemStatus, _itemSubir, _itemParar, _itemAtualizar, _itemFecharDocker;
    private readonly Dictionary<string, ToolStripMenuItem> _itensAbrir = [];
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Icon _verde = Bolinha(Color.FromArgb(46, 160, 67));
    private readonly Icon _amarelo = Bolinha(Color.FromArgb(210, 153, 34));
    private readonly Icon _cinza = Bolinha(Color.FromArgb(130, 130, 130));
    private bool _ocupado;

    public TrayApp(string pastaApp, Ajustes ajustes, bool subirAoIniciar)
    {
        _docker = new Docker(pastaApp);
        _ajustes = ajustes;

        _menu = new ContextMenuStrip();
        _itemStatus = new ToolStripMenuItem("Verificando…") { Enabled = false };
        _itemSubir = new ToolStripMenuItem("▶  Subir tudo", null, async (_, _) => await SubirAsync());
        _itemParar = new ToolStripMenuItem("■  Parar tudo", null, async (_, _) => await PararAsync());
        _menu.Items.AddRange([_itemStatus, new ToolStripSeparator(), _itemSubir, _itemParar, new ToolStripSeparator()]);

        foreach (var s in Servicos)
        {
            var item = new ToolStripMenuItem($"Abrir {s.Nome}", null, (_, _) => AbrirUrl($"http://localhost:{s.Porta}"));
            _itensAbrir[s.Nome] = item;
            _menu.Items.Add(item);
        }

        _itemAtualizar = new ToolStripMenuItem("Atualizar programas (baixar versões novas)", null, async (_, _) => await AtualizarAsync());
        _itemFecharDocker = new ToolStripMenuItem("Fechar o Docker ao parar") { Checked = true, CheckOnClick = true };
        _menu.Items.AddRange(
        [
            new ToolStripSeparator(),
            new ToolStripMenuItem("Senha do qBittorrent", null, async (_, _) => await MostrarSenhaQbitAsync()),
            new ToolStripMenuItem("Abrir pasta de downloads", null, (_, _) => AbrirUrl(_ajustes.PastaDownloads)),
            new ToolStripMenuItem("Abrir pasta do Mídia Stack", null, (_, _) => AbrirUrl(_docker.PastaCompose)),
            new ToolStripMenuItem("Ver log", null, (_, _) => AbrirLog()),
            new ToolStripMenuItem("Configurações…", null, async (_, _) => await ConfiguracoesAsync()),
            new ToolStripMenuItem("Conferir configuração", null, async (_, _) => await ConferirAsync()),
            _itemAtualizar,
            _itemFecharDocker,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Sair", null, (_, _) => Sair()),
        ]);

        _icone = new NotifyIcon
        {
            Icon = _cinza,
            Text = "Mídia Stack",
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

        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += async (_, _) => await AtualizarStatusAsync();
        _timer.Start();
        _ = subirAoIniciar ? SubirAsync() : AtualizarStatusAsync();
    }

    /// <summary>Reabre o assistente; se algo estiver no ar, aplica na hora (o compose recria o que mudou).</summary>
    private async Task ConfiguracoesAsync()
    {
        if (_ocupado) return;
        using var assistente = new Assistente(_ajustes, primeiraVez: false);
        if (assistente.ShowDialog() != DialogResult.OK) return;
        _ajustes = assistente.Resultado;
        _ajustes.Salvar(_docker.PastaCompose);
        _ajustes.PrepararPasta(_docker.PastaCompose);
        if (await AlgumNoArAsync()) await ConferirAsync();
        else Avisar("Configurações salvas", "Valem a partir do próximo \"Subir tudo\".");
    }

    private async Task SubirAsync()
    {
        if (_ocupado) return;
        if (!_docker.Instalado)
        {
            var r = MessageBox.Show(
                "O Docker Desktop não está instalado.\n\nEle é necessário para rodar os programas. " +
                "Instalar agora pelo instalador oficial (winget)? Vai abrir um terminal e o Windows pede permissão de administrador; " +
                "no fim, reinicie, abra o Docker Desktop uma vez e volte ao Mídia Stack.",
                "Mídia Stack", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes) Docker.InstalarDesktop();
            return;
        }

        await Trabalhar("Subindo…", async () =>
        {
            // Sobe a pilha e liga Prowlarr → Sonarr → qBittorrent (só mexe no que falta).
            var configurador = new Configurador(_docker, _ajustes, msg =>
            {
                _itemStatus.Text = msg;
                _icone.Text = $"Mídia Stack — {msg}";
            });
            var (ok, mensagem) = await configurador.SubirEConfigurarAsync();
            // O qBittorrent gera uma senha nova a cada início (enquanto não houver senha fixa): já mostra e copia.
            if (ok && await _docker.SenhaTemporariaQbitAsync() is { } senha)
            {
                CopiarTexto(senha);
                mensagem += $"\nqBittorrent: usuário admin, senha {senha} (copiada)";
            }
            Avisar(ok ? "Tudo no ar" : "Problema ao subir", mensagem, ok ? ToolTipIcon.Info : ToolTipIcon.Error);
        });
    }

    private async Task MostrarSenhaQbitAsync()
    {
        var senha = _docker.Instalado ? await _docker.SenhaTemporariaQbitAsync() : null;
        if (senha == null)
        {
            MessageBox.Show(
                "Nenhuma senha temporária nesta sessão.\n\nOu o qBittorrent está desligado (use \"Subir tudo\"), " +
                "ou você já definiu uma senha fixa em Ferramentas → Opções → Web UI.",
                "Mídia Stack — qBittorrent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        CopiarTexto(senha);
        MessageBox.Show(
            $"Endereço: http://localhost:8080\nUsuário: admin\nSenha: {senha}\n\n" +
            "A senha já foi copiada (Ctrl+V). Ela muda a cada vez que o qBittorrent sobe; para uma senha fixa, " +
            "entre e defina em Ferramentas → Opções → Web UI.",
            "Mídia Stack — qBittorrent", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        await Trabalhar("Conferindo…", async () =>
        {
            var (ok, mensagem) = await new Configurador(_docker, _ajustes, msg => _itemStatus.Text = msg).SubirEConfigurarAsync();
            Avisar(ok ? "Configuração ok" : "Problema na configuração", mensagem, ok ? ToolTipIcon.Info : ToolTipIcon.Error);
        });
    }

    private async Task PararAsync()
    {
        if (_ocupado) return;
        await Trabalhar("Parando…", async () =>
        {
            if (await _docker.EsperarMotorAsync(TimeSpan.FromSeconds(5)))
            {
                var (codigo, saida) = await _docker.DerrubarAsync();
                if (codigo != 0)
                {
                    Avisar("Falha ao parar", UltimaLinha(saida), ToolTipIcon.Error);
                    return;
                }
            }
            if (_itemFecharDocker.Checked && _docker.DesktopAberto)
                await _docker.FecharDesktopAsync();
            Avisar("Tudo parado", _itemFecharDocker.Checked ? "Programas e Docker desligados." : "Programas desligados.");
        });
    }

    private async Task AtualizarAsync()
    {
        if (_ocupado) return;
        if (!_docker.Instalado || !await _docker.EsperarMotorAsync(TimeSpan.FromSeconds(5)))
        {
            Avisar("Docker desligado", "Use \"Subir tudo\" primeiro.", ToolTipIcon.Warning);
            return;
        }
        await Trabalhar("Atualizando…", async () =>
        {
            var (codigo, saida) = await _docker.AtualizarImagensAsync();
            if (codigo != 0) { Avisar("Falha ao atualizar", UltimaLinha(saida), ToolTipIcon.Error); return; }
            // Recria só os containers cuja imagem mudou, se a pilha estiver no ar.
            if (await AlgumNoArAsync()) await _docker.SubirAsync();
            Avisar("Atualizado", "Programas na versão mais recente.");
        });
    }

    private async Task Trabalhar(string rotulo, Func<Task> acao)
    {
        _ocupado = true;
        _itemSubir.Enabled = _itemParar.Enabled = _itemAtualizar.Enabled = false;
        _itemStatus.Text = rotulo;
        _icone.Icon = _amarelo;
        _icone.Text = $"Mídia Stack — {rotulo}";
        try { await acao(); }
        catch (Exception e)
        {
            Log.Escrever($"erro: {e}");
            Avisar("Erro", e.Message, ToolTipIcon.Error);
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
            _itensAbrir[Servicos[i].Nome].Enabled = noAr[i];

        (_icone.Icon, _itemStatus.Text) = qtd switch
        {
            0 => (_cinza, "Desligado"),
            _ when qtd == Servicos.Length => (_verde, "Tudo no ar"),
            _ => (_amarelo, $"Parcial: {string.Join(", ", Servicos.Where((_, i) => noAr[i]).Select(s => s.Nome))}"),
        };
        _icone.Text = $"Mídia Stack — {_itemStatus.Text}";
        _itemSubir.Enabled = qtd < Servicos.Length;
        _itemParar.Enabled = qtd > 0 || _docker.DesktopAberto;
        _itemAtualizar.Enabled = true;
    }

    private static async Task<bool> AlgumNoArAsync() =>
        (await Task.WhenAll(Servicos.Select(s => Docker.PortaAbertaAsync(s.Porta)))).Any(x => x);

    private void Avisar(string titulo, string texto, ToolTipIcon tipo = ToolTipIcon.Info) =>
        _icone.ShowBalloonTip(4000, titulo, string.IsNullOrWhiteSpace(texto) ? " " : texto, tipo);

    private static string UltimaLinha(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() is { Length: > 0 } l
            ? (l.Length > 200 ? l[..200] : l)
            : "Veja o log para detalhes.";

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
