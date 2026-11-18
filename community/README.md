# Resultados da comunidade / Community results

`results.json` é a base da **Comparação online** do Supernova Bench.

- O app lê este arquivo por HTTPS (`raw.githubusercontent.com/.../HEAD/community/results.json`) — só funciona com o repositório **público**.
- Para enviar um resultado, o app abre no navegador uma *issue* já preenchida (título começando com `[resultado]` e um bloco JSON).
- A GitHub Action `.github/workflows/community-results.yml` valida o bloco (`.github/scripts/accept_result.py`), adiciona aqui só os campos públicos (sem nome do computador, números de série ou caminhos) e fecha a issue com uma resposta.
- Resultados enviados não são verificados: servem para comparação aproximada.

`results.json` is the base for Supernova Bench's **Online comparison**. The app reads it over HTTPS (the repository must be public) and
submits results through a pre-filled GitHub issue that a GitHub Action validates and merges here (public fields only).
