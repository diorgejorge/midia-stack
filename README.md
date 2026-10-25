# Mídia Stack

**Português** · [English](README.en.md) · [中文](README.zh-CN.md)

Um ícone perto do relógio do Windows que liga e desliga, com um clique, uma pilha de programas em Docker
para acompanhar séries e filmes da sua biblioteca do **Jellyfin**:

| Programa | Para quê | Endereço |
|---|---|---|
| **Seerr** (antigo Jellyseerr) | catálogo para pedir séries e filmes | http://localhost:5055 |
| **Sonarr** | séries: busca, baixa, renomeia e organiza | http://localhost:8989 |
| **Radarr** | filmes: o mesmo, para filmes | http://localhost:7878 |
| **Prowlarr** | gerencia os indexadores e repassa ao Sonarr/Radarr | http://localhost:9696 |
| **qBittorrent** | faz os downloads | http://localhost:8080 |

Desligado, nada fica consumindo memória: o app também fecha o Docker Desktop.

## Instalar numa máquina nova

1. Baixe o `MidiaStack.exe` da [página de releases](../../releases) e coloque numa pasta própria
   (ex.: `C:\MidiaStack`). Ele já traz o .NET dentro; não precisa instalar nada além do Docker.
2. Abra o `MidiaStack.exe`. Na primeira vez aparece o **assistente de instalação**:
   - **Pasta de mídia**: a mesma onde ficam as bibliotecas do Jellyfin (ex.: `C:\home\jellyfin`), e os
     nomes das subpastas de séries, filmes e downloads (são criadas se não existirem);
   - **Idioma** da interface: português, inglês ou chinês (o padrão segue o Windows);
   - **Downloads ao mesmo tempo**, **mínimo de seeds** e **dias de seed** (0 = sem limite em qualquer um;
     com 0 dias o torrent fica em seed para sempre e não sai da lista sozinho);
   - **Jellyfin** (opcional): endereço e uma chave de API (*Painel do Jellyfin → Chaves de API → +*; o
     botão **Testar** confere). Com ela, o que terminar de baixar aparece no Jellyfin na hora;
   - **Docker Desktop**: se não estiver instalado, o botão instala pelo `winget` (pede administrador e,
     no fim, um reinício; abra o Docker Desktop uma vez para aceitar os termos).
3. Clique **Concluir**. Ele sobe tudo e liga os programas entre si sozinho.

## Primeira configuração, passo a passo

Depois do assistente, o que só você pode fazer (uma vez; senhas e chaves ficam com você):

### 1. Indexadores (Prowlarr — http://localhost:9696)

*Indexers → Add Indexer*, filtre por categoria **TV** e/ou **Movies**, clique no indexador, **Test** e,
se der ✓, **Save**. Eles vão para o Sonarr e o Radarr sozinhos (filmes só para o Radarr). Para dar
preferência a um indexador, mude **Indexer Priority** (1 = maior; o padrão é 25) — ele só desempata
lançamentos de mesma qualidade.

### 2. Seerr (http://localhost:5055)

1. Escolha **Jellyfin**.
2. **Jellyfin URL**: `host.docker.internal` — **sem** `http://` (o prefixo já aparece ao lado do campo) e
   **sem** `localhost` (dentro do Docker, `localhost` é o próprio Seerr). **Port**: `8096`.
3. E-mail, usuário e senha do Jellyfin → **Sign In** → **Sync Libraries**, marque séries e filmes →
   **Continue** → **Finish Setup** (a etapa de serviços pode ser pulada).
4. No ícone do Mídia Stack: **Conferir configuração** — liga o Seerr ao Sonarr e ao Radarr.

O Seerr cria no Jellyfin uma chave de API com o nome **Seerr**: não apague.

### 3. Chave de API do Jellyfin para o Mídia Stack (opcional, recomendado)

Com ela, o que termina de baixar aparece no Jellyfin na hora.

1. No Jellyfin (http://localhost:8096), como administrador: **Painel → Avançado → Chaves de API → + Nova
   chave de API**, nome `Mídia Stack` (atalho: http://localhost:8096/web/#/dashboard/keys).
2. No ícone do Mídia Stack: **Configurações…** → **Jellyfin** → cole em **Chave de API** → **Testar**
   (*conectado ✓*) → **Salvar**.

Use uma chave só para o Mídia Stack (não reaproveite a do Seerr). Trocar a chave em Configurações regrava
a conexão no Sonarr e no Radarr sozinho.

### 4. Senha fixa do qBittorrent (http://localhost:8080)

1. No ícone: **Senha do qBittorrent** (copia a senha temporária desta sessão).
2. Entre com o usuário `admin` e cole a senha.
3. *Ferramentas → Opções → Web UI → Autenticação*: digite a senha nova → **Salvar**.

O Sonarr e o Radarr continuam entrando sem senha (IPs liberados).

## Problemas comuns

| Sintoma | Causa e solução |
|---|---|
| Login no Seerr falha mesmo com a senha certa | O endereço ficou `http://http://…` ou `localhost`. Use só `host.docker.internal`, porta `8096`. |
| **Testar** do Jellyfin falha | Chave errada ou Jellyfin desligado. Confira em *Painel → Chaves de API*. |
| Nada baixa / fila vazia | Sem indexadores: veja *System → Status* no Sonarr/Radarr. Cadastre indexadores no Prowlarr. |
| Busca acha resultados mas recusa todos | Perfil de qualidade (ex.: "HD-720p" recusa 1080p), mínimo de seeds ou idioma. A busca interativa (ícone de pessoa no episódio) mostra o motivo de cada recusa. |
| Indexador falha no **Test** | Site bloqueado pela sua conexão ou atrás de proteção anti-robô (Cloudflare). Use outro. |
| Download lento | Poucos seeds conectados ou muitos torrents na fila. Ajuste "Downloads ao mesmo tempo" em Configurações. |
| Duas legendas aparecem juntas | Desligue a **legenda secundária** no player do Jellyfin; ou o arquivo marca o inglês como padrão. |
| Torrent concluído não sai da lista | É o seed: sai depois dos "Dias de seed" (só contam com o Mídia Stack ligado; 0 = nunca sai). |
| Windows avisa "O Windows protegeu o computador" ao abrir o .exe | O .exe não é assinado digitalmente: *Mais informações → Executar assim mesmo*. |

## Sugestão: legendas automáticas com o BabelSubs

O [BabelSubs](https://github.com/diorgejorge/jellyfin-plugin-babelsubs) é um plugin para o Jellyfin: quando um
vídeo começa sem legenda no seu idioma, ele traduz a legenda em inglês do próprio vídeo ou baixa a melhor dos
provedores instalados (ex.: OpenSubtitles), e já liga no player. Combina com o Mídia Stack, que traz muitos
lançamentos só com legenda em inglês.

No Jellyfin (conta de administrador): *Painel → Plugins → Repositórios → +*, cole
`https://raw.githubusercontent.com/diorgejorge/jellyfin-plugin-babelsubs/main/manifest.json`, depois
*Catálogo → BabelSubs → Instalar* e reinicie o Jellyfin. O item **Legendas automáticas (BabelSubs)…** do menu
copia o endereço e abre essa página; se ele ainda não estiver instalado, o app também dá a dica uma vez,
depois da primeira subida.

## Uso

Clique no ícone:

| Cor | Significa |
|---|---|
| cinza | tudo desligado |
| amarelo | trabalhando ou só parte no ar |
| verde | tudo no ar |

- **▶ Subir tudo** / **■ Parar tudo**; clique duplo abre o Seerr.
- **Senha do qBittorrent**: mostra e copia a senha temporária (ela muda a cada início até você definir
  uma fixa em *Ferramentas → Opções → Web UI*).
- **Configurações…**: reabre o assistente; se algo estiver no ar, aplica na hora.
- **Conferir configuração**: refaz a checagem com tudo no ar.
- **Atualizar programas**: baixa as versões novas das imagens.

## O que o "Subir tudo" configura sozinho

A cada subida ele confere e corrige só o que falta:

- pastas de séries e filmes no Sonarr e no Radarr;
- qBittorrent como cliente de download dos dois, **sem senha** só para eles (IPs fixos `172.30.50.10` e
  `172.30.50.11` liberados no qBittorrent; o navegador continua pedindo login);
- qBittorrent: pasta de downloads, porta 6881, fila (N downloads ao mesmo tempo, torrents lentos não
  ocupam vaga) e **seed por N dias**, depois o torrent sai da lista;
- importação por **hardlink** (o arquivo na pasta da série e o do torrent em seed ocupam espaço uma vez só);
- Prowlarr ligado ao Sonarr e ao Radarr, com **mínimo de seeds** e indexadores sincronizados;
- com a chave de API do Jellyfin: Sonarr e Radarr avisam o Jellyfin ao importar, renomear ou apagar
  (a conexão é testada antes de ser gravada; `/data/` é mapeado para a pasta de mídia do Windows);
- Sonarr e Radarr cadastrados no Seerr (perfil "HD - 720p/1080p"), depois do primeiro login nele;
- *Allowed Hosts* dos programas, se preenchido, inclui `localhost` e o nome do container.

Sem interface: `MidiaStack.exe --subir` (o resultado vai para o `midia-stack.log`).
Forçar o idioma ao abrir: `MidiaStack.exe --idioma pt|en|zh`.

## Arquivos na pasta do app

| Arquivo | O que é |
|---|---|
| `MidiaStack.exe` | o app |
| `midia-stack.json` | respostas do assistente |
| `docker-compose.yml`, `.env` | gerados pelo app a partir do que vai embutido no .exe |
| `config\` | configurações e bancos dos programas — **leve esta pasta junto** ao trocar de máquina para manter indexadores, histórico e pedidos |
| `midia-stack.log` | registro do que o app executou (sem senhas) |

## Compilar

Requer o SDK do .NET 10.

```bash
dotnet publish src/MidiaStack -c Release -o publish
```

O `publish\MidiaStack.exe` é autocontido (um arquivo só, com o .NET dentro).

## Licença

[MIT](LICENSE)
