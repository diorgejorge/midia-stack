# radarr-lang-priority

Faz o Radarr preferir áudio num idioma (padrão: PT-BR) e trocar o arquivo quando aparecer um release com esse áudio.
Só Python 3, sem dependências. Requer Radarr v5+.

## Como funciona

1. **`setup`** (uma vez): **faça backup antes** (*System → Backup → Backup Now*). Cria o Custom Format
   `Preferred Audio Language` (idioma detectado **ou** título com `DUAL|DUBLADO|NACIONAL|PT-BR`) e, em cada Quality Profile:
   - score do CF = `CF_SCORE` (1000);
   - `Upgrades Allowed` ligado, `Upgrade Until Custom Format Score` ≥ 1000;
   - idioma do profile = `Any` (com `Original` o Radarr rejeitaria o dublado; com `Portuguese (Brazil)` não baixaria
     nada enquanto não houver dublado).

   A partir daí o RSS do próprio Radarr já faz o upgrade quando surgir um release dublado.
2. **`search`** (agendado): o RSS só pega releases novos. Esse comando procura filmes monitorados cujo arquivo
   atual não tem o idioma e dispara busca para no máximo `SEARCH_LIMIT` por execução, respeitando `COOLDOWN_DAYS`
   por filme, para não sobrecarregar os indexadores.

## Uso

A API key é lida de `config\radarr\config.xml` do stack; não precisa guardá-la em lugar nenhum.

```
py radarr_lang_priority.py setup
py radarr_lang_priority.py search
```

Agendar no Windows a cada 6h (PowerShell):

```powershell
Register-ScheduledTask -TaskName 'Radarr PT-BR search' -Action (New-ScheduledTaskAction -Execute "$PWD\run_search.cmd") -Trigger (New-ScheduledTaskTrigger -Once -At (Get-Date).AddHours(6) -RepetitionInterval (New-TimeSpan -Hours 6)) -Settings (New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries)
```

No Linux, cron: `0 */6 * * * cd /caminho/scripts/radarr-lang-priority && python3 radarr_lang_priority.py search`.

## Variáveis

| Variável | Padrão | |
|---|---|---|
| `RADARR_URL` | `http://localhost:7878` | |
| `RADARR_API_KEY` | lida do `config.xml` | |
| `RADARR_CONFIG` | `../../config/radarr/config.xml` | onde ler a API key |
| `LANG_NAMES` | `Portuguese (Brazil)` | nomes do Radarr, separados por vírgula |
| `TITLE_REGEX` | `\b(DUAL\|DUBLADO\|NACIONAL\|PT[-. ]?BR)\b` | casa títulos de release |
| `CF_NAME` | `Preferred Audio Language` | |
| `CF_SCORE` | `1000` | |
| `PROFILES` | todos | profiles a alterar, separados por vírgula |
| `SEARCH_LIMIT` | `10` | buscas por execução |
| `COOLDOWN_DAYS` | `7` | intervalo mínimo entre buscas do mesmo filme |
| `STATE_FILE` | `./radarr_lang_priority_state.json` | |

## Reverter

Restaure o backup em *System → Backup → Restore*, ou apague o Custom Format e volte o idioma dos profiles.
