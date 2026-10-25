using System.Globalization;

namespace MidiaStack;

/// <summary>
/// Idioma da interface: português, inglês ou chinês (simplificado). Cada texto fica no ponto de uso como
/// T.S("pt", "en", "zh"), sem arquivos de recursos. Sem escolha salva, segue o idioma do Windows
/// (português e chinês quando o Windows estiver neles; inglês para o resto).
/// </summary>
internal static class T
{
    public const string Portugues = "pt", Ingles = "en", Chines = "zh";

    public static readonly (string Codigo, string Nome)[] Idiomas =
    [
        (Portugues, "Português"),
        (Ingles, "English"),
        (Chines, "中文（简体）"),
    ];

    public static string Atual { get; private set; } = DoSistema();

    /// <summary>Código vazio ou desconhecido = idioma do Windows.</summary>
    public static void Definir(string? codigo) =>
        Atual = Idiomas.Any(i => i.Codigo == codigo) ? codigo! : DoSistema();

    public static string DoSistema()
    {
        // Linux (com globalização invariante, sem ICU): o idioma vem das variáveis LANGUAGE/LC_ALL/LANG.
        var nome = OperatingSystem.IsWindows()
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : new[] { "LANGUAGE", "LC_ALL", "LANG" }
                .Select(Environment.GetEnvironmentVariable)
                .FirstOrDefault(v => !string.IsNullOrEmpty(v) && v != "C" && v != "POSIX")?[..2] ?? "en";
        return nome switch { "pt" => Portugues, "zh" => Chines, _ => Ingles };
    }

    /// <summary>Nome do app na interface.</summary>
    public static string App => S("Mídia Stack", "Media Stack", "Media Stack");

    public static string S(string pt, string en, string zh) => Atual switch
    {
        Portugues => pt,
        Chines => zh,
        _ => en,
    };
}
