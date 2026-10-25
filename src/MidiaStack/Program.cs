namespace MidiaStack;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var pastaApp = AcharPastaApp();
        var salvos = Ajustes.Carregar(pastaApp);
        // "--idioma en|pt|zh" força o idioma (útil na primeira execução, antes de haver escolha salva).
        var forcado = args.SkipWhile(a => a != "--idioma").Skip(1).FirstOrDefault();
        T.Definir(forcado ?? salvos?.Idioma);

        // "MidiaStack.exe --subir": sobe e configura sem interface (o resultado vai para o midia-stack.log).
        if (args.Contains("--subir"))
        {
            if (salvos == null)
            {
                Log.Escrever("FALHA: sem midia-stack.json; abra o MidiaStack.exe uma vez para responder o assistente.");
                return 1;
            }
            var docker = new Docker(pastaApp);
            var (ok, mensagem) = new Configurador(docker, salvos, msg => Log.Escrever("progresso: " + msg))
                .SubirEConfigurarAsync().GetAwaiter().GetResult();
            Log.Escrever((ok ? "OK: " : "FALHA: ") + mensagem);
            return ok ? 0 : 1;
        }

        // Uma instância só: um segundo clique no atalho não cria outro ícone na bandeja.
        using var mutex = new Mutex(true, "MidiaStack-instancia-unica", out var primeira);
        if (!primeira)
        {
            MessageBox.Show(
                T.S("O Mídia Stack já está aberto — procure o ícone perto do relógio.",
                    "Media Stack is already running — look for the icon next to the clock.",
                    "Media Stack 已在运行 — 请查看时钟旁的图标。"),
                T.App, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();

        // Primeira execução nesta pasta: assistente de instalação.
        var ajustes = salvos;
        var subirAgora = false;
        if (ajustes == null)
        {
            if (Assistente.Abrir(Ajustes.Novos(T.Atual), primeiraVez: true) is not { } resposta) return 0;
            ajustes = resposta.Ajustes;
            ajustes.Salvar(pastaApp);
            ajustes.PrepararPasta(pastaApp);
            subirAgora = resposta.Subir;
        }

        Application.Run(new TrayApp(pastaApp, ajustes, subirAgora));
        return 0;
    }

    /// <summary>
    /// Pasta de trabalho: onde está o midia-stack.json (subindo a partir do .exe, para rodar também de bin\
    /// durante o desenvolvimento); se não houver, a pasta do .exe.
    /// </summary>
    private static string AcharPastaApp()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Ajustes.NomeArquivo)))
                return dir.FullName;
        return AppContext.BaseDirectory.TrimEnd('\\');
    }
}
