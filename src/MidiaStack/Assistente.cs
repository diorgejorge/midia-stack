namespace MidiaStack;

/// <summary>
/// Assistente da primeira execução (e "Configurações…" no menu): pergunta onde ficam as bibliotecas e as
/// regras de download, confere o Docker Desktop e devolve os ajustes. Numa máquina nova basta responder e subir.
/// </summary>
internal sealed class Assistente : Form
{
    private readonly TextBox _pasta = new() { Width = 330 };
    private readonly TextBox _series = new() { Width = 120 };
    private readonly TextBox _filmes = new() { Width = 120 };
    private readonly TextBox _downloads = new() { Width = 120 };
    private readonly TextBox _fuso = new() { Width = 200 };
    private readonly NumericUpDown _dias = new() { Minimum = 0, Maximum = 365, Width = 70 };
    private readonly NumericUpDown _seeds = new() { Minimum = 0, Maximum = 1000, Width = 70 };
    private readonly NumericUpDown _simultaneos = new() { Minimum = 1, Maximum = 50, Width = 70 };
    private readonly CheckBox _subir = new() { Text = "Subir tudo ao concluir", AutoSize = true, Checked = true };
    private readonly Label _docker = new() { AutoSize = true };
    private readonly Button _instalarDocker = new() { Text = "Instalar Docker Desktop", AutoSize = true };

    public Ajustes Resultado { get; private set; }
    public bool SubirAoConcluir => _subir.Checked;

    public Assistente(Ajustes atuais, bool primeiraVez)
    {
        Resultado = atuais;
        Text = primeiraVez ? "Mídia Stack — instalação" : "Mídia Stack — configurações";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = new Font("Segoe UI", 9.5f);

        _pasta.Text = atuais.PastaMidia;
        _series.Text = atuais.SubpastaSeries;
        _filmes.Text = atuais.SubpastaFilmes;
        _downloads.Text = atuais.SubpastaDownloads;
        _fuso.Text = atuais.FusoHorario;
        _dias.Value = atuais.DiasDeSeed;
        _seeds.Value = atuais.MinimoSeeds;
        _simultaneos.Value = atuais.DownloadsSimultaneos;
        _subir.Visible = primeiraVez;

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

        if (primeiraVez)
        {
            var intro = new Label
            {
                Text = "Responda uma vez; depois é só usar o ícone perto do relógio.\n" +
                       "Os programas rodam no Docker e guardam tudo na pasta deste .exe.",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };
            grade.Controls.Add(intro);
            grade.SetColumnSpan(intro, 2);
        }

        Titulo("Pastas (as mesmas bibliotecas do Jellyfin)");
        var procurar = new Button { Text = "Procurar…", AutoSize = true };
        procurar.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = _pasta.Text, Description = "Pasta com as bibliotecas de mídia" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _pasta.Text = dlg.SelectedPath;
        };
        Linha("Pasta de mídia", Fluxo(_pasta, procurar));
        Linha("Subpasta de séries", _series);
        Linha("Subpasta de filmes", _filmes);
        Linha("Subpasta de downloads", _downloads);

        Titulo("Downloads");
        Linha("Downloads ao mesmo tempo", _simultaneos);
        Linha("Mínimo de seeds", Fluxo(_seeds, Dica("torrents com menos são recusados")));
        Linha("Dias de seed", Fluxo(_dias, Dica("depois disso o torrent sai da lista")));
        Linha("Fuso horário", _fuso);

        Titulo("Docker Desktop");
        _instalarDocker.Click += (_, _) => InstalarDocker();
        Linha("Situação", Fluxo(_docker, _instalarDocker));
        AtualizarDocker();
        Activated += (_, _) => AtualizarDocker();

        var ok = new Button { Text = primeiraVez ? "Concluir" : "Salvar", AutoSize = true, DialogResult = DialogResult.None };
        var cancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
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
        _docker.Text = instalado ? "instalado ✓" : "não instalado — necessário para rodar os programas";
        _docker.ForeColor = instalado ? Color.SeaGreen : Color.Firebrick;
        _instalarDocker.Visible = !instalado;
    }

    private void InstalarDocker()
    {
        var r = MessageBox.Show(this,
            "Vai abrir uma janela de terminal com o instalador oficial (winget install Docker.DockerDesktop).\n\n" +
            "O Windows pede permissão de administrador e, no fim, provavelmente um reinício. " +
            "Depois de reiniciar, abra o Docker Desktop uma vez para aceitar os termos e volte a abrir o Mídia Stack.",
            "Instalar Docker Desktop", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
        if (r == DialogResult.OK) Docker.InstalarDesktop();
    }

    private void Concluir()
    {
        var nomes = new[] { _series.Text, _filmes.Text, _downloads.Text }.Select(n => n.Trim()).ToArray();
        string? erro = null;
        if (string.IsNullOrWhiteSpace(_pasta.Text) || !Path.IsPathFullyQualified(_pasta.Text.Trim()))
            erro = "Escolha a pasta de mídia (um caminho completo, como C:\\midia).";
        else if (nomes.Any(n => n.Length == 0 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            erro = "As subpastas precisam de um nome simples, sem barras.";
        else if (nomes.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 3)
            erro = "Séries, filmes e downloads precisam ser subpastas diferentes.";
        else if (string.IsNullOrWhiteSpace(_fuso.Text))
            erro = "Informe o fuso horário (ex.: America/Sao_Paulo).";
        if (erro != null)
        {
            MessageBox.Show(this, erro, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Resultado = new Ajustes
        {
            PastaMidia = _pasta.Text.Trim().TrimEnd('\\'),
            SubpastaSeries = nomes[0],
            SubpastaFilmes = nomes[1],
            SubpastaDownloads = nomes[2],
            FusoHorario = _fuso.Text.Trim(),
            DiasDeSeed = (int)_dias.Value,
            MinimoSeeds = (int)_seeds.Value,
            DownloadsSimultaneos = (int)_simultaneos.Value,
        };
        DialogResult = DialogResult.OK;
    }
}
