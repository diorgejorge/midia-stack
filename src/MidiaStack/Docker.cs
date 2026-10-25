using System.Diagnostics;

namespace MidiaStack;

/// <summary>
/// Conversa com o Docker e com o docker compose da pasta do app. No Windows é o Docker Desktop (abre e fecha
/// junto com a pilha); no Linux é o serviço do Docker Engine, que fica sempre rodando.
/// </summary>
internal sealed class Docker(string pastaCompose)
{
    private const string DesktopExe = @"C:\Program Files\Docker\Docker\Docker Desktop.exe";
    private const string CliPadrao = @"C:\Program Files\Docker\Docker\resources\bin\docker.exe";

    public string PastaCompose { get; } = pastaCompose;

    /// <summary>
    /// Linux logo depois de instalar o Docker: o usuário só entra no grupo "docker" na próxima sessão, então
    /// até lá os comandos vão por sudo (a senha é pedida no terminal).
    /// </summary>
    private bool _usarSudo;

    public static bool DesktopInstalado => OperatingSystem.IsWindows() ? File.Exists(DesktopExe) : NoPath("docker");

    public bool Instalado => DesktopInstalado;

    private static bool NoPath(string programa) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(d => File.Exists(Path.Combine(d, programa)));

    /// <summary>
    /// Abre um terminal visível com o instalador oficial pelo winget: o usuário acompanha e aprova o pedido
    /// de administrador do Windows. (No Linux a instalação é feita pelo programa de terminal.)
    /// </summary>
    public static void InstalarDesktop()
    {
        var fim = T.S("Pronto. Reinicie o Windows, abra o Docker Desktop uma vez e depois o Midia Stack.",
                      "Done. Restart Windows, open Docker Desktop once, then Media Stack.",
                      "完成。请重启 Windows，打开一次 Docker Desktop，然后再打开 Media Stack。");
        // chcp 65001: o terminal mostra acentos e chinês corretamente.
        Process.Start(new ProcessStartInfo("cmd.exe",
            $"/k chcp 65001 >nul && winget install -e --id Docker.DockerDesktop && echo. && echo {fim}")
        { UseShellExecute = true });
    }

    /// <summary>No Linux não há Desktop: o motor é um serviço (ver AbrirDesktop).</summary>
    public bool DesktopAberto => !OperatingSystem.IsWindows() || Process.GetProcessesByName("Docker Desktop").Length > 0;

    // O instalador põe o docker.exe no PATH, mas um app aberto antes da instalação não enxerga o PATH novo.
    private static string Cli => OperatingSystem.IsWindows() && File.Exists(CliPadrao) ? CliPadrao : "docker";

    /// <summary>Windows: abre o Docker Desktop. Linux: liga o serviço do Docker, se estiver parado.</summary>
    public void AbrirDesktop()
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(DesktopExe) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo("sudo", "systemctl start docker") { UseShellExecute = false })?.WaitForExit();
    }

    /// <summary>Espera o motor do Docker responder (o Desktop leva de 20 s a 1 min para subir).</summary>
    public async Task<bool> EsperarMotorAsync(TimeSpan limite, CancellationToken ct = default)
    {
        var fim = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < fim)
        {
            var (codigo, saida) = await RodarAsync("info --format {{.ServerVersion}}", ct);
            if (codigo == 0) return true;
            // Linux: usuário ainda fora do grupo "docker" — passa a usar sudo e tenta de novo na hora.
            if (!OperatingSystem.IsWindows() && !_usarSudo && saida.Contains("permission denied", StringComparison.OrdinalIgnoreCase))
            {
                _usarSudo = true;
                continue;
            }
            if (!OperatingSystem.IsWindows() && saida.Contains("Cannot connect", StringComparison.OrdinalIgnoreCase))
                AbrirDesktop();
            await Task.Delay(3000, ct);
        }
        return false;
    }

    public Task<(int Codigo, string Saida)> SubirAsync(CancellationToken ct = default) =>
        RodarAsync("compose up -d", ct);

    public Task<(int Codigo, string Saida)> DerrubarAsync(CancellationToken ct = default) =>
        RodarAsync("compose down", ct);

    public Task<(int Codigo, string Saida)> AtualizarImagensAsync(CancellationToken ct = default) =>
        RodarAsync("compose pull", ct);

    public Task<(int Codigo, string Saida)> ComposeAsync(string args, CancellationToken ct = default) =>
        RodarAsync("compose " + args, ct);

    public Task<(int Codigo, string Saida)> ExecAsync(string container, string args, bool registrar = true) =>
        RodarAsync($"exec {container} {args}", registrar: registrar);

    /// <summary>Fecha o Docker Desktop para devolver a memória da VM do WSL (só Windows).</summary>
    public async Task FecharDesktopAsync()
    {
        if (!OperatingSystem.IsWindows()) return;
        // "docker desktop stop" existe nas versões novas do Desktop; nas antigas, encerra o processo.
        var (codigo, _) = await RodarAsync("desktop stop");
        if (codigo == 0) return;
        foreach (var p in Process.GetProcessesByName("Docker Desktop"))
        {
            try { p.CloseMainWindow(); if (!p.WaitForExit(5000)) p.Kill(true); } catch { }
        }
    }

    /// <summary>
    /// Senha temporária do Web UI do qBittorrent desta sessão (ele gera uma nova a cada início enquanto
    /// ninguém define uma fixa). Null se não estiver rodando ou se já houver senha definida.
    /// </summary>
    public async Task<string?> SenhaTemporariaQbitAsync()
    {
        // Só os logs desde o último início: senhas de sessões anteriores não valem mais.
        var (c1, inicio) = await RodarAsync("inspect -f {{.State.StartedAt}} qbittorrent");
        if (c1 != 0) return null;
        // Não registra no log: a saída contém a senha.
        var (c2, logs) = await RodarAsync($"logs --since {inicio.Trim()} qbittorrent", registrar: false);
        if (c2 != 0) return null;
        const string marca = "temporary password is provided for this session: ";
        var linha = logs.Split('\n').LastOrDefault(l => l.Contains(marca));
        return linha?[(linha.IndexOf(marca) + marca.Length)..].Trim();
    }

    private async Task<(int Codigo, string Saida)> RodarAsync(string args, CancellationToken ct = default, bool registrar = true)
    {
        var psi = _usarSudo
            ? new ProcessStartInfo("sudo", "docker " + args)
            : new ProcessStartInfo(Cli, args);
        psi.WorkingDirectory = PastaCompose;
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        if (OperatingSystem.IsWindows())
        {
            // O docker chama auxiliares (docker-credential-desktop) pelo PATH; garante a pasta deles mesmo com PATH antigo.
            var bin = Path.GetDirectoryName(CliPadrao)!;
            var path = psi.Environment["PATH"] ?? "";
            if (!path.Contains(bin, StringComparison.OrdinalIgnoreCase))
                psi.Environment["PATH"] = bin + ";" + path;
        }
        try
        {
            using var p = Process.Start(psi)!;
            var saida = p.StandardOutput.ReadToEndAsync(ct);
            var erro = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            var texto = (await saida + await erro).Trim();
            Log.Escrever($"docker {args} -> {p.ExitCode}{(registrar && texto.Length > 0 ? "\n" + texto : "")}");
            return (p.ExitCode, texto);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Escrever($"docker {args} falhou: {e.Message}");
            return (-1, e.Message);
        }
    }

    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(2),
    };

    /// <summary>
    /// Qualquer resposta HTTP em localhost = aquele programa está no ar. Só abrir a porta TCP não basta:
    /// o Docker aceita a conexão assim que o container sobe, antes de o programa terminar de iniciar.
    /// </summary>
    public static async Task<bool> PortaAbertaAsync(int porta)
    {
        try
        {
            using var r = await Http.GetAsync($"http://127.0.0.1:{porta}/", HttpCompletionOption.ResponseHeadersRead);
            return true;
        }
        catch { return false; }
    }
}

internal static class Log
{
    public static readonly string Arquivo = Path.Combine(AppContext.BaseDirectory, "midia-stack.log");

    public static void Escrever(string msg)
    {
        try { File.AppendAllText(Arquivo, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n"); } catch { }
    }
}
