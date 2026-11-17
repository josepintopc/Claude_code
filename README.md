# Supernova Bench

**Diagnóstico, benchmarks e ferramentas para o seu PC em um só lugar.** / *Diagnostics, benchmarks and tools for your PC in one place.*

Supernova Bench é um app para Windows 10/11 que junta, num só programa, o que normalmente exige vários:
informações de hardware no estilo **CPU-Z / GPU-Z / AIDA64**, benchmark de CPU no estilo **Cinebench**,
sensores em tempo real e, nas próximas fases, stress de GPU no estilo **FurMark**, testes de **RAM e SSD**,
**launcher de jogos** e **atualizador de drivers**. Interface em **português (padrão) e inglês**, tema **escuro cósmico** ou claro.

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

### Próximas fases

| Fase | Conteúdo |
|---|---|
| 2 — GPU | Página no estilo GPU-Z (DXGI/NVML: clocks, PCIe, Resizable BAR, VBIOS), stress de GPU no estilo FurMark (Direct3D 11) e benchmark de cena de jogo. |
| 3 — RAM e SSD | Latência e largura de banda da RAM, teste de estabilidade da RAM, benchmark de disco estilo CrystalDiskMark (seq. 1M, 4K QD1, 4K QD32), saúde SMART e stress de CPU com limite de temperatura. |
| 4 — Jogos | Biblioteca com jogos da Steam, Epic, GOG, Ubisoft, EA, Battle.net e Xbox; capas, perfis de desempenho por jogo e tempo de jogo. |
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
supernova-cli info
```

## Estrutura

```text
src/
  SupernovaBench.Core/      motores de benchmark, pontuação, histórico, relatórios, traduções (multiplataforma)
  SupernovaBench.Hardware/  WMI, registro e LibreHardwareMonitor (Windows)
  SupernovaBench.App/       interface WPF (MVVM): Views, ViewModels, Themes, Localization
  SupernovaBench.Cli/       benchmarks pela linha de comando
tests/SupernovaBench.Core.Tests/
```

- **Traduções:** `src/SupernovaBench.Core/Localization/Strings/pt-BR.json` e `en.json`. Para outro idioma, crie um JSON com as
  mesmas chaves e registre-o em `Loc.Languages` — um teste garante que todos os idiomas têm as mesmas chaves.
- **Temas:** `src/SupernovaBench.App/Themes/Cosmic.xaml` e `Light.xaml` (só cores); estilos em `Controls.xaml`.
- **Dados do usuário:** `%AppData%\SupernovaBench` (`settings.json`, `history.json`, `supernova.log`).

---

## English summary

Supernova Bench is a Windows 10/11 all-in-one PC tool: CPU-Z/AIDA64-style hardware details, live sensors,
a Cinebench-style CPU path-tracing benchmark (multi-core, single-core, 10-minute sustained), HTML/JSON reports
and cross-PC comparison. Upcoming phases add FurMark-style GPU stress, RAM/SSD benchmarks, a game launcher and a
driver updater. The UI is available in Portuguese (default) and English, with a cosmic dark theme or a light theme.
Download the latest build from **Actions → Build → Artifacts**. Licensed under MIT; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
