# Guia do backlog da página Casa

Guia para continuar o Mhiagos Control a partir da branch `connectlife-casa`, implementando os 10 itens que o Thiago pediu em 26/10/2026. Escrito para um modelo que começa sem contexto nenhum, dentro do Claude Code, neste repositório.

## TL;DR

- Trabalhe na worktree `E:\Claude\mhiagos-casa`, branch `connectlife-casa`. Não faça push nem PR sem o ok do Thiago.
- Faça um item por vez. A cada item: compilar, rodar a suíte inteira, gerar capturas, commit local, gerar o instalador local e relatar ao Thiago.
- Ordem recomendada: 1, 4, 5, 2, 3, 6, 7, 8, 9, 10. Os itens 1, 4 e 5 arrumam a página Casa e liberam espaço; o 8, o 9 e o 10 dependem de a página Casa e o serviço estarem estáveis.
- Nunca mande comando ao ar-condicionado ou à lavadora reais sem autorização explícita do Thiago na conversa. Se ele autorizar, anote o estado antes e restaure no fim.
- O projeto compila com o `csc.exe` do .NET Framework 4 (C# 5). Nada de `$""`, `?.`, `nameof`, `=>` em membros, `out var` ou tuplas.

## Como usar este guia

1. Leia a seção "Contexto essencial" e a seção "Regras invioláveis" inteiras antes de mexer em qualquer coisa.
2. Escolha o próximo item na ordem recomendada, a não ser que o Thiago peça outra.
3. Antes de editar, abra e leia os arquivos citados no item. Os nomes de classes e métodos abaixo existiam no commit `8f81a0b`; confirme com `git log` e com uma busca no código se ainda são esses.
4. Quando o item tiver "a confirmar com o Thiago", pergunte antes de implementar essa parte, com uma recomendação.
5. Termine cada item com o checklist da última seção.

## Contexto essencial

### O que é o Mhiagos Control

Aplicativo de bandeja para Windows, em WinForms, que nasceu para mandar leituras do PC (CPU, GPU, memória) ao display do cooler e virou um painel geral: métricas, perfis por jogo, bateria dos periféricos e, agora, a casa (ar-condicionado e lava e seca Hisense pela nuvem ConnectLife). Repositório no GitHub: `Feurrado/MhiagosControl`.

- Versão nesta branch: 2.29.0, definida em `src\PageLayouts.cs` (`AppInfo.Versao`) e em `tools\Installer.cs` (`Versao`).
- A branch parte da 2.28.0, que só está no GitHub no PR #24 (`release-2.28.0` para `main`), ainda aberto. O `main` remoto está na 2.27.2. Por isso não se faz push desta branch antes do merge do #24 ou de um ok do Thiago.
- Commits da página Casa, do mais antigo ao mais novo: `ea1cd38`, `edc4ec5`, `37b967e`, `603ee90`, `8f81a0b`.

### Linguagem e build

- C# 5 compilado pelo `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, sem SDK e sem NuGet. Referências: System, System.Core, System.Drawing, System.Windows.Forms, System.Management e `lib\LibreHardwareMonitorLib.dll`. Não há biblioteca de JSON: use a classe `Json` de `src\ConnectLife.cs` (`Json.Ler`, `Json.Escrever`, `Json.Texto`).
- Não há Python na máquina para ferramentas. Edite arquivos com a ferramenta Edit.
- Os fontes ficam em CRLF e em UTF-8 com BOM. Glifos da fonte Segoe MDL2 vão sempre ESCAPADOS no código (`"\uE710"`), nunca o caractere colado: algumas ferramentas trocam o escape pelo caractere da área de uso privado, e o projeto já perdeu glifos assim. Depois de escrever um arquivo, confira se não sobrou caractere entre U+E000 e U+F8FF.
- Compilar o app: `powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1` (gera `bin\MhiagosControl.exe`).
- Rodar a suíte: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-tests.ps1`. Ela compila os fontes de `src` com os testes de `tools` e roda `bin\Tests.exe`. A última linha diz `N verificacoes, todas passaram` ou quantas falharam. Arquivo de teste novo precisa entrar na lista `$srcs` de `tools\build-tests.ps1`.
- Capturas de tela dos testes: defina `$env:MHIAGOS_VISUAL_DIR="$env:TEMP\casa-cap"` antes de rodar a suíte; o helper `CaptureEvolution(controle, nome)` grava PNG lá. Para entregar ao Thiago, copie para `%TEMP%\clprobe`.
- O teste "com o RTSS no ar, o leitor reconhece o cabecalho" depende do RivaTuner nesta máquina e às vezes falha também na 2.28.0 sem mudanças. Se falhar, confira rodando `E:\Claude\mc-release-228\bin\Tests.exe` antes de culpar o seu código.
- Instalador local: copie `E:\Claude\mhiagos-apple2\lib\api-ms-win-core-sysinfo-825-64.dll` para `lib\`, rode `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-installer.ps1`, apague a DLL de `lib\` e copie `dist\MhiagosControlSetup.exe` para `E:\Claude\instaladores\MhiagosControlSetup-2.29.0-casa-ar-local.exe`. Esse instalador contém a DLL comercial do HWiNFO: é só para a máquina do Thiago, nunca publicar.
- O app instalado roda elevado e o instalador pede administrador. Não tente elevar nem instalar sozinho: entregue o caminho ao Thiago e peça para ele fechar o Mhiagos pela bandeja e instalar.

### Convenções de interface

- Toda medida em pixel passa por `Ui.S(px)` (escala de DPI). Campos estáticos que usam `Ui.S` precisam estar depois de `Ui.Escala` na ordem de inicialização.
- Cores sempre lidas do `Ui` na hora de pintar: `Ui.Window`, `Ui.Surface`, `Ui.SurfaceAlt`, `Ui.Border`, `Ui.Text`, `Ui.Muted`, `Ui.Faint`, `Ui.Accent`, `Ui.AccentSoft`, `Ui.AccentText`, `Ui.Warn`, `Ui.Danger`, `Ui.Hover`. Misturas com `Ui.Misturar(a, b, t)`.
- Fontes: `Ui.FontBase`, `Ui.FontMed`, `Ui.FontSmall`, `Ui.FontSection`, `Ui.FontTitle`, `Ui.FontAppName`, `Ui.FontValue`, `Ui.FontGlyph9/10/11/18`. Fonte nova é campo estático com `new Font(Ui.Tipo.Exibicao, Ui.Pt(n))`, nunca criada a cada pintura.
- Controles do kit em `src\Ui.cs`: `Pagina` (página com rolagem própria; chame `Sincronizar()` no fim de cada arranjo), `Card`, `FlatBtn` (`Primary`, `Danger`, `Glyph`), `Toggle` (`Label`, `Checked`, `CheckedChanged`), `Segmented` (`SetItems`, `SelectedIndex`), `NumberBox` (`Minimum`, `Maximum`, `Value`, `ValueChanged`), `NavBar`/`NavItem`. Recolhíveis: `CollapsiblePanel` em `src\CollapsiblePanel.cs` (`Conteudo`, `AlturaAberta`, `Definir(aberto, animar)`, evento `AlturaMudou`), com estado lembrado por `DetalhesDaBateria.Aberto(chave)` em `src\BatteryPage.cs`. Animações: `Motion` e `Transicao` em `src\Motion.cs`.
- Controles da Casa já prontos e reaproveitáveis: `Mostrador` (dial), `Blocos` (fila de blocos com ícone), `BotaoRedondo`, `Secao` (bloco com título e botão de ação), `CampoDeTexto`, `AnelDoCiclo`, `EtapasDoCiclo`, `BotaoChave`, `IconesCasa` (desenhos de floco, gota, folha, ventoinha, sol, lua, chave de boca, energia).
- Textos em dois idiomas: propriedades em `public static partial class T` com `P("pt", "en")`. A suíte acusa texto igual nos dois idiomas, então use palavras diferentes (por exemplo "Normal" e "Standard").
- Testes de layout sempre a 880 e 1300 px de largura, em pt-BR e en-US, conferindo que nada se sobrepõe nem sai do cartão (helper `SemSobrepor` em `tools\CasaTests.cs`).

### Arquivos da página Casa

- `src\ConnectLife.cs`: `ConnectLifeNuvem` (OAuth do navegador, `TrocarCodigo`, renovação, `ListarAparelhos`, `Propriedades(tipo, modelo)`, `AutoDiagnostico(puid)`, `Definir(puid, props)`), `Aparelho` (estado, `Meta`, `ModeloLegivel`, `ArCondicionado`, `Lavadora`, `Int`, `Num`, `Tem`), `CofreConnectLife` (Gerenciador de Credenciais) e `Json`.
- `src\CasaServico.cs`: `ServicoCasa` (vive com a bandeja; lê a nuvem a cada 60 s, ou 30 s com a página aberta; `Enviar`, `Atualizar`, `Partes`, `SoSozinhas`, timer de desligar, evento `Mudou`, `AvisoWindows`), `PreferenciasCasa` (casa.ini, incluindo `Cenas`), `Alertas`, `Diagnostico`, `HistoricoDoAr` (casa-historico.txt, `PorDia` para horas e consumo estimado).
- `src\CasaPage.cs`: `PaginaCasa` (a página; puxa tudo do serviço em `PuxarDoServico`; arranjo em `Arranjar`), `CartaoDeLogin`, `CartaoDeAparelho` (base dos cartões: `Mostrar`, `Preencher`, `Arranjar`, `Mandar`, `MandarVarios`), `CartaoGenerico`, `BotaoRedondo`, `CampoDeTexto`.
- `src\CasaAr.cs`: `MetaPropriedade`, `TabelaDoAr` (nomes, ícones e cores de modo e ventilador), `Mostrador`, `Blocos`, `CartaoDoAr` (painel do ar com as seções Funções, Preferências, Cenas, Desligar automaticamente, editor e diagnóstico; métodos `Arranjar`, `ArranjarSecoes`, `Conteudo`, `AbrirEditor`, `AlternarDiagnostico`).
- `src\CasaCenas.cs`: `CenaCasa` (modelo de cena, `Comando(aparelho)`, `Padrao()`), `Secao`, `PainelDeCenas`, `EditorDeCena`, `PainelDoTimer`, `BotaoChave`.
- `src\CasaExtras.cs`: `CartaoHistoricoDoAr` (gráfico, horas por dia, consumo estimado, potência e tarifa, alertas).
- `src\CasaLavadora.cs`: `TabelaDaLavadora`, `CicloAtual`, `CiclosDaLavadora` (casa-ciclos.txt e avisos), `AnelDoCiclo`, `EtapasDoCiclo`, `CartaoDaLavadora`.
- Fora da Casa, mas citados: `src\SettingsForm.cs` (janela; `BuildPages`, `BuildPageVisaoGeral` perto da linha 1007, `ArranjarVisaoGeral` perto da linha 1228, `ArranjarPagina`), `src\TrayApp.cs` (`TrayContext`, `IniciarCasa`, `BuildMenu`, `MostrarWidget`, `Notificar`), `src\BatteryWidget.cs` (`ConfigDoWidget`, `PintorDoWidget`, `WidgetDeBateria`), `src\PeripheralStrip.cs`, `src\Peripherals.cs` (`Perifericos.Atual`, `Perifericos.Ocultos`), `src\Profiles.cs` (`Config`, config.ini).
- Testes: `tools\CasaTests.cs` (`RegressionCasa` chama todos os da Casa; dados de exemplo em `Exemplo()` e `Lavadora("secando" | "concluido" | "offline")`).
- Dados em `%LOCALAPPDATA%\MhiagosControl\`: `config.ini` (Config; tem `casasembipe` e `perifericosocultos`), `casa.ini` (PreferenciasCasa: alertas, potências, tarifa, timer, cenas), `casa-historico.txt` (amostras do ar, 35 dias), `casa-ciclos.txt` (ciclos da lavadora), `log.txt`.
- Referências da API em `docs\connectlife-ar-propriedades.json`, `docs\connectlife-ar-levantamento.json` e `docs\connectlife-lavadora-levantamento.json` (identificadores da conta redigidos).

## Regras invioláveis

1. Nenhum comando real ao ar-condicionado ou à lavadora sem autorização explícita do Thiago naquela conversa. Com autorização: leia e anote o estado completo antes, faça só o teste combinado, confira na leitura seguinte e restaure o estado anotado no fim, conferindo de novo. Se algo falhar, pare, restaure e relate.
2. Nos testes da suíte, a página nunca fala com a nuvem. O `ServicoCasa` só é iniciado pela bandeja (`TrayContext.IniciarCasa`); nos testes use `PaginaCasa.MostrarSemNuvem(lista)`, `ServicoCasa.Simular`, `PaginaCasa.IgnorarCofre = true` e aponte `PreferenciasCasa.Arquivo`, `HistoricoDoAr.Arquivo` e `CiclosDaLavadora.Arquivo` para uma pasta temporária. Lembre de `ServicoCasa.PararSimulacao()` entre cenários.
3. `t_sleep` (modo noturno) só pega no ar de janela quando vai num comando SOZINHO. `ServicoCasa.Partes` já separa o que está em `ServicoCasa.SoSozinhas`; todo comando novo deve passar por `ServicoCasa.Enviar`.
4. `t_beep` não é estado do aparelho: com "Comandos sem bipe" ligado, `t_beep=0` vai junto de cada comando, e se a nuvem recusar o comando é repetido sem ele. Isso também já está em `ServicoCasa.Enviar`.
5. Ar "Gil do Frio" (Hisense AW-12CR2RWGU00, tipo 008, modelo 301): modos 0 Ventilar, 2 Frio, 3 Desumidificar, 5 ECO; ventilador 2 baixa, 3 média, 4 alta; temperatura 16 a 30 °C (61 a 86 °F). Valores aceitos sempre vêm do `Aparelho.Meta` (get_property_list); não invente valor.
6. Lavadora "Josefina Roupa Limpa" (WD3S1114-LVT008-000, tipo 025, modelo 1wj110009v0t) informa `ApplicationPermissions = 1`, que pela própria lista do modelo é "só monitorar". Trate a lavadora como somente leitura até o Thiago ligar o modo remoto no painel e autorizar testes.
7. O refresh token da ConnectLife muda a cada renovação. O app já lê o cofre antes de renovar (`ConnectLifeNuvem.UsarCofre`). Qualquer programa de sonda fora do app deve assinar `TokenMudou` e gravar o novo token com `CofreConnectLife.Gravar`, senão o app instalado perde a sessão.
8. Não digite senha nem credencial em lugar nenhum. O login é feito pelo Thiago no navegador.
9. Não publique o instalador e não faça push, PR, merge ou release sem o ok do Thiago. O PR #24 está pendente.
10. Periféricos: o G325 e o PRO X estão escondidos por `Config.PerifericosOcultos` (padrão `GHub|G325,GHub|PRO X`), aplicado só pelo executável. Não desfaça isso.
11. Todo layout novo tem teste a 880 e 1300 px, em pt e en, sem sobreposição.

## Os 10 itens

### Item 1: Preferências e Funções numa aba separada

Objetivo: deixar o dashboard da página Casa só com o que se usa no dia a dia, levando as configurações para uma aba "Ajustes" dentro da própria página.

Onde mexer: `src\CasaPage.cs` (`PaginaCasa`: cabeçalho, `Arranjar`), `src\CasaAr.cs` (`CartaoDoAr`: seções `_secFuncoes`, `_secPref` e o grid em `ArranjarSecoes`), `src\CasaExtras.cs` (`CartaoHistoricoDoAr`: os campos de potência, tarifa e os alertas).

Passos:
1. Acrescente no cabeçalho da `PaginaCasa`, à esquerda de "Atualizar", um `Segmented` com "Painel" e "Ajustes" (textos novos em `T`).
2. Crie um controle `AjustesDaCasa` (arquivo novo `src\CasaAjustes.cs`) com seções no estilo `Secao`: "Ar-condicionado" (Comandos sem bipe, unidade °C/°F e os interruptores de função que hoje ficam em `_secFuncoes`), "Alertas no Windows" (os toggles de `CartaoHistoricoDoAr` e o limite de temperatura), "Estimativa de consumo" (potências e tarifa), "Cenas" (atalho para o editor) e "Conta" (o botão "Sair da conta").
3. Mova os controles mantendo o comportamento. O mais seguro é deixar a lógica onde está (`Mandar`, `PreferenciasCasa.Salvar`) e só trocar quem os hospeda; os toggles de função continuam chamando `Mandar(chave, valor)` do cartão do ar.
4. O modo noturno continua no painel do ar (o item 2 ganha um botão próprio). No painel do ar ficam: mostrador, liga/desliga, modo, ventilador, cenas e timer.
5. Lembre a aba escolhida com uma chave nova em casa.ini (`PreferenciasCasa`).

A confirmar com o Thiago: o nome da aba ("Ajustes" ou "Preferências") e se "Comandos sem bipe" fica no painel ou vai para Ajustes (recomendação: Ajustes).

Testes: as duas abas a 880 e 1300 px, pt e en, sem sobreposição; trocar de aba não perde estado; os toggles movidos continuam mandando o mesmo comando (com o serviço simulado, confira o `ComandoEventArgs`).

Pronto quando: o painel não mostra mais Preferências nem os campos de potência e tarifa, e tudo o que saiu está na aba Ajustes, funcionando.

Armadilhas: `CartaoDoAr.Preencher` atualiza os toggles a partir do estado; se eles mudarem de dono, continue atualizando-os. Não quebre `PaginaCasa.SemBipe` e `SemBipeMudou`, que a `SettingsForm` usa para gravar a config.

### Item 2: Botão de modo noturno

Objetivo: um botão de um clique para ligar e desligar o modo noturno, visível no painel do ar.

Onde mexer: `src\CasaAr.cs` (`CartaoDoAr`), aproveitando `BotaoRedondo` e o desenho `IconesCasa.Lua`.

Passos:
1. Crie um botão redondo com a lua ao lado do liga/desliga (ou um botão de pílula "Noturno" na linha do liga/desliga), aceso quando `t_sleep` for 1.
2. Clique: `Mandar("t_sleep", aceso ? 0 : 1)`. `ServicoCasa.Enviar` já manda o `t_sleep` sozinho.
3. Mostre só se `CenaCasa.Aceita(A, "t_sleep", 1)`; desabilite com o ar desligado ou desconectado.
4. Se o item 1 já estiver pronto, tire o toggle "Modo noturno" das Funções para não haver dois controles iguais.

Testes: botão visível e aceso conforme o estado do exemplo; sem sobreposição a 880 e 1300 px; o comando gerado tem só `t_sleep`.

Pronto quando: o botão liga e desliga o noturno, e o estado volta certo na leitura seguinte (validar no ar real só com autorização).

Armadilhas: o ar pode levar uns segundos para refletir; a prévia otimista de `Mandar` já cobre isso. Não envie `t_sleep` junto de outras propriedades.

### Item 3: Cena por X minutos com retorno ao padrão

Objetivo: rodar uma cena por um tempo e depois voltar sozinho ao padrão 24 °C, Frio (`t_work_mode` 2), ventilador baixo (`t_fan_speed` 2).

Onde mexer: `src\CasaCenas.cs` (`CenaCasa`, `EditorDeCena`, `PainelDeCenas`), `src\CasaServico.cs` (`ServicoCasa`, `PreferenciasCasa`), seguindo o padrão do timer de desligar (`DefinirTimer`, `CancelarTimer`, `ArmarTimerDesligar`, `ConferirTimer`).

Passos:
1. Em `CenaCasa`, acrescente `Duracao` em minutos (0 = sem retorno) e inclua no `Linha()`/`DeLinha()` como 8º campo, aceitando linhas antigas de 7 campos.
2. No `EditorDeCena`, um campo "Voltar ao padrão depois de" com opções Nunca, 15 min, 30 min, 1 h, 2 h (um `Segmented` ou `Blocos`).
3. Em `PreferenciasCasa`, guarde `RetornoEmUtc` e `RetornoPuid`, e o padrão de retorno (`RetornoModo` 2, `RetornoTemp` 24, `RetornoVento` 2), com chaves novas em casa.ini.
4. No `ServicoCasa`, crie `AgendarRetorno(puid, minutos)`, `CancelarRetorno()` e um timer como o de desligar. Ao disparar, mande `t_power` 1, `t_work_mode` 2, `t_temp` 24 (convertido para °F se o aparelho estiver em °F), `t_fan_speed` 2 e `t_sleep` 0, por `Enviar` (o `t_sleep` vai separado sozinho), e avise com `AvisoWindows`.
5. Ao rodar uma cena com `Duracao` maior que 0, agende o retorno. Rodar outra cena, ligar o timer de desligar ou mexer manualmente no ar cancela o retorno pendente (a confirmar com o Thiago; recomendação: cancelar ao mexer manualmente).
6. Mostre a contagem regressiva no cartão, na seção do timer, com "Cancelar".
7. Ao reabrir o app, se o retorno venceu há mais de 15 minutos, descarte (mesma regra do timer de desligar).

A confirmar com o Thiago: se o padrão de retorno deve ser editável na aba Ajustes, e se mexer manualmente cancela o retorno.

Testes: gravação e leitura da cena com `Duracao` e de linha antiga; retorno persistido e cancelado; comando de retorno com os valores do padrão e `t_sleep` separado (`ServicoCasa.Partes`); layout do editor com o campo novo.

Pronto quando: a cena roda, a contagem aparece, o ar volta ao padrão no horário e o retorno sobrevive a reabrir o app.

Armadilhas: só funciona com o PC ligado e o Mhiagos rodando; deixe isso escrito no cartão. Não duplique a lógica do timer de desligar: extraia o que for comum.

### Item 4: Consumo do ar em seção recolhível

Objetivo: o cartão "Histórico e consumo" do ar abre e fecha, lembrando o estado.

Onde mexer: `src\CasaExtras.cs` (`CartaoHistoricoDoAr`), `src\CasaPage.cs` (`PaginaCasa.Arranjar`), `src\CollapsiblePanel.cs`.

Passos:
1. Envolva o conteúdo do cartão num `CollapsiblePanel`, com um cabeçalho clicável (título e seta) que fica sempre visível, no estilo do botão do widget da página Baterias (`BotaoDoWidget` e `AlternarPainelDoWidget` em `src\SettingsForm.cs`).
2. Guarde aberto ou fechado com `DetalhesDaBateria.Aberto` e a gravação correspondente, com uma chave própria (por exemplo `casa-consumo-ar`), ou em casa.ini.
3. Assine `AlturaMudou` para chamar `PaginaCasa.Arranjar()` durante a animação.
4. Com o item 1 pronto, os campos de potência e tarifa já estarão em Ajustes; aqui fica só a leitura (gráfico, barras e números).

Testes: aberto e fechado a 880 e 1300 px, pt e en; nos testes, `Ui.AnimacoesForcadas = false` e avance quadros como nos testes existentes de `CollapsiblePanel`.

Pronto quando: o cartão começa fechado (a confirmar) e abre e fecha suave, sem pular os cartões de baixo.

Armadilhas: `Pagina.Sincronizar()` não é idempotente; chame uma vez por arranjo. O `PaginaCasa` tem método próprio `PuxarDoServico`: não crie outro método chamado `Sincronizar` na página, porque ele esconde o da `Pagina` e causa recursão infinita (já aconteceu).

### Item 5: Consumo da lavadora em seção recolhível

Objetivo: água, energia e o histórico de ciclos da lavadora numa seção que abre e fecha.

Onde mexer: `src\CasaLavadora.cs` (`CartaoDaLavadora`: parâmetros, `_lbCiclos`, `_ciclos`, `Arranjar`).

Passos:
1. Tire "Água (ciclo)" e "Energia (ciclo)" da grade "Este ciclo" e leve, junto com "Últimos ciclos", para uma seção "Consumo e histórico" recolhível dentro do cartão (mesmo padrão do item 4).
2. Na seção, mostre também totais do mês somando `CiclosDaLavadora.Ultimos` (água e energia por mês). A API não dá o total mensal; é soma dos ciclos registrados pelo Mhiagos, rotulada como tal.
3. Lembre aberto ou fechado com uma chave própria.

A confirmar com o Thiago: os valores `Water_consumption_*` e `Electricit_consumption_*` parecem do ciclo, mas não está provado. Observe um ciclo do começo ao fim (no log ou no casa-ciclos.txt) antes de afirmar.

Testes: seção aberta e fechada nos três estados de exemplo (secando, concluído, offline), 880 e 1300 px, pt e en.

Pronto quando: o cartão da lavadora fica com o ciclo atual em destaque e o consumo e o histórico atrás de um clique.

Armadilhas: `CartaoDeAparelho.AvisarAltura()` é o que pede novo arranjo à página; chame-o quando a seção mudar de altura.

### Item 6: Nome dos programas da lavadora

Objetivo: mostrar o nome do programa selecionado (hoje aparece "Programa 7") em pt e en.

Onde mexer: `src\CasaLavadora.cs` (`TabelaDaLavadora.Programa(id)`), testes em `tools\CasaTests.cs`.

Fontes a pesquisar, nesta ordem:
1. Dicionário do `oyvindwe/connectlife-ha` (`custom_components/connectlife/data_dictionaries/`). Não há arquivo para o modelo `1wj110009v0t`, mas há vários `025-1wj...` (por exemplo `025-1wj105091v0t.yaml`, `025-1wj120495v0t.yaml`); veja se algum lista os valores de `Selected_program_ID`. O `025.yaml` genérico não lista.
2. A API: `get_property_list` traz `Selected_program_ID` sem nomes. Tente, só leitura, `query_system_data` no gateway do app (`clife-eu-gateway.hijuconn.com`), que para lava-louças devolve a lista de programas. Na juapi-3rd ele devolveu `data: null`. O gateway do app usa outra assinatura (RSA, veja `connectlife/api.py` da biblioteca Python em `E:\Claude\connectlife-win\.venv`); implementar isso só vale se trouxer os nomes.
3. App ConnectLife para Android: o APK costuma ter os nomes dos programas por tipo e modelo nos recursos ou em JSON baixado. Só baixe com autorização do Thiago (é download de arquivo de terceiros), em pasta isolada, sem executar nada.
4. Por último, peça ao Thiago: para cada programa, o número que o Mhiagos mostra e o nome no painel da máquina. Isso também calibra o que vier das outras fontes.

Como mapear:
1. Crie em `TabelaDaLavadora` um dicionário isolado por modelo (chave `"025-1wj110009v0t"`), com `id` para o par pt/en; os nomes ficam em `T` ou numa função com `P(...)`.
2. `Programa(id)` passa a receber o aparelho (ou tipo e modelo) e devolve o nome; sem entrada, continua "Programa N".
3. Registre a fonte de cada nome num comentário (ex.: "confirmado pelo Thiago em DD/MM").

Testes: o id conhecido vira o nome nos dois idiomas; id desconhecido continua pelo número.

Pronto quando: o programa em uso aparece com o mesmo nome do painel da máquina.

Armadilhas: não invente nomes. Programa errado é pior que "Programa 7".

### Item 7: Botão de autodiagnóstico da lavadora

Objetivo: um botão de diagnóstico na lavadora como o do ar (a chave de boca).

O que existe:
- `basic/self_check/info` (`ConnectLifeNuvem.AutoDiagnostico(puid)`) funciona para o ar e devolve a lista oficial de falhas com nomes e orientações. Para a lavadora, a resposta está em `docs\connectlife-lavadora-levantamento.json` (campo `selfcheck`): 30 entradas, todas com `statusKey = error_code` e `statusValue` igual ao código (1, 3, 4, 5, 17...), com `guide` em inglês (por exemplo, o código 1 manda conferir a entrada de água e o 3 limpar a bomba de drenagem) e `fault` vazio em várias. Ou seja, é um dicionário de código de erro para orientação, ótimo para explicar o `error_code` quando ele aparecer.
- A lavadora publica `error_code` e `machine_status = 4` (erro), já usados por `CicloAtual`.
- Não há comando de "rodar autoteste" conhecido, e a lavadora está em "só monitorar". O botão é de leitura: mostra a lista oficial, o estado agora, o código de erro e a última leitura.

Passos:
1. Generalize `Diagnostico` (em `src\CasaServico.cs`) para qualquer aparelho: hoje `Diagnostico.Atualizar` só é chamado para o ar em `ServicoCasa.LerAgora`; chame também para a lavadora (uma consulta por hora).
2. Em `CartaoDaLavadora`, ponha um `BotaoChave` no cabeçalho (mesma posição do ar: à esquerda do selo) e uma seção que mostra `Diagnostico.Texto(A)`; se a lista vier vazia, mostre o `error_code` e o `machine_status`.
3. Como a lista da lavadora usa `error_code` e não `f_e_*`, ajuste `Diagnostico.Texto` e `Diagnostico.TemFalha` para ler os pares `statusKey`/`statusValue` da lista oficial: falha ativa é quando `Estado[statusKey] == statusValue`. Para a lavadora, mostre a orientação do código atual (traduza as frases conhecidas em `T.CasaOrientacao`, como já é feito com "after_sales" no ar) e, sem erro, "Nenhum erro" com a lista recolhida.

A confirmar com o Thiago: se ele quer o botão mesmo sem comando de autoteste (só leitura).

Testes: interpretação da resposta gravada da lavadora; ponto vermelho com `error_code` diferente de 0; layout.

Pronto quando: a chave aparece na lavadora e mostra o estado real das falhas.

Armadilhas: não chame endpoint de escrita. Não confunda `error_code = 0` com falta de dado.

### Item 8: Status e controle fácil na Visão geral

Objetivo: ver e controlar o básico da casa sem sair da Visão geral.

Onde mexer: `src\SettingsForm.cs` (`BuildPageVisaoGeral`, `ArranjarVisaoGeral`), um controle novo `src\CasaResumo.cs`, `ServicoCasa` como fonte de dados.

Passos:
1. Crie `ResumoDaCasa : Card` (ou uma faixa como a `PeripheralStrip`) com um bloco por aparelho:
   - Ar: nome, ambiente, alvo com botões menos e mais, liga/desliga, modo atual com ícone.
   - Lavadora: estado (fase), tempo restante e hora de término, mini anel de progresso.
2. Assine `ServicoCasa.Mudou` e marshale para a interface como a `PaginaCasa` faz (`NaInterface`); desassine no `Dispose`.
3. Comandos pelo mesmo caminho da página (`ServicoCasa.Enviar`), com a prévia otimista.
4. Sem login, mostre um convite para entrar na página Casa; com aparelho desconectado, o estado em cinza com a última leitura.
5. Encaixe na grade de `ArranjarVisaoGeral` sem empurrar o cooler para fora; respeite a largura máxima (`LarguraMaxDaPagina`).

A confirmar com o Thiago: onde fica na Visão geral (recomendação: abaixo dos periféricos, acima das especificações).

Testes: Visão geral a 880 e 1300 px com o resumo, logado e sem login, ar ligado e desligado, lavadora rodando e offline; nenhum teste de Visão geral existente pode quebrar (há vários em `tools\RevisaoVisualTests.cs` e `tools\PageTests.cs`).

Pronto quando: dá para ligar o ar, mudar a temperatura e ver quanto falta da lavadora sem trocar de página.

Armadilhas: a Visão geral reparte a altura de um jeito próprio (`ArranjarVisaoGeral`, `hIdeal`); leia o método inteiro antes. Os testes de Visão geral contam blocos por posição.

### Item 9: Widget do ar e da lavadora

Objetivo: um widget de área de trabalho, como o de bateria, com controle rápido do ar e o tempo restante da lavadora.

Onde mexer: `src\BatteryWidget.cs` (`ConfigDoWidget`, `PintorDoWidget`, `Quadro`, `WidgetDeBateria`), `src\TrayApp.cs` (`MostrarWidget`, menu da bandeja em `BuildMenu`), página Baterias (painel do widget em `MontarCartaoDoWidget`).

Passos:
1. Decida com o Thiago entre (a) acrescentar blocos da casa no widget de bateria existente, como itens a mais na lista, ou (b) um segundo widget só da casa. Recomendação: (b), uma `WidgetDaCasa : Form` que reaproveita `PintorDoWidget` (vidro, cores, fontes) e `ConfigDoWidget` como modelo de configuração (posição, travar, sempre na frente, vidro e cor), num arquivo próprio `widget-casa.ini`.
2. Conteúdo: ar (ambiente, alvo, menos e mais, liga/desliga) e lavadora (fase, tempo restante, hora de término, anel). Cliques no widget mandam pelo `ServicoCasa.Enviar`.
3. Item no menu da bandeja para mostrar e esconder; o clique no nome abre a página Casa (a janela já abre página pela chave: `SettingsForm.MostrarPagina("casa")`, e a bandeja guarda o pedido em `TrayContext._paginaPedida`).
4. Atualiza com `ServicoCasa.Mudou`; a cada segundo só a contagem regressiva.

A confirmar com o Thiago: opção (a) ou (b), e se o widget tem controle ou só leitura.

Testes: pintura do widget com os exemplos (ligado, desligado, lavadora rodando e offline) em escala 1, 1,25 e 1,5; configuração gravada e lida.

Pronto quando: o widget mostra o estado ao vivo e os botões funcionam (validação no ar real só com autorização).

Armadilhas: o widget de bateria tem regras de foco, menu nativo e camadas; leia `src\BatteryWidget.cs` inteiro antes. O menu da bandeja usa `ContextMenu` nativo de propósito.

### Item 10: Nova Visão geral como controlador geral

Objetivo: reestilizar a Visão geral para refletir o que o app virou (PC, periféricos, casa e IoT), tirando o foco do display do cooler sem perder nada do que existe.

Proposta concreta (validar com o Thiago antes de codar, de preferência com uma captura simulada de cada versão):
1. Faixa de status no topo, com um "chip" por área: PC (CPU e GPU com temperatura e uso), Periféricos (bateria do X11), Casa (ar ligado ou desligado e ambiente; lavadora com tempo restante), Painel do cooler (conectado ou não). Clique no chip leva à página da área.
2. Grade de cartões abaixo, em duas colunas na janela larga e uma na estreita:
   - "Computador": os mostradores principais de hoje (CPU, GPU, memória), menores.
   - "Casa": o `ResumoDaCasa` do item 8.
   - "Periféricos": a `PeripheralStrip` atual.
   - "Display do cooler": a foto do cooler e o que ele mostra agora, num cartão menor e recolhível, em vez de dominar a página.
   - "Especificações": a ficha atual, recolhível.
3. Ordem e visibilidade dos cartões configuráveis (o app já guarda ordem de páginas em `Config.NavOrder`; use a mesma ideia com uma chave nova).
4. Mantenha todos os dados e ações de hoje; só muda a hierarquia e o tamanho.
5. Implemente atrás de uma opção (por exemplo `Config.VisaoGeralNova`), ligada por padrão só quando o Thiago aprovar, para comparar antes e depois.

Onde mexer: `src\SettingsForm.cs` (`BuildPageVisaoGeral`, `ArranjarVisaoGeral` e os campos `_cardCooler`, `_cardPerif` e os mostradores), novos controles em arquivo próprio, `src\Profiles.cs` para as opções novas.

Testes: todos os testes atuais de Visão geral continuam passando (ajuste só os que medem a hierarquia antiga, explicando no commit); novos testes de grade a 880 e 1300 px, pt e en; captura em escala 1,5 (`Tests.exe --captura-escala 1.5 pasta`).

Pronto quando: o Thiago aprova as capturas e a Visão geral nova mostra PC, periféricos e casa com o mesmo peso.

Armadilhas: este é o item mais arriscado. Faça em etapas pequenas com commits separados (faixa de status, depois grade, depois cooler recolhível). A Visão geral tem muitas regras de centragem e altura documentadas nos comentários de `ArranjarVisaoGeral` e nas memórias do projeto (PRs #20, #21 e #23); leia antes.

## Ideias extras levantadas

- Lembrete se a roupa ficar parada na máquina depois do fim do ciclo.
- Tempo médio por programa a partir de `CiclosDaLavadora`.
- "Programar para terminar às X h" na lavadora (`DelayEndTime`, `DelayEndTime_Minute`), só depois de confirmar o modo remoto.
- Automações com o estado do PC: ligar o ar ao abrir um jogo (perfil por jogo) ou ECO depois de N minutos de ociosidade.

## Checklist de cada entrega

1. `git status` e `git log --oneline -5` antes de começar, para confirmar a base.
2. Compilar: `build.ps1` sem erro.
3. Suíte completa: `tools\build-tests.ps1` com todas as verificações passando (ou só a do RTSS, comprovada como da máquina).
4. Capturas com `MHIAGOS_VISUAL_DIR` e cópia das relevantes para `%TEMP%\clprobe`; olhe cada uma antes de entregar.
5. Normalizar os arquivos tocados para CRLF e UTF-8 com BOM; conferir que não sobrou glifo não escapado.
6. Commit local com mensagem descritiva em português, terminando com as linhas de coautoria pedidas na sessão. Confira com `git diff --cached --name-only` que não entra `bin\`, `dist\`, DLL do HWiNFO, token nem identificador da conta.
7. Gerar o instalador local (com a DLL copiada e apagada depois) em `E:\Claude\instaladores\`.
8. Relatar ao Thiago: o que mudou, o que foi testado e como, o que ficou de fora, hash do commit, caminho do instalador e das capturas, e o que ele precisa fazer (instalar, responder alguma pergunta, autorizar algum teste).
