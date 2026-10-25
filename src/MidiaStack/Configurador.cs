using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace MidiaStack;

/// <summary>
/// Liga os programas entre si depois de subir: Prowlarr → Sonarr/Radarr (indexadores),
/// Sonarr/Radarr → qBittorrent (downloads) e Seerr → Sonarr/Radarr (pedidos).
/// Tudo é idempotente: o que já está configurado fica como está.
/// </summary>
internal sealed class Configurador(Docker docker, Ajustes ajustes, Action<string> progresso)
{
    /// <summary>Um *arr da pilha. Ip = IP fixo no docker-compose.yml, liberado sem senha no qBittorrent.</summary>
    private sealed record Arr(
        string Nome, string Container, int Porta, string Ip,
        string CampoCategoria, string Categoria, string ComandoBusca);

    private static readonly Arr Sonarr = new(
        "Sonarr", "sonarr", 8989, "172.30.50.10", "tvCategory", "tv-sonarr", "MissingEpisodeSearch");

    private static readonly Arr Radarr = new(
        "Radarr", "radarr", 7878, "172.30.50.11", "movieCategory", "radarr", "MissingMoviesSearch");

    private static string RotuloBusca(Arr arr) => arr == Sonarr
        ? T.S("episódios", "episodes", "剧集")
        : T.S("filmes", "movies", "电影");

    /// <summary>Pasta raiz do *arr vista de dentro do container (a subpasta escolhida no assistente).</summary>
    private string PastaRaiz(Arr arr) => arr == Sonarr ? ajustes.DataSeries : ajustes.DataFilmes;

    private static readonly Arr[] Arrs = [Sonarr, Radarr];

    // Perfil de qualidade padrão para o que o Seerr pede (existe de fábrica no Sonarr e no Radarr).
    private const string PerfilPadrao = "HD - 720p/1080p";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private string PastaConfig => Path.Combine(docker.PastaCompose, "config");

    /// <summary>Abre o Docker se preciso, sobe a pilha e liga os programas. Retorna a mensagem final.</summary>
    public async Task<(bool Ok, string Mensagem)> SubirEConfigurarAsync()
    {
        if (!docker.Instalado)
            return (false, T.S("Docker Desktop não está instalado.", "Docker Desktop is not installed.", "未安装 Docker Desktop。"));

        // Pastas de mídia + docker-compose.yml e .env com os caminhos desta máquina.
        ajustes.PrepararPasta(docker.PastaCompose);
        if (!docker.DesktopAberto)
        {
            progresso(T.S("Abrindo o Docker…", "Opening Docker…", "正在打开 Docker…"));
            docker.AbrirDesktop();
        }
        if (!await docker.EsperarMotorAsync(TimeSpan.FromMinutes(3)))
            return (false, T.S("O Docker não respondeu. Abra o Docker Desktop manualmente e tente de novo.",
                               "Docker did not respond. Open Docker Desktop manually and try again.",
                               "Docker 没有响应。请手动打开 Docker Desktop 后重试。"));

        progresso(T.S("Subindo os programas…", "Starting the programs…", "正在启动程序…"));
        var (codigo, saida) = await docker.ComposeAsync("up -d");
        if (codigo != 0) return (false, UltimaLinha(saida));

        try
        {
            return (true, await ConfigurarAsync());
        }
        catch (Exception e)
        {
            Log.Escrever($"configuração falhou: {e}");
            return (false, T.S("Programas no ar, mas a configuração automática falhou: ",
                               "Programs are running, but the automatic configuration failed: ",
                               "程序已运行，但自动配置失败：") + e.Message);
        }
    }

    private async Task<string> ConfigurarAsync()
    {
        progresso(T.S("Aguardando os programas…", "Waiting for the programs…", "正在等待程序…"));
        // Na primeira execução os arquivos de configuração só existem depois que cada programa inicia.
        string Xml(string container) => Path.Combine(PastaConfig, container, "config.xml");
        var prowlarrXml = Xml("prowlarr");
        var qbitConf = Path.Combine(PastaConfig, "qbittorrent", "qBittorrent", "qBittorrent.conf");
        var seerrJson = Path.Combine(PastaConfig, "seerr", "settings.json");
        await EsperarArquivosAsync([.. Arrs.Select(a => Xml(a.Container)), prowlarrXml, qbitConf, seerrJson]);

        // Ajustes em arquivo: o programa precisa estar parado (o qBittorrent regrava o .conf ao sair).
        // Só a liberação de IPs vai por arquivo: é ela que permite mexer no resto pela API depois.
        await AjustarArquivoAsync("qbittorrent", qbitConf, LiberarArrsNoQbit);
        foreach (var a in Arrs)
            await AjustarArquivoAsync(a.Container, Xml(a.Container), t => GarantirAllowedHosts(t, a.Container));
        await AjustarArquivoAsync("prowlarr", prowlarrXml, t => GarantirAllowedHosts(t, "prowlarr"));

        var prowlarr = new Api("http://127.0.0.1:9696/api/v1", ApiKey(prowlarrXml));
        var apis = Arrs.ToDictionary(a => a, a => new Api($"http://127.0.0.1:{a.Porta}/api/v3", ApiKey(Xml(a.Container))));
        foreach (var api in apis.Values) await api.EsperarAsync(TimeSpan.FromMinutes(3));
        await prowlarr.EsperarAsync(TimeSpan.FromMinutes(3));
        await EsperarQbitAsync();

        progresso(T.S("Ligando os programas…", "Connecting the programs…", "正在连接程序…"));
        var novidades = new List<string>();
        if (await GarantirPreferenciasQbitAsync())
            novidades.Add(T.S("preferências do qBittorrent", "qBittorrent preferences", "qBittorrent 首选项"));
        var buscar = new List<Arr>();
        foreach (var (arr, api) in apis)
        {
            var antes = novidades.Count;
            if (await GarantirPastaRaizAsync(arr, api))
                novidades.Add(T.S($"pasta do {arr.Nome}", $"{arr.Nome} folder", $"{arr.Nome} 文件夹"));
            if (await GarantirClienteQbitAsync(arr, api))
                novidades.Add(T.S($"qBittorrent no {arr.Nome}", $"qBittorrent in {arr.Nome}", $"{arr.Nome} 连接 qBittorrent"));
            if (await GarantirAppProwlarrAsync(arr, prowlarr, api.Chave))
                novidades.Add(T.S($"Prowlarr no {arr.Nome}", $"Prowlarr in {arr.Nome}", $"{arr.Nome} 连接 Prowlarr"));
            if (novidades.Count > antes) buscar.Add(arr);
        }

        // Aviso ao Jellyfin: falha aqui (chave errada, Jellyfin desligado) não derruba o resto da configuração.
        var avisos = new List<string>();
        if (ajustes.JellyfinChave.Length > 0 && (!ajustes.JellyfinAplicado || !await JellyfinComEventosAsync(apis.Values)))
        {
            var todos = true;
            foreach (var (arr, api) in apis)
            {
                try
                {
                    if (await GarantirJellyfinNoArrAsync(arr, api))
                        novidades.Add(T.S($"Jellyfin no {arr.Nome}", $"Jellyfin in {arr.Nome}", $"{arr.Nome} 连接 Jellyfin"));
                }
                catch (InvalidOperationException e)
                {
                    todos = false;
                    Log.Escrever($"Jellyfin no {arr.Nome}: {e.Message}");
                    avisos.Add(T.S($" Não liguei o Jellyfin no {arr.Nome}: {e.Message}",
                                   $" Could not connect Jellyfin in {arr.Nome}: {e.Message}",
                                   $"无法在 {arr.Nome} 中连接 Jellyfin：{e.Message}"));
                }
            }
            if (todos)
            {
                ajustes.JellyfinAplicado = true;
                ajustes.Salvar(docker.PastaCompose);
            }
        }

        if (await GarantirMinimoSeedsAsync(prowlarr))
            novidades.Add(T.S($"mínimo de {ajustes.MinimoSeeds} seeds", $"minimum of {ajustes.MinimoSeeds} seeders", $"最少 {ajustes.MinimoSeeds} 个做种"));

        // Empurra os indexadores do Prowlarr para Sonarr/Radarr (rápido; roda sempre).
        await prowlarr.PostAsync("command", new JsonObject { ["name"] = "ApplicationIndexerSync" });

        // O Seerr só aceita a chave de API depois do primeiro login (o assistente cria o administrador
        // a partir da conta do Jellyfin); até lá, fica pendente e é ligado numa próxima subida.
        var seerr = new Api("http://127.0.0.1:5055/api/v1", ApiKeySeerr(seerrJson), "status");
        await seerr.EsperarAsync(TimeSpan.FromMinutes(2));
        var seerrIniciado = (bool?)(await seerr.GetAsync("settings/public"))?["initialized"] == true;
        // O login do assistente já cria o administrador; a chave de API funciona a partir daí, mesmo
        // que o último clique ("Finish Setup") não tenha sido dado.
        var seerrPronto = seerrIniciado || await SeerrTemAdminAsync(seerr);
        if (seerrPronto)
        {
            foreach (var (arr, api) in apis)
                if (await GarantirArrNoSeerrAsync(arr, api, seerr))
                    novidades.Add(T.S($"{arr.Nome} no Seerr", $"{arr.Nome} in Seerr", $"Seerr 连接 {arr.Nome}"));
            if (!seerrIniciado)
            {
                await seerr.PostAsync("settings/initialize", new JsonObject());
                novidades.Add(T.S("assistente do Seerr concluído", "Seerr setup finished", "Seerr 设置已完成"));
            }
        }
        var pendente = string.Concat(avisos) + (seerrPronto
            ? ""
            : T.S(" Falta só o primeiro login no Seerr (http://localhost:5055) com a sua conta do Jellyfin; depois use \"Conferir configuração\".",
                  " Only the first Seerr login is left (http://localhost:5055) with your Jellyfin account; then use \"Check configuration\".",
                  "只差用 Jellyfin 账号首次登录 Seerr（http://localhost:5055）；之后请使用“检查配置”。"));

        if (novidades.Count == 0)
            return T.S("Tudo no ar e já configurado.", "All running and already configured.", "全部运行中，已配置完成。") + pendente;

        // Acabou de ligar um *arr: busca de uma vez o que falta nele.
        if (buscar.Count > 0) await Task.Delay(TimeSpan.FromSeconds(10));
        foreach (var arr in buscar)
            await apis[arr].PostAsync("command", new JsonObject { ["name"] = arr.ComandoBusca });

        Log.Escrever("configurado: " + string.Join(", ", novidades));
        var rotulos = buscar.Select(RotuloBusca).ToList();
        var busca = buscar.Count == 0 ? "" : T.S(
            $" Buscando {string.Join(" e ", rotulos)} que faltam.",
            $" Searching for missing {string.Join(" and ", rotulos)}.",
            $"正在搜索缺少的{string.Join("和", rotulos)}。");
        return T.S("Configurado: ", "Configured: ", "已配置：")
            + string.Join(T.S(", ", ", ", "、"), novidades) + T.S(".", ".", "。") + busca + pendente;
    }

    // ---------- arquivos ----------

    private async Task EsperarArquivosAsync(string[] arquivos)
    {
        var fim = DateTime.UtcNow.AddMinutes(3);
        while (arquivos.Any(a => !File.Exists(a)))
        {
            if (DateTime.UtcNow > fim)
                throw new TimeoutException(T.S("Arquivo de configuração não apareceu: ", "Configuration file did not appear: ", "配置文件未出现：")
                    + arquivos.First(a => !File.Exists(a)));
            await Task.Delay(2000);
        }
    }

    /// <summary>Se o ajuste muda o arquivo: para o container, grava e sobe de novo.</summary>
    private async Task AjustarArquivoAsync(string servico, string arquivo, Func<string, string> ajuste)
    {
        if (ajuste(await File.ReadAllTextAsync(arquivo)) == await File.ReadAllTextAsync(arquivo)) return;

        progresso(T.S($"Ajustando {servico}…", $"Adjusting {servico}…", $"正在调整 {servico}…"));
        await docker.ComposeAsync("stop " + servico);
        // Relê depois de parar: o programa pode ter regravado o arquivo ao sair.
        var atual = await File.ReadAllTextAsync(arquivo);
        await File.WriteAllTextAsync(arquivo, ajuste(atual), new UTF8Encoding(false));
        Log.Escrever($"ajustado {arquivo}");
        await docker.ComposeAsync("up -d " + servico);
    }

    /// <summary>
    /// Libera os IPs do Sonarr e do Radarr no Web UI do qBittorrent. Até a 4.x a opção fica em [Preferences]
    /// como "WebUI\..."; a 5.x migra para a seção [WebUI] sem o prefixo. Grava nos dois formatos.
    /// </summary>
    private static string LiberarArrsNoQbit(string ini)
    {
        var faixas = string.Join(", ", Arrs.Select(a => $"{a.Ip}/32"));
        ini = DefinirIni(ini, "Preferences", @"WebUI\AuthSubnetWhitelistEnabled", "true");
        ini = DefinirIni(ini, "Preferences", @"WebUI\AuthSubnetWhitelist", faixas);
        ini = DefinirIni(ini, "WebUI", "AuthSubnetWhitelistEnabled", "true");
        return DefinirIni(ini, "WebUI", "AuthSubnetWhitelist", faixas);
    }

    private static string DefinirIni(string ini, string secao, string chave, string valor)
    {
        var linhas = ini.Replace("\r\n", "\n").Split('\n').ToList();
        var inicio = linhas.FindIndex(l => l.Trim() == $"[{secao}]");
        if (inicio < 0)
        {
            if (linhas.Count > 0 && linhas[^1] != "") linhas.Add("");
            linhas.Add($"[{secao}]");
            inicio = linhas.Count - 1;
        }
        var fim = linhas.FindIndex(inicio + 1, l => l.StartsWith('['));
        if (fim < 0) fim = linhas.Count;

        var i = linhas.FindIndex(inicio + 1, fim - inicio - 1, l => l.StartsWith(chave + "="));
        if (i >= 0) linhas[i] = $"{chave}={valor}";
        else
        {
            // Insere antes das linhas em branco do fim da seção.
            var pos = fim;
            while (pos - 1 > inicio && linhas[pos - 1].Trim() == "") pos--;
            linhas.Insert(pos, $"{chave}={valor}");
        }
        return string.Join("\n", linhas);
    }

    /// <summary>
    /// Allowed Hosts vazio libera tudo. Preenchido, precisa incluir o navegador (localhost) e o nome do
    /// container, senão um programa não consegue falar com o outro.
    /// </summary>
    private static string GarantirAllowedHosts(string xml, string nomeContainer)
    {
        var doc = XDocument.Parse(xml);
        var el = doc.Root?.Element("AllowedHosts");
        if (el == null || string.IsNullOrWhiteSpace(el.Value)) return xml;

        var hosts = el.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var faltando = new[] { "localhost", "127.0.0.1", nomeContainer }
            .Where(h => !hosts.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        if (faltando.Count == 0) return xml;

        el.Value = string.Join(", ", hosts.Concat(faltando));
        return doc.ToString();
    }

    private static string ApiKey(string configXml) =>
        XDocument.Load(configXml).Root?.Element("ApiKey")?.Value
        ?? throw new InvalidOperationException("ApiKey não encontrada em " + configXml);

    private static string ApiKeySeerr(string settingsJson) =>
        (string?)JsonNode.Parse(File.ReadAllText(settingsJson))?["main"]?["apiKey"]
        ?? throw new InvalidOperationException("apiKey não encontrada em " + settingsJson);

    // ---------- APIs ----------

    private static async Task EsperarQbitAsync()
    {
        var fim = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < fim)
        {
            if (await Docker.PortaAbertaAsync(8080)) return;
            await Task.Delay(2000);
        }
    }

    /// <summary>
    /// Preferências do qBittorrent, pela API a partir do container do Sonarr (IP liberado sem senha); mudam na
    /// hora, sem reiniciar, e só são enviadas as que estiverem diferentes:
    /// - pastas: downloads em /data/&lt;downloads&gt;, incompletos numa subpasta; porta fixa 6881 (a liberada no compose);
    /// - fila: o padrão de fábrica (3 downloads ativos) deixa a maior parte do que o Sonarr manda parada; torrents
    ///   lentos não ocupam vaga, para um torrent sem seeds não travar os outros;
    /// - seed por N dias com ação "parar": o Sonarr/Radarr importa assim que o download termina (por hardlink: o
    ///   arquivo na pasta da série e o do torrent ocupam espaço uma vez só) e, quando o torrent para ao fim do
    ///   seed, apaga ele da lista ("Remove Completed", ligado por padrão). Sem meta, o *arr nunca remove.
    /// 0 no assistente = sem limite: 0 downloads simultâneos desliga a fila; 0 dias de seed = seed para sempre
    /// (o torrent fica na lista até ser removido à mão).
    /// </summary>
    private async Task<bool> GarantirPreferenciasQbitAsync()
    {
        const string url = "http://qbittorrent:8080/api/v2/app";
        var desejado = new JsonObject
        {
            // Sem barra no fim: é assim que o qBittorrent devolve os caminhos (senão parece sempre diferente).
            ["save_path"] = ajustes.DataDownloads,
            ["temp_path_enabled"] = true,
            ["temp_path"] = ajustes.DataDownloads + "/incompleto",
            ["listen_port"] = 6881,
            ["upnp"] = false,
            ["queueing_enabled"] = ajustes.DownloadsSimultaneos > 0,
            ["max_ratio_enabled"] = false,
            ["max_seeding_time_enabled"] = ajustes.DiasDeSeed > 0,
        };
        if (ajustes.DownloadsSimultaneos > 0)
        {
            desejado["max_active_downloads"] = ajustes.DownloadsSimultaneos;
            desejado["max_active_uploads"] = 4;
            desejado["max_active_torrents"] = ajustes.DownloadsSimultaneos + 4;
            desejado["dont_count_slow_torrents"] = true;
        }
        if (ajustes.DiasDeSeed > 0)
        {
            desejado["max_seeding_time"] = ajustes.DiasDeSeed * 24 * 60;
            desejado["max_ratio_act"] = 0;
        }

        // Sem registrar a saída: é a lista inteira de preferências e encheria o log a cada subida.
        var (codigo, saida) = await docker.ExecAsync(Sonarr.Container, $"curl -s -m 10 {url}/preferences", registrar: false);
        if (codigo != 0)
            throw new InvalidOperationException(T.S("qBittorrent não respondeu: ", "qBittorrent did not respond: ", "qBittorrent 没有响应：") + UltimaLinha(saida));
        var atual = JsonNode.Parse(saida)!;

        var diferentes = new JsonObject();
        foreach (var (chave, valor) in desejado)
            if (atual[chave]?.ToJsonString() != valor!.ToJsonString())
                diferentes[chave] = valor.DeepClone();
        if (diferentes.Count == 0) return false;

        // URL-encoded: sem aspas na linha de comando do docker exec.
        (codigo, saida) = await docker.ExecAsync(Sonarr.Container,
            $"curl -s -m 10 -f -d json={Uri.EscapeDataString(diferentes.ToJsonString())} {url}/setPreferences");
        if (codigo != 0)
            throw new InvalidOperationException(T.S("Não consegui ajustar o qBittorrent: ", "Could not adjust qBittorrent: ", "无法调整 qBittorrent：") + UltimaLinha(saida));
        Log.Escrever("qBittorrent ajustado: " + string.Join(", ", diferentes.Select(p => p.Key)));
        return true;
    }

    /// <summary>
    /// Mínimo de seeds nos perfis de sincronização do Prowlarr: ele repassa o valor a cada indexador do
    /// Sonarr/Radarr, que passam a recusar torrents com menos seeds (quase mortos, baixam devagar ou nunca).
    /// </summary>
    private async Task<bool> GarantirMinimoSeedsAsync(Api prowlarr)
    {
        var mudou = false;
        foreach (var perfil in ((JsonArray)(await prowlarr.GetAsync("appprofile"))!).Select(p => p!.AsObject()))
        {
            if ((int?)perfil["minimumSeeders"] == ajustes.MinimoSeeds) continue;
            var novo = perfil.DeepClone().AsObject();
            novo["minimumSeeders"] = ajustes.MinimoSeeds;
            await prowlarr.PutAsync($"appprofile/{(int)perfil["id"]!}", novo);
            mudou = true;
        }
        return mudou;
    }

    private async Task<bool> GarantirPastaRaizAsync(Arr arr, Api api)
    {
        var pastas = (JsonArray)(await api.GetAsync("rootfolder"))!;
        if (pastas.Any(p => ((string?)p?["path"])?.TrimEnd('/') == PastaRaiz(arr))) return false;
        await api.PostAsync("rootfolder", new JsonObject { ["path"] = PastaRaiz(arr) });
        return true;
    }

    private static async Task<bool> GarantirClienteQbitAsync(Arr arr, Api api)
    {
        var clientes = (JsonArray)(await api.GetAsync("downloadclient"))!;
        if (clientes.Any(c => (string?)c?["implementation"] == "QBittorrent")) return false;

        var schema = ((JsonArray)(await api.GetAsync("downloadclient/schema"))!)
            .First(s => (string?)s?["implementation"] == "QBittorrent")!.DeepClone().AsObject();
        schema["name"] = "qBittorrent";
        schema["enable"] = true;
        DefinirCampo(schema, "host", "qbittorrent");
        DefinirCampo(schema, "port", 8080);
        DefinirCampo(schema, arr.CampoCategoria, arr.Categoria);
        await api.PostAsync("downloadclient", schema);
        return true;
    }

    private static async Task<bool> GarantirAppProwlarrAsync(Arr arr, Api prowlarr, string chaveArr)
    {
        var apps = (JsonArray)(await prowlarr.GetAsync("applications"))!;
        var existente = apps.FirstOrDefault(a => (string?)a?["implementation"] == arr.Nome)?.AsObject();

        var app = existente?.DeepClone().AsObject()
            ?? ((JsonArray)(await prowlarr.GetAsync("applications/schema"))!)
                .First(s => (string?)s?["implementation"] == arr.Nome)!.DeepClone().AsObject();

        var mudou = existente == null;
        mudou |= DefinirCampo(app, "prowlarrUrl", "http://prowlarr:9696");
        mudou |= DefinirCampo(app, "baseUrl", $"http://{arr.Container}:{arr.Porta}");
        // A API devolve a chave mascarada ("********"); só grava a chave quando cria a ligação.
        if (existente == null) DefinirCampo(app, "apiKey", chaveArr);
        if (!mudou) return false;

        app["name"] = arr.Nome;
        app["syncLevel"] ??= "fullSync";
        if (existente == null) await prowlarr.PostAsync("applications", app);
        else await prowlarr.PutAsync($"applications/{(int)existente["id"]!}", app);
        return true;
    }

    private static async Task<bool> SeerrTemAdminAsync(Api seerr)
    {
        try
        {
            var usuarios = await seerr.GetAsync("user?take=1");
            return (int?)usuarios?["pageInfo"]?["results"] > 0;
        }
        catch (InvalidOperationException)
        {
            return false; // 403 antes do primeiro login
        }
    }

    // Eventos em que o Sonarr/Radarr avisam o Jellyfin; cada um só é ligado se aquele programa tiver o evento.
    private static readonly string[] GatilhosJellyfin =
    [
        "onDownload", "onUpgrade", "onImportComplete", "onRename",
        "onSeriesDelete", "onEpisodeFileDelete", "onEpisodeFileDeleteForUpgrade",
        "onMovieDelete", "onMovieFileDelete", "onMovieFileDeleteForUpgrade",
    ];

    /// <summary>A conexão com o Jellyfin existe nos dois e avisa ao importar (versões antigas gravavam sem eventos).</summary>
    private static async Task<bool> JellyfinComEventosAsync(IEnumerable<Api> apis)
    {
        foreach (var api in apis)
        {
            var conexao = ((JsonArray)(await api.GetAsync("notification"))!)
                .FirstOrDefault(n => (string?)n?["implementation"] == "MediaBrowser");
            if ((bool?)conexao?["onDownload"] != true) return false;
        }
        return true;
    }

    /// <summary>
    /// Conexão "Emby / Jellyfin" no Sonarr/Radarr: ao importar, renomear ou apagar, eles pedem ao Jellyfin que
    /// atualize a pasta, e o episódio aparece na hora. Com o Jellyfin no mesmo PC, mapeia /data/ (caminho nos
    /// containers) para a pasta de mídia do Windows, senão o Jellyfin não acha a pasta que deve atualizar.
    /// </summary>
    private async Task<bool> GarantirJellyfinNoArrAsync(Arr arr, Api api)
    {
        if (ajustes.JellyfinParaContainers is not { } uri) return false;

        var existente = ((JsonArray)(await api.GetAsync("notification"))!)
            .FirstOrDefault(n => (string?)n?["implementation"] == "MediaBrowser")?.AsObject();
        var conexao = existente?.DeepClone().AsObject()
            ?? ((JsonArray)(await api.GetAsync("notification/schema"))!)
                .First(s => (string?)s?["implementation"] == "MediaBrowser")!.DeepClone().AsObject();

        conexao["name"] = "Jellyfin";
        DefinirCampo(conexao, "host", uri.Host);
        DefinirCampo(conexao, "port", uri.Port);
        DefinirCampo(conexao, "useSsl", uri.Scheme == Uri.UriSchemeHttps);
        DefinirCampo(conexao, "urlBase", uri.AbsolutePath.TrimEnd('/'));
        DefinirCampo(conexao, "apiKey", ajustes.JellyfinChave);
        DefinirCampo(conexao, "updateLibrary", true);
        DefinirCampo(conexao, "notify", false);
        if (ajustes.JellyfinLocal)
        {
            DefinirCampo(conexao, "mapFrom", "/data/");
            DefinirCampo(conexao, "mapTo", ajustes.PastaMidiaParaJellyfin);
        }
        // "onDownload" ↔ "supportsOnDownload".
        foreach (var gatilho in GatilhosJellyfin)
            if ((bool?)conexao["supports" + char.ToUpper(gatilho[0]) + gatilho[1..]] == true)
                conexao[gatilho] = true;

        // Salvar não testa a chave: testa antes, para não gravar uma conexão que não funciona.
        await api.PostAsync("notification/test", conexao);
        if (existente == null) await api.PostAsync("notification", conexao);
        else await api.PutAsync($"notification/{(int)existente["id"]!}", conexao);
        return true;
    }

    /// <summary>Cadastra o Sonarr/Radarr no Seerr como servidor padrão, para os pedidos irem direto para ele.</summary>
    private async Task<bool> GarantirArrNoSeerrAsync(Arr arr, Api api, Api seerr)
    {
        var rota = $"settings/{arr.Container}";
        var servidores = (JsonArray)(await seerr.GetAsync(rota))!;
        if (servidores.Count > 0) return false;

        var perfis = (JsonArray)(await api.GetAsync("qualityprofile"))!;
        var perfil = perfis.FirstOrDefault(p => (string?)p?["name"] == PerfilPadrao) ?? perfis.First()!;

        var servidor = new JsonObject
        {
            ["name"] = arr.Nome,
            ["hostname"] = arr.Container,
            ["port"] = arr.Porta,
            ["apiKey"] = api.Chave,
            ["useSsl"] = false,
            ["baseUrl"] = "",
            ["activeProfileId"] = (int)perfil!["id"]!,
            ["activeProfileName"] = (string?)perfil["name"],
            ["activeDirectory"] = PastaRaiz(arr),
            ["is4k"] = false,
            ["isDefault"] = true,
            ["externalUrl"] = $"http://localhost:{arr.Porta}",
            ["syncEnabled"] = true,
            ["preventSearch"] = false,
            ["tags"] = new JsonArray(),
        };
        if (arr == Sonarr)
        {
            servidor["activeAnimeProfileId"] = (int)perfil["id"]!;
            servidor["activeAnimeProfileName"] = (string?)perfil["name"];
            servidor["activeAnimeDirectory"] = PastaRaiz(arr);
            servidor["animeTags"] = new JsonArray();
            servidor["enableSeasonFolders"] = true;
            servidor["seriesType"] = "standard";
            servidor["animeSeriesType"] = "anime";
        }
        else
        {
            servidor["minimumAvailability"] = "released";
        }
        await seerr.PostAsync(rota, servidor);
        return true;
    }

    /// <summary>Muda o valor de um campo do "fields" do *arr; retorna se mudou.</summary>
    private static bool DefinirCampo(JsonObject recurso, string nome, JsonNode valor)
    {
        var campo = ((JsonArray)recurso["fields"]!).First(f => (string?)f?["name"] == nome)!.AsObject();
        if (campo["value"]?.ToJsonString() == valor.ToJsonString()) return false;
        campo["value"] = valor;
        return true;
    }

    private static string UltimaLinha(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim()
        ?? T.S("Veja o log.", "See the log.", "请查看日志。");

    private sealed class Api(string baseUrl, string chave, string rotaStatus = "system/status")
    {
        public string Chave => chave;

        public async Task EsperarAsync(TimeSpan limite)
        {
            var fim = DateTime.UtcNow + limite;
            while (true)
            {
                try
                {
                    using var r = await Http.SendAsync(Req(HttpMethod.Get, rotaStatus));
                    if (r.IsSuccessStatusCode) return;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                if (DateTime.UtcNow > fim)
                    throw new TimeoutException(T.S($"{baseUrl} não respondeu.", $"{baseUrl} did not respond.", $"{baseUrl} 没有响应。"));
                await Task.Delay(2000);
            }
        }

        public async Task<JsonNode?> GetAsync(string caminho)
        {
            using var r = await Http.SendAsync(Req(HttpMethod.Get, caminho));
            return JsonNode.Parse(await Ler(r, caminho));
        }

        public Task PostAsync(string caminho, JsonNode corpo) => Enviar(HttpMethod.Post, caminho, corpo);

        public Task PutAsync(string caminho, JsonNode corpo) => Enviar(HttpMethod.Put, caminho, corpo);

        private async Task Enviar(HttpMethod metodo, string caminho, JsonNode corpo)
        {
            var req = Req(metodo, caminho);
            req.Content = new StringContent(corpo.ToJsonString(), Encoding.UTF8, "application/json");
            using var r = await Http.SendAsync(req);
            await Ler(r, caminho);
        }

        private HttpRequestMessage Req(HttpMethod metodo, string caminho)
        {
            var req = new HttpRequestMessage(metodo, $"{baseUrl}/{caminho}");
            req.Headers.Add("X-Api-Key", chave);
            return req;
        }

        private static async Task<string> Ler(HttpResponseMessage r, string caminho)
        {
            var texto = await r.Content.ReadAsStringAsync();
            if (!r.IsSuccessStatusCode)
                throw new InvalidOperationException($"{caminho}: HTTP {(int)r.StatusCode} {Resumo(texto)}");
            return texto;
        }

        private static string Resumo(string corpo)
        {
            // Os *arr devolvem erros de validação como [{ "errorMessage": "..." }]; o Seerr, { "message": "..." }.
            try
            {
                var json = JsonNode.Parse(corpo);
                if (json is JsonArray erros)
                    return string.Join("; ", erros.Select(e => (string?)e?["errorMessage"]).Where(m => m != null));
                if ((string?)json?["message"] is { } msg) return msg;
            }
            catch { }
            return corpo.Length > 200 ? corpo[..200] : corpo;
        }
    }
}
