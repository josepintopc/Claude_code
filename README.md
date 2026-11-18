# Supernova Bench

**Diagnóstico, benchmarks e ferramentas para o seu PC em um só lugar.** / *Diagnostics, benchmarks and tools for your PC in one place.*

Supernova Bench é um app para Windows 10/11 que junta, num só programa, o que normalmente exige vários:
informações de hardware no estilo **CPU-Z / GPU-Z / AIDA64**, benchmark de CPU no estilo **Cinebench**,
benchmark de GPU com cena 3D, stress de GPU no estilo **FurMark**, sensores em tempo real, benchmark de memória e cache no estilo
**AIDA64**, benchmark de disco no estilo **CrystalDiskMark**, saúde SMART no estilo **CrystalDiskInfo**, stress de CPU, teste de RAM,
**biblioteca de jogos** de todos os launchers e, na próxima fase, **atualizador de drivers**. Interface em **português (padrão) e inglês**, tema **escuro cósmico** ou claro.

> Projeto inspirado no [ResetExplode](https://github.com/GustavoReset/ResetExplode) (MIT), de Gustavo (GustavoReset).
> O motor de benchmark de CPU, a coleta via WMI e o monitor de sensores foram portados e adaptados de lá — veja
> [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## Fase 1 — o que já funciona

| Módulo | O que faz |
|---|---|
| **Visão geral** | Resumo do PC (CPU, GPU, RAM, placa-mãe, discos, Windows), monitor ao vivo com gráfico de temperatura de CPU/GPU, última pontuação e atalhos. |
| **Hardware** (CPU-Z / AIDA64) | Processador (núcleos, threads, caches, instruções AVX/AVX2/AVX-512...), placa-mãe e BIOS (modo UEFI, Secure Boot), memória por módulo (fabricante, part number, velocidade, tensão, slot) com **estimativa de EXPO/XMP e dual channel**, placa de vídeo (VRAM real pelo registro do driver), discos (SSD/HDD, NVMe/SATA, saúde) e sistema. Botão "Copiar seção" (sem números de série). |
| **Sensores** | Todas as temperaturas, cargas, clocks, consumo, tensões e ventoinhas (LibreHardwareMonitor), agrupados por hardware, com filtro, mín./máx. e diagnóstico quando a temperatura da CPU não aparece (com instalação do PawnIO pelo winget). |
| **Benchmark de CPU** (Cinebench) | Path tracer em cores, determinístico: **Multi-core**, **Single-core** e **Sustentado (10 min)**. A imagem aparece sendo renderizada bloco a bloco, com os blocos ativos destacados. Pontuação = amostras por segundo ÷ 10.000. Ranking com seus resultados e os de outros PCs importados. O PC não entra em suspensão durante o teste. |
| **Relatórios** | Exporta relatório completo em **HTML** (com a identidade visual do app) ou **JSON**. Importa o JSON de outro PC para comparar no ranking. Histórico de resultados. Nada de números de série ou nome do computador nos arquivos exportados. |
| **Configurações** | Idioma (PT-BR / English) e tema (escuro cósmico / claro) trocados na hora, intervalo dos sensores, pasta de dados. |

## Fase 2 — GPU

| Módulo | O que faz |
|---|---|
| **Hardware → Placa de vídeo** (GPU-Z) | Chip, arquitetura, processo, shaders/SM/RT/Tensor, fill rate e TFLOPS (banco de especificações), VRAM real, barramento e largura de banda, **Resizable BAR**, clocks, **PCIe atual e máximo**, limites de energia, VBIOS, driver (WHQL), CUDA e DirectX 12 Ultimate. Com NVIDIA, os dados vêm direto da NVML; nas outras marcas, de DXGI, registro e WMI. |
| **GPU (3D)** (3DMark/Unigine) | Cena de jogo em Direct3D 11 — vale com 40 mil árvores, sombras em tempo real 4096² com PCF, água, céu de entardecer e HDR — em 1080p, 1440p ou 4K por 30 ou 60 s. Pontuação = FPS médio × 10, mais **FPS médio** e **1% low**. 3 s de aquecimento fora da medição; sensores pausados durante o teste; ranking por resolução e duração. |
| **Stress de GPU** (FurMark) | Toro "peludo" com até 160 camadas instanciadas, MSAA e cadeia de FMAs no shader, sem limite de FPS. Resolução, MSAA, intensidade, duração e **limite de temperatura** (para sozinho). Mostra FPS, temperatura atual e máxima, carga, clock, consumo e o limitador de clock (PerfCap). |

## Fase 3 — RAM, SSD e stress de CPU

| Módulo | O que faz |
|---|---|
| **Memória RAM** (AIDA64 Cache & Memory) | Leitura, gravação e cópia (GB/s, todas as threads) e latência (ns, perseguição de ponteiros aleatória) na RAM e nos caches L1, L2 e L3, com os tamanhos de cache lidos do processador. Tabela preenchida ao vivo. Pontuação: **1.000 = DDR4-3200 dual channel típico** (média geométrica de leitura ÷ 45 GB/s, gravação ÷ 45, cópia ÷ 40 e 80 ns ÷ latência). Leva cerca de 30 s. |
| **Disco / SSD** (CrystalDiskMark) | Os 8 testes do perfil padrão: SEQ1M Q8T1, SEQ1M Q1T1, RND4K Q32T1 e RND4K Q1T1, em leitura e gravação, direto no disco (sem o cache do Windows), com arquivo de 256 MB, 1 GB ou 4 GB cheio de dados aleatórios e apagado no fim. Mostra MB/s, IOPS ou latência (µs). Pontuação: **1.000 = SSD SATA típico** (média geométrica das 8 medidas ÷ 560/530, 520/490, 380/330 e 45/120 MB/s). Ranking por unidade. |
| **Saúde dos discos** (CrystalDiskInfo) | Veredito **Bom / Atenção / Ruim**, vida restante, temperatura, horas ligado, ciclos, total lido/gravado e erros. NVMe: log de saúde 0x02 (via `IOCTL_STORAGE_QUERY_PROPERTY`). SATA: atributos SMART brutos (ID, atual, pior, limite, valor bruto). Também usa os contadores de confiabilidade do Windows. Aparece resumido em Hardware → Armazenamento. |
| **Stress de CPU** | Três cargas (calor máximo com FMA/AVX, render realista e inteiros), todas as threads, metade ou uma, **limite de temperatura** (80 a 95 °C) e duração. Cada bloco de cálculo é conferido: um resultado diferente é um **erro de cálculo** (CPU instável), como no Prime95. Gráfico de temperatura ao vivo, clock, consumo e carga. |
| **Teste de RAM** (TestMem5 / HCI MemTest) | Ocupa 50, 70 ou 85% da memória livre, divide entre as threads e repete quatro padrões (endereço, inversões móveis, bit andando e aleatório), com cobertura, velocidade e lista de erros (posição, esperado, lido). Resultado **Aprovado / Reprovado**. Não alcança a memória já usada pelo Windows (para isso, MemTest86). |

## Fase 4 — Jogos

| Módulo | O que faz |
|---|---|
| **Biblioteca** (estilo Playnite / GOG Galaxy) | Encontra sozinho os jogos instalados pela **Steam** (todas as bibliotecas, com o tempo de jogo da própria Steam), **Epic Games**, **GOG Galaxy**, **Ubisoft Connect**, **EA app**, **Battle.net** e **app Xbox / Game Pass**, além de jogos adicionados à mão (.exe). Grade de capas com busca, filtro por loja e ordenação (recentes, nome, mais jogados), favoritos e ocultos. **Jogar** abre o jogo pelo launcher dele. |
| **Capas** | Cache da própria Steam, CDN da Steam (para jogos de outras lojas, pela busca da loja Steam pelo nome — pode ser desligado nas Configurações), ícone do executável ou as iniciais. Ficam em `%AppData%\SupernovaBench\covers`. |
| **Tempo de jogo** | Com o Supernova aberto, ele percebe quando um jogo está rodando (por qualquer launcher) e conta o tempo, as sessões e a última partida. Para jogos da Steam, mostra o tempo registrado pela Steam. |
| **Perfil de desempenho por jogo** | Plano de energia (alto desempenho / desempenho máximo / equilibrado) aplicado quando o jogo abre e desfeito quando fecha, prioridade do processo e **GPU preferida** (o mesmo ajuste das Configurações de gráficos do Windows, útil em notebooks com duas placas). |

A Visão geral mostra os jogos jogados recentemente.

### Próximas fases

| Fase | Conteúdo |
|---|---|
| 5 — Drivers | Inventário de drivers, busca de atualizações (Windows Update, NVIDIA, AMD, Intel) e ponto de restauração antes de instalar. |
| 6 — Extras | Overlay de FPS, detector de gargalo e comparação online. |

---

## Download

Cada push gera o `SupernovaBench.exe` (arquivo único, já com o .NET embutido) no GitHub Actions:
**Actions → Build → execução mais recente → Artifacts → `SupernovaBench-win-x64`**.
Ao criar uma tag `v*` (ex.: `v0.1.0`), uma Release com o `.exe` e o `.zip` é publicada automaticamente.

O app pede **permissão de administrador** ao abrir (necessário para ler os sensores internos da CPU e a saúde dos discos).
Como o executável ainda não é assinado digitalmente, o Windows SmartScreen pode avisar: clique em *Mais informações → Executar assim mesmo*.

Para ver temperatura e consumo da CPU, o LibreHardwareMonitor precisa do driver gratuito **PawnIO** — o próprio app
detecta a falta e oferece instalar (`winget install namazso.PawnIO`).

## Compilar / Build

Requisitos: Windows 10/11 e [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet build SupernovaBench.sln -c Release
dotnet test tests/SupernovaBench.Core.Tests
dotnet run --project src/SupernovaBench.App -c Release
```

O app exige administrador (manifesto `requireAdministrator`): rode o terminal ou o Visual Studio **como administrador**
para usar `dotnet run`.

Gerar o `.exe` único:

```powershell
dotnet publish src/SupernovaBench.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

O núcleo (`SupernovaBench.Core`), a linha de comando e os testes também compilam e rodam em Linux/macOS.

### Linha de comando

```text
supernova-cli bench cpu [--mode multi|single|sustained] [--quick] [--threads N] [--minutes M] [--json] [--save] [--bmp arquivo.bmp]
supernova-cli bench gpu [--res 1080p|1440p|4k] [--seconds N] [--warp] [--headless] [--json] [--save]
supernova-cli bench memory [--quick] [--json] [--save]
supernova-cli bench disk [--drive C | --path PASTA] [--size 256m|1g|4g] [--runs N] [--seconds S] [--json] [--save]
supernova-cli stress cpu [--mode fma|render|int] [--threads N] [--seconds N]
supernova-cli stress ram [--mb N | --percent P] [--seconds N]
supernova-cli disk health
supernova-cli games list [--json]
supernova-cli gpu info
supernova-cli info
```

## Estrutura

```text
src/
  SupernovaBench.Core/      motores de benchmark (CPU, memória, disco), stress de CPU/RAM, leitura de SMART, pontuação,
                            histórico, relatórios, traduções (multiplataforma)
  SupernovaBench.Hardware/  WMI, registro, LibreHardwareMonitor e saúde dos discos (SMART/NVMe) (Windows)
  SupernovaBench.Graphics/  Direct3D 11 (Vortice): stress de GPU, cena 3D, DXGI, NVML e dados estilo GPU-Z (Windows)
  SupernovaBench.Games/     launchers de jogos (registro e arquivos), processos, plano de energia e GPU preferida (Windows)
  SupernovaBench.App/       interface WPF (MVVM): Views, ViewModels, Themes, Localization
  SupernovaBench.Cli/       benchmarks pela linha de comando
tests/SupernovaBench.Core.Tests/     testes do núcleo (rodam em qualquer sistema)
tests/SupernovaBench.Windows.Tests/  shaders, banco de GPUs, fumaça no WARP e leitura de discos (só rodam no Windows)
tests/SupernovaBench.App.Tests/      abre todas as páginas da interface nos dois temas e idiomas (Windows)
```

- **Traduções:** `src/SupernovaBench.Core/Localization/Strings/pt-BR.json` e `en.json`. Para outro idioma, crie um JSON com as
  mesmas chaves e registre-o em `Loc.Languages` — um teste garante que todos os idiomas têm as mesmas chaves.
- **Temas:** `src/SupernovaBench.App/Themes/Cosmic.xaml` e `Light.xaml` (só cores); estilos em `Controls.xaml`.
- **Dados do usuário:** `%AppData%\SupernovaBench` (`settings.json`, `history.json`, `games.json`, `covers\`, `supernova.log`).

---

## English summary

Supernova Bench is a Windows 10/11 all-in-one PC tool: CPU-Z/AIDA64-style hardware details, live sensors,
a Cinebench-style CPU path-tracing benchmark (multi-core, single-core, 10-minute sustained), a Direct3D 11 game-scene
GPU benchmark (average FPS and 1% low), a FurMark-style GPU stress test with a temperature cutoff, GPU-Z-style card
details (NVML/DXGI), an AIDA64-style cache and memory benchmark, a CrystalDiskMark-style disk benchmark,
CrystalDiskInfo-style SMART health (NVMe and SATA), a CPU stress test with calculation-error detection, an in-Windows
RAM stability test, a game library for Steam, Epic, GOG, Ubisoft, EA, Battle.net and Xbox (covers, playtime tracking and
per-game performance profiles), HTML/JSON reports and cross-PC comparison. The next phase adds a driver updater. The UI is available in Portuguese (default) and English, with a cosmic dark theme or a light theme.
Download the latest build from **Actions → Build → Artifacts**. Licensed under MIT; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
