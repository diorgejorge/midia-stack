namespace MidiaStack;

/// <summary>
/// Assistente da primeira execução (e "Configurações…" no menu): pergunta onde ficam as bibliotecas e as
/// regras de download, confere o Docker Desktop e devolve os ajustes. Numa máquina nova basta responder e subir.
/// Trocar o idioma fecha com DialogResult.Retry e o chamador reabre já traduzido (Abrir faz isso).
/// </summary>
internal sealed class Assistente : Form
{
    private readonly ComboBox _idioma = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly TextBox _pasta = new() { Width = 330 };
    private readonly TextBox _series = new() { Width = 120 };
    private readonly TextBox _filmes = new() { Width = 120 };
    private readonly TextBox _downloads = new() { Width = 120 };
    private readonly TextBox _fuso = new() { Width = 200 };
    private readonly NumericUpDown _dias = new() { Minimum = 0, Maximum = 365, Width = 70 };
    private readonly NumericUpDown _seeds = new() { Minimum = 0, Maximum = 1000, Width = 70 };
    private readonly NumericUpDown _simultaneos = new() { Minimum = 0, Maximum = 50, Width = 70 };
    private readonly CheckBox _subir = new() { AutoSize = true, Checked = true };
    private readonly TextBox _jellyfinUrl = new() { Width = 240 };
    private readonly TextBox _jellyfinChave = new() { Width = 240, UseSystemPasswordChar = true };
    private readonly Label _jellyfinTeste = new() { AutoSize = true, Margin = new Padding(6, 6, 0, 0) };
    private readonly Label _docker = new() { AutoSize = true };
    private readonly Button _instalarDocker = new() { AutoSize = true };

    public Ajustes Resultado { get; private set; }
    public bool SubirAoConcluir => _subir.Checked;

    /// <summary>Mostra o assistente (reabrindo se o idioma mudar). Null = cancelado.</summary>
    public static (Ajustes Ajustes, bool Subir)? Abrir(Ajustes atuais, bool primeiraVez, IWin32Window? dono = null)
    {
        var ajustes = atuais;
        var subir = true;
        while (true)
        {
            T.Definir(ajustes.Idioma);
            using var dlg = new Assistente(ajustes, primeiraVez, subir);
            var r = dono == null ? dlg.ShowDialog() : dlg.ShowDialog(dono);
            ajustes = dlg.Resultado;
            subir = dlg.SubirAoConcluir;
            if (r == DialogResult.Retry) continue;
            if (r != DialogResult.OK)
            {
                T.Definir(atuais.Idioma);
                return null;
            }
            return (ajustes, subir);
        }
    }

    private Assistente(Ajustes atuais, bool primeiraVez, bool subir)
    {
        Resultado = atuais;
        Text = primeiraVez
            ? T.S("Mídia Stack — instalação", "Media Stack — setup", "Media Stack — 安装")
            : T.S("Mídia Stack — configurações", "Media Stack — settings", "Media Stack — 设置");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = new Font(T.Atual == T.Chines ? "Microsoft YaHei UI" : "Segoe UI", 9.5f);

        foreach (var (_, nome) in T.Idiomas) _idioma.Items.Add(nome);
        _idioma.SelectedIndex = Array.FindIndex(T.Idiomas, i => i.Codigo == T.Atual);
        _idioma.SelectedIndexChanged += (_, _) =>
        {
            var novo = T.Idiomas[_idioma.SelectedIndex].Codigo;
            Resultado = LerCampos();
            // Subpastas ainda com o nome padrão do idioma anterior acompanham a troca; editadas ficam como estão.
            var (seriesAntes, filmesAntes) = Ajustes.SubpastasPadrao(T.Atual);
            var (seriesNovo, filmesNovo) = Ajustes.SubpastasPadrao(novo);
            if (Resultado.SubpastaSeries == seriesAntes) Resultado.SubpastaSeries = seriesNovo;
            if (Resultado.SubpastaFilmes == filmesAntes) Resultado.SubpastaFilmes = filmesNovo;
            Resultado.Idioma = novo;
            DialogResult = DialogResult.Retry;
        };

        _pasta.Text = atuais.PastaMidia;
        _series.Text = atuais.SubpastaSeries;
        _filmes.Text = atuais.SubpastaFilmes;
        _downloads.Text = atuais.SubpastaDownloads;
        _fuso.Text = atuais.FusoHorario;
        _dias.Value = Math.Clamp(atuais.DiasDeSeed, 0, 365);
        _seeds.Value = Math.Clamp(atuais.MinimoSeeds, 0, 1000);
        _simultaneos.Value = Math.Clamp(atuais.DownloadsSimultaneos, 0, 50);
        _jellyfinUrl.Text = atuais.JellyfinUrl;
        _jellyfinChave.Text = atuais.JellyfinChave;
        _subir.Text = T.S("Subir tudo ao concluir", "Start everything when done", "完成后全部启动");
        _subir.Checked = subir;
        _subir.Visible = primeiraVez;
        _instalarDocker.Text = T.S("Instalar Docker Desktop", "Install Docker Desktop", "安装 Docker Desktop");

        var grade = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grade.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grade.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void Linha(string rotulo, Control controle)
        {
            grade.Controls.Add(new Label { Text = rotulo, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 8, 12, 0) });
            grade.Controls.Add(controle);
        }
        void Titulo(string texto)
        {
            var l = new Label { Text = texto, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 14, 0, 2) };
            grade.Controls.Add(l);
            grade.SetColumnSpan(l, 2);
        }

        Linha(T.S("Idioma", "Language", "语言"), _idioma);

        if (primeiraVez)
        {
            var intro = new Label
            {
                Text = T.S(
                    "Responda uma vez; depois é só usar o ícone perto do relógio.\n" +
                    "Os programas rodam no Docker e guardam tudo na pasta deste .exe.",
                    "Answer once; after that just use the icon next to the clock.\n" +
                    "The programs run in Docker and keep everything in this .exe's folder.",
                    "只需回答一次，之后使用时钟旁的图标即可。\n" +
                    "程序运行在 Docker 中，所有数据保存在本 .exe 所在的文件夹。"),
                AutoSize = true,
                Margin = new Padding(0, 10, 0, 6),
            };
            grade.Controls.Add(intro);
            grade.SetColumnSpan(intro, 2);
        }

        Titulo(T.S("Pastas (as mesmas bibliotecas do Jellyfin)", "Folders (the same Jellyfin libraries)", "文件夹（与 Jellyfin 媒体库相同）"));
        var procurar = new Button { Text = T.S("Procurar…", "Browse…", "浏览…"), AutoSize = true };
        procurar.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog
            {
                SelectedPath = _pasta.Text,
                Description = T.S("Pasta com as bibliotecas de mídia", "Folder with the media libraries", "存放媒体库的文件夹"),
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) _pasta.Text = dlg.SelectedPath;
        };
        Linha(T.S("Pasta de mídia", "Media folder", "媒体文件夹"), Fluxo(_pasta, procurar));
        Linha(T.S("Subpasta de séries", "TV shows subfolder", "剧集子文件夹"), _series);
        Linha(T.S("Subpasta de filmes", "Movies subfolder", "电影子文件夹"), _filmes);
        Linha(T.S("Subpasta de downloads", "Downloads subfolder", "下载子文件夹"), _downloads);

        Titulo(T.S("Downloads", "Downloads", "下载"));
        Linha(T.S("Downloads ao mesmo tempo", "Simultaneous downloads", "同时下载数"),
            Fluxo(_simultaneos, Dica(T.S("0 = sem limite", "0 = unlimited", "0 = 不限制"))));
        Linha(T.S("Mínimo de seeds", "Minimum seeders", "最少做种数"),
            Fluxo(_seeds, Dica(T.S("torrents com menos são recusados · 0 = sem mínimo",
                                   "torrents with fewer are rejected · 0 = no minimum",
                                   "低于此数的种子会被拒绝 · 0 = 不限制"))));
        Linha(T.S("Dias de seed", "Seeding days", "做种天数"),
            Fluxo(_dias, Dica(T.S("depois disso o torrent sai da lista · 0 = para sempre",
                                  "then the torrent is removed from the list · 0 = forever",
                                  "之后种子会从列表中移除 · 0 = 永久做种"))));
        Linha(T.S("Fuso horário", "Time zone", "时区"), _fuso);

        Titulo(T.S("Jellyfin (opcional)", "Jellyfin (optional)", "Jellyfin（可选）"));
        var testar = new Button { Text = T.S("Testar", "Test", "测试"), AutoSize = true };
        testar.Click += async (_, _) => await TestarJellyfinAsync();
        Linha(T.S("Endereço", "Address", "地址"), _jellyfinUrl);
        Linha(T.S("Chave de API", "API key", "API 密钥"), Fluxo(_jellyfinChave, testar, _jellyfinTeste));
        var dicaJellyfin = Dica(T.S(
            "Painel do Jellyfin → Chaves de API → +. Com ela, o que terminar de baixar aparece no Jellyfin na hora.",
            "Jellyfin Dashboard → API Keys → +. With it, finished downloads show up in Jellyfin right away.",
            "Jellyfin 控制台 → API 密钥 → +。有了它，下载完成的内容会立即出现在 Jellyfin 中。"));
        dicaJellyfin.Margin = new Padding(0, 0, 0, 0);
        grade.Controls.Add(new Label { AutoSize = true });
        grade.Controls.Add(dicaJellyfin);

        Titulo("Docker Desktop");
        _instalarDocker.Click += (_, _) => InstalarDocker();
        Linha(T.S("Situação", "Status", "状态"), Fluxo(_docker, _instalarDocker));
        AtualizarDocker();
        Activated += (_, _) => AtualizarDocker();

        var ok = new Button
        {
            Text = primeiraVez ? T.S("Concluir", "Finish", "完成") : T.S("Salvar", "Save", "保存"),
            AutoSize = true,
            DialogResult = DialogResult.None,
        };
        var cancelar = new Button { Text = T.S("Cancelar", "Cancel", "取消"), AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Concluir();
        var botoes = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
        botoes.Controls.AddRange([cancelar, ok, _subir]);
        grade.Controls.Add(botoes);
        grade.SetColumnSpan(botoes, 2);

        Controls.Add(grade);
        AcceptButton = ok;
        CancelButton = cancelar;
    }

    private static FlowLayoutPanel Fluxo(params Control[] controles)
    {
        var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 0) };
        f.Controls.AddRange(controles);
        return f;
    }

    private static Label Dica(string texto) =>
        new() { Text = texto, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(6, 6, 0, 0) };

    private void AtualizarDocker()
    {
        var instalado = Docker.DesktopInstalado;
        _docker.Text = instalado
            ? T.S("instalado ✓", "installed ✓", "已安装 ✓")
            : T.S("não instalado — necessário para rodar os programas",
                  "not installed — required to run the programs",
                  "未安装 — 运行这些程序需要它");
        _docker.ForeColor = instalado ? Color.SeaGreen : Color.Firebrick;
        _instalarDocker.Visible = !instalado;
    }

    private void InstalarDocker()
    {
        var r = MessageBox.Show(this,
            T.S("Vai abrir uma janela de terminal com o instalador oficial (winget install Docker.DockerDesktop).\n\n" +
                "O Windows pede permissão de administrador e, no fim, provavelmente um reinício. " +
                "Depois de reiniciar, abra o Docker Desktop uma vez para aceitar os termos e volte a abrir o Mídia Stack.",
                "A terminal window will open with the official installer (winget install Docker.DockerDesktop).\n\n" +
                "Windows will ask for administrator permission and, at the end, probably a restart. " +
                "After restarting, open Docker Desktop once to accept its terms, then open Media Stack again.",
                "将打开一个终端窗口运行官方安装程序（winget install Docker.DockerDesktop）。\n\n" +
                "Windows 会请求管理员权限，结束时可能需要重启。" +
                "重启后，请先打开一次 Docker Desktop 接受条款，然后再打开 Media Stack。"),
            T.S("Instalar Docker Desktop", "Install Docker Desktop", "安装 Docker Desktop"),
            MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (r == DialogResult.OK) Docker.InstalarDesktop();
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Confere endereço e chave do Jellyfin a partir deste PC (GET /System/Info com a chave).</summary>
    private async Task TestarJellyfinAsync()
    {
        _jellyfinTeste.ForeColor = SystemColors.GrayText;
        _jellyfinTeste.Text = T.S("testando…", "testing…", "测试中…");
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Get, _jellyfinUrl.Text.Trim().TrimEnd('/') + "/System/Info");
            req.Headers.Add("X-Emby-Token", _jellyfinChave.Text.Trim());
            using var r = await Http.SendAsync(req);
            var ok = r.IsSuccessStatusCode;
            _jellyfinTeste.ForeColor = ok ? Color.SeaGreen : Color.Firebrick;
            _jellyfinTeste.Text = ok
                ? T.S("conectado ✓", "connected ✓", "已连接 ✓")
                : (int)r.StatusCode == 401
                    ? T.S("chave inválida", "invalid key", "密钥无效")
                    : $"HTTP {(int)r.StatusCode}";
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
        {
            _jellyfinTeste.ForeColor = Color.Firebrick;
            _jellyfinTeste.Text = T.S("não respondeu", "no response", "无响应");
        }
    }

    private Ajustes LerCampos() => new()
    {
        Idioma = T.Atual,
        PastaMidia = _pasta.Text.Trim().TrimEnd('\\'),
        SubpastaSeries = _series.Text.Trim(),
        SubpastaFilmes = _filmes.Text.Trim(),
        SubpastaDownloads = _downloads.Text.Trim(),
        FusoHorario = _fuso.Text.Trim(),
        DiasDeSeed = (int)_dias.Value,
        MinimoSeeds = (int)_seeds.Value,
        DownloadsSimultaneos = (int)_simultaneos.Value,
        DicaBabelSubsMostrada = Resultado.DicaBabelSubsMostrada,
        JellyfinUrl = _jellyfinUrl.Text.Trim().TrimEnd('/'),
        JellyfinChave = _jellyfinChave.Text.Trim(),
        // Mudou endereço ou chave: o configurador precisa regravar no Sonarr/Radarr.
        JellyfinAplicado = Resultado.JellyfinAplicado
            && Resultado.JellyfinUrl == _jellyfinUrl.Text.Trim().TrimEnd('/')
            && Resultado.JellyfinChave == _jellyfinChave.Text.Trim(),
    };

    private void Concluir()
    {
        var a = LerCampos();
        var nomes = new[] { a.SubpastaSeries, a.SubpastaFilmes, a.SubpastaDownloads };
        string? erro = null;
        if (a.PastaMidia.Length == 0 || !Path.IsPathFullyQualified(a.PastaMidia))
            erro = T.S("Escolha a pasta de mídia (um caminho completo, como C:\\midia).",
                       "Choose the media folder (a full path, such as C:\\media).",
                       "请选择媒体文件夹（完整路径，例如 C:\\media）。");
        else if (nomes.Any(n => n.Length == 0 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            erro = T.S("As subpastas precisam de um nome simples, sem barras.",
                       "Subfolders need a simple name, without slashes.",
                       "子文件夹需要简单的名称，不能包含斜杠。");
        else if (nomes.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 3)
            erro = T.S("Séries, filmes e downloads precisam ser subpastas diferentes.",
                       "TV shows, movies and downloads must be different subfolders.",
                       "剧集、电影和下载必须是不同的子文件夹。");
        else if (a.JellyfinChave.Length > 0 && a.JellyfinParaContainers == null)
            erro = T.S("Endereço do Jellyfin inválido (ex.: http://localhost:8096).",
                       "Invalid Jellyfin address (e.g. http://localhost:8096).",
                       "Jellyfin 地址无效（例如 http://localhost:8096）。");
        else if (a.FusoHorario.Length == 0)
            erro = T.S("Informe o fuso horário (ex.: America/Sao_Paulo).",
                       "Enter the time zone (e.g. America/New_York).",
                       "请输入时区（例如 Asia/Shanghai）。");
        if (erro != null)
        {
            MessageBox.Show(this, erro, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Resultado = a;
        DialogResult = DialogResult.OK;
    }
}
