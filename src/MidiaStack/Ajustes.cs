using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MidiaStack;

/// <summary>
/// Respostas do assistente da primeira execução, salvas em midia-stack.json ao lado do .exe.
/// A partir delas o app gera o docker-compose.yml (embutido no .exe) e o .env com os caminhos desta máquina.
/// </summary>
internal sealed class Ajustes
{
    public const string NomeArquivo = "midia-stack.json";

    /// <summary>Pasta com as bibliotecas do Jellyfin; vira /data dentro dos containers.</summary>
    public string PastaMidia { get; set; } = @"C:\home\jellyfin";
    public string SubpastaSeries { get; set; } = "series";
    public string SubpastaFilmes { get; set; } = "filmes";
    public string SubpastaDownloads { get; set; } = "downloads";
    public string FusoHorario { get; set; } = "America/Sao_Paulo";
    public int DiasDeSeed { get; set; } = 7;
    public int MinimoSeeds { get; set; } = 10;
    public int DownloadsSimultaneos { get; set; } = 8;

    public string PastaSeries => Path.Combine(PastaMidia, SubpastaSeries);
    public string PastaFilmes => Path.Combine(PastaMidia, SubpastaFilmes);
    public string PastaDownloads => Path.Combine(PastaMidia, SubpastaDownloads);

    // Os mesmos caminhos vistos de dentro dos containers.
    public string DataSeries => "/data/" + SubpastaSeries;
    public string DataFilmes => "/data/" + SubpastaFilmes;
    public string DataDownloads => "/data/" + SubpastaDownloads;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Arquivo(string pastaApp) => Path.Combine(pastaApp, NomeArquivo);

    public static Ajustes? Carregar(string pastaApp)
    {
        var arquivo = Arquivo(pastaApp);
        if (!File.Exists(arquivo)) return null;
        try { return JsonSerializer.Deserialize<Ajustes>(File.ReadAllText(arquivo)); }
        catch (Exception e) { Log.Escrever($"ajustes inválidos em {arquivo}: {e.Message}"); return null; }
    }

    public void Salvar(string pastaApp) =>
        File.WriteAllText(Arquivo(pastaApp), JsonSerializer.Serialize(this, Json), new UTF8Encoding(false));

    /// <summary>Cria as pastas de mídia e grava docker-compose.yml + .env na pasta do app.</summary>
    public void PrepararPasta(string pastaApp)
    {
        foreach (var p in new[] { PastaSeries, PastaFilmes, PastaDownloads })
            Directory.CreateDirectory(p);

        using (var recurso = Assembly.GetExecutingAssembly().GetManifestResourceStream("docker-compose.yml")!)
        using (var leitor = new StreamReader(recurso))
        {
            var compose = leitor.ReadToEnd();
            var destino = Path.Combine(pastaApp, "docker-compose.yml");
            if (!File.Exists(destino) || File.ReadAllText(destino) != compose)
                File.WriteAllText(destino, compose, new UTF8Encoding(false));
        }

        // O compose lê as variáveis do .env da mesma pasta. Barras normais: o Docker aceita "C:/..." no Windows.
        var env = $"PASTA_MIDIA={PastaMidia.Replace('\\', '/')}\nTZ={FusoHorario}\n";
        File.WriteAllText(Path.Combine(pastaApp, ".env"), env, new UTF8Encoding(false));
    }
}
