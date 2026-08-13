# Protocolo e engenharia

Como o painel foi levantado, como os sensores sao lidos, e o que ficou medido
em vez de suposto. O [README](../README.md) cobre instalar e usar.

---

## Protocolo do painel

Levantado por engenharia reversa: captura USB do software original (USBPcap),
decodificação byte a byte e validação por escrita direta no dispositivo.

**Dispositivo:** `VID 0x1A2C` / `PID 0x4984`
O firmware se identifica como *"USB Gaming Keyboard"* — descritor genérico
reaproveitado do fabricante do microcontrolador. O canal real de dados é a
coleção HID *vendor-defined* com `UsagePage 0xFF01`.

**Transporte:** transferência de controle no EP0 — `SET_REPORT` da classe HID.

```
Setup: 21 09 07 03 01 00 40 00
       │  │  │  │  │     └── wLength = 64
       │  │  │  │  └──────── wIndex  = 1 (interface)
       │  │  └──┴─────────── wValue  = 0x0307 (tipo 3 = Feature, ReportID 7)
       │  └───────────────── bRequest = 0x09 (SET_REPORT)
       └──────────────────── bmRequestType = 0x21 (OUT | Class | Interface)
```

**Payload — 64 bytes:**

| Byte | Conteúdo |
|------|----------|
| `[0]` | `0x07` — ReportID |
| `[1]` | centena do painel 1 |
| `[2]` | dezena do painel 1 |
| `[3]` | unidade do painel 1 |
| `[4]` | flags — `bit0 (0x01)` = °F ; `bit4 (0x10)` = % |
| `[5]` | centena do painel 2 |
| `[6]` | dezena do painel 2 |
| `[7]` | unidade do painel 2 |
| `[8..63]` | `0x00` |

Os dígitos são enviados **separados, um por byte, em decimal direto** — não é BCD
compactado nem inteiro binário. Para exibir `73`, envia-se `0`, `7`, `3`.
Os códigos `0x0A`–`0x0F` **apagam** o dígito.

**Sem checksum, sem criptografia, sem número de sequência.**

O byte de dígito é um **índice de tabela no firmware**, não um mapa de
segmentos. A prova é `0x00`: se fosse bitmap acenderia nada, e acende o `0`.
Não há, portanto, como desenhar figuras ou animar segmentos avulsos — o painel
escreve os dez algarismos e mais nada. Para varrer o que resta do protocolo,
veja `tools\Probe.cs` em *Ferramentas*.

### Flags (`report[4]`)

Os dois bits são **independentes** — as quatro combinações são válidas:

| Valor | Painel 1 | Painel 2 |
|-------|----------|----------|
| `0x00` | °C | W |
| `0x01` | °F | W |
| `0x10` | °C | % |
| `0x11` | °F | % |

O bit apenas **acende o símbolo**; a conversão numérica é responsabilidade do
software. O software original usa a centena exclusivamente para Fahrenheit,
que ultrapassa 99 — mas a faixa `000–999` está integralmente disponível nos
dois painéis, validada por escrita.

### Watchdog

O firmware apaga o painel se parar de receber atualizações. É obrigatório
reenviar continuamente. O software original usa cadência de **~1105 ms**
(medida: desvio inferior a 1%). Este projeto envia a cada 1000 ms, preservando
folga para atrasos normais de coleta e escalonamento do Windows.

---

## Fontes de sensores

O aplicativo tem duas fontes e escolhe a melhor disponível no arranque.

### HWiNFO (preferida)

`engine\api-ms-win-core-sysinfo-825-64.dll` é a **biblioteca cliente do HWiNFO**
(HWiNFO32 Client Library 8.25, REALiX s.r.o.), distribuída pelo fabricante do
cooler com nome de API do Windows. É o mesmo motor que o software original usa
para ler temperatura — e a razão de ele funcionar onde a LibreHardwareMonitor
falha: seu driver é assinado WHQL pela Microsoft e **não** consta na lista de
drivers vulneráveis.

A biblioteca exporta **797 funções, nenhuma com nome** — só por ordinal. A
correspondência abaixo foi recuperada do `DeviceDriver.exe` original,
localizando os `GetProcAddress` e decodificando os *call sites*. Todas são
`cdecl`:

| Ordinal | Assinatura | Papel |
|---------|-----------|-------|
| `850` | `int Init(0xC0)` | inicializa; devolve 0 em caso de sucesso |
| `156` | `int GetCount()` | quantidade de grupos de sensores |
| `263` | `int (void)` | chamada uma vez por ciclo, após a contagem |
| `678` | `int (int i)` | prepara o grupo `i` |
| `952` | `int (int i, char* buf, int tam)` | nome do grupo `i` |
| `641` | `int (int classe, int i, int j, void* elem)` | leitura `j` do grupo `i`; `0` encerra a série |
| `398`, `613` | — | resolvidos e validados pelo original, não usados na leitura |

O elemento devolvido por `641` tem **464 bytes** (`0x1D0`):

| Offset | Campo |
|--------|-------|
| `+0x08` | valor (`double`) |
| `+0x10` | unidade, ASCII (`"°C"`, `"W"`, `"MHz"`, `"MB"`…) |
| `+0x30` | categoria de hardware (`10` sistema, `11` CPU, `12` placa-mãe, `13` GPU, `15` disco, `16` rede) |
| `+0x148` | rótulo da leitura |

O primeiro argumento de `641` é a **classe de leitura**: `1` temperatura,
`2` voltagem, `3` ventoinha, `4` corrente, `5` potência, `6` clock, `7` uso,
`8` outros. O software original só consulta a classe 1 — daí ele exibir
temperatura pelo HWiNFO e watts pela outra fonte.

O `Init` falha com código **1** sem elevação, porque a biblioteca precisa
registrar e subir seu driver.

> **A DLL não está neste repositório** — é software comercial de terceiros e
> não pode ser redistribuída (veja *Licença*).

A biblioteca acompanha o instalador e é carregada de `bin\engine\`. Ao compilar
do código, ponha `api-ms-win-core-sysinfo-825-64.dll` em `lib\` e o `build.ps1`
a copia para lá.

<img src="docs/sobre.png" width="480" alt="Aba Sobre, com o resumo das fontes de sensores">

### Por que não a memória compartilhada do HWiNFO

A alternativa óbvia à biblioteca comercial seria a **interface de memória
compartilhada** do HWiNFO: nada de DLL, o aplicativo lê um mapeamento de
memória publicado pelo HWiNFO em execução. Resolveria de uma vez o problema de
licença que impede publicar um instalador.

Foi verificada e **não serve para este caso**: na versão gratuita do HWiNFO a
interface é limitada a **12 horas de execução**, depois se desativa sozinha e
precisa ser remarcada à mão. Para um aplicativo de bandeja que fica ligado com a
máquina, isso é o mostrador morrer todo dia sem aviso. Sem o limite, só com a
licença Pro.

Ou seja, ela não elimina a dependência — troca uma dependência comercial por
outra, e ainda transfere para quem usa a tarefa de instalar o HWiNFO e manter a
opção ligada. Fica registrado para não ser reavaliada do zero.

### LibreHardwareMonitor (reserva)

Usada apenas quando o HWiNFO não está disponível — o que, com a biblioteca
acompanhando o instalador, não acontece numa instalação normal. Cobre GPU, uso
de CPU, memória, disco e rede, e **devolve zero** em temperatura, potência e
clock real do processador.

Não é aberta quando o HWiNFO responde: não haveria o que ganhar.

---

### Notas de implementação

- A leitura de sensores roda em **thread própria**: percorrer o hardware leva
  dezenas a centenas de ms e travaria a interface se fosse feita na thread de
  UI. Só a atualização do tooltip volta para a UI.
- A cadência é **compensada**: o laço desconta o tempo gasto no ciclo, mantendo
  1100 ms reais independentemente da carga da máquina.
- **Instância única** garantida por mutex — duas instâncias disputariam o painel.
- A biblioteca do HWiNFO não expõe consulta individual, então a enumeração
  visita grupo por grupo. **Preparar um grupo (ordinal `678`) custa ~2,9 ms; as
  169 leituras que vêm depois custam praticamente zero.** O preço do ciclo é o
  número de grupos, e nada mais. Por isso, com a janela fechada, o aplicativo lê
  **apenas os grupos dos sensores que estão no mostrador** — os do perfil ativo
  mais os de todos os perfis do rodízio. Medido nesta máquina, com 19 grupos e
  169 sensores: **55,7 ms por ciclo lendo tudo, 4,3 ms lendo dirigido.**
- O identificador do sensor guarda o **nome** do grupo, não o índice. O índice é
  cacheado, mas **nunca usado sem conferir o nome** depois de selecionar (custa
  0,001 ms): se não bater, o aplicativo refaz a varredura completa e reaprende.
  Sem essa conferência, um dispositivo aparecendo ou sumindo faria o mostrador
  ler o sensor errado em silêncio — o pior modo de falhar que este programa tem.
- `List()` e `Snapshot()` **completam sozinhos** se o último ciclo foi dirigido.
  A garantia mora neles e não em quem chama, porque esquecer seria silencioso: o
  seletor apareceria com dois sensores em vez de 138, e o defeito se pareceria
  com "sumiram sensores", longe da causa.
- Sensores por núcleo são **resumidos em médias** (clock, potência, tensão, uso),
  para não enterrar os sensores gerais. Desligável em *Mostrar todos os sensores*.
- A conversão para Fahrenheit só se aplica a sensores do tipo `Temperature`.
- A **unidade vem da fonte**, não do tipo do sensor: o HWiNFO devolve memória em
  `MB` e o rótulo genérico do tipo dizia `GB` sobre o número errado. Leituras em
  `MB` são convertidas para `GB` e exibidas com uma casa (`11.6 GB`).
- O seletor de unidade do painel 2 **acompanha a métrica**: escolher um sensor de
  potência acende `W`, um de uso acende `%`.
- Valores acima de 999 são limitados pelo hardware; o tooltip sinaliza com
  `[excede 999]`. Divisores por sensor permitem caber métricas maiores.
- **Início automático** por Tarefa Agendada com `/rl highest`: a chave `Run` do
  registro não serve para aplicativos elevados.
- `SessionEnding` encerra a thread antes de fechar as fontes.
- A **tela de carregamento vive em thread própria**, com laço de mensagens
  próprio. Pendurada na thread principal ela travava: durante a subida do driver
  o `LoadLibrary` segura o cadeado do carregador, o Windows marca a janela como
  travada e passa a engolir os cliques — não dava para fechá-la.
- As **barras de rolagem são desenhadas pelo aplicativo**. A nativa entra como um
  risco claro sobre o cartão escuro mesmo com `DarkMode_Explorer`, e não aceita
  espessura nem raio. Escondê-la tem um efeito colateral: o `ListBox` só rola com
  a roda enquanto a barra nativa está visível, então a roda também passou a ser
  tratada na mão, pelo `TopIndex`.
- A altura da lista é **acertada para um múltiplo da linha**; a sobra vira recheio
  do painel. Sem isso a última linha aparecia cortada ao meio, como se houvesse
  item escondido onde não havia.
- **Trocar de idioma reabre a janela** em vez de reetiquetá-la viva. Reetiquetar
  exigiria que cada controle guardasse a chave do seu texto e soubesse se
  retraduzir — dezenas de pontos, e o que escapasse ficaria no idioma antigo sem
  ninguém notar. Reconstruir não deixa canto por traduzir. As edições pendentes
  vão para o perfil antes, então nada se perde.
- Os nomes de categoria são **guardados em português e traduzidos na hora de
  desenhar**. São chave de agrupamento, não texto de tela; traduzi-los na origem
  quebraria a comparação que põe cada sensor sob o cabeçalho certo.
- A **busca de sensor casa os dois nomes** da categoria, então digitar "memory"
  acha o que a interface em inglês chama Memory e a configuração chama `Memória`.
- A thread de atualização **nunca lê a configuração viva**. Quem edita publica um
  clone; ela só lê a referência, o que é atômico. Antes, criar ou excluir perfil
  no momento errado lançava exceção no meio do ciclo, e o mostrador podia exibir
  um perfil pela metade.
- A posição no rodízio é **derivada do relógio**, não somada a cada volta. O ciclo
  dura 1100 ms mais o que a varredura levar, e um contador incrementado "quando
  der" acumularia esse resto até o rodízio de 20 s virar de 23.
- A ociosidade vem de `GetLastInputInfo` com **aritmética sem sinal**:
  `GetTickCount` dá a volta a cada 49,7 dias, e a subtração em `uint` continua
  certa na volta.

### Testes

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-tests.ps1
```

Sem framework de propósito: o projeto compila com o `csc.exe` que vem no Windows,
e uma dependência de teste custaria exatamente a propriedade que o torna fácil de
compilar. Os testes compilam **contra os fontes**, e não contra o executável,
para alcançar o que é interno sem abrir visibilidade só para poder testar.

Cobrem o que a interface não denuncia: a montagem do quadro de 64 bytes, o
preparo do valor para três dígitos, a formatação da leitura, a ida e volta da
configuração, o rodízio e a completude das traduções. Dois deles varrem por
reflexão em vez de listar à mão — **todo campo do perfil** (cópia, INI,
exportação) e **todo texto de interface nos dois idiomas** — porque o modo de
falhar é sempre acrescentar algo e esquecer de uma das pontas, e uma lista
escrita à mão esqueceria junto.

### Medição

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-bench.ps1
.\bin\Bench.exe
```

Mede alocação e tempo por ciclo com `AppDomain.MonitoringTotalAllocatedMemorySize`
— que conta bytes alocados de verdade, enquanto `GC.GetTotalMemory` só mostra o
que sobreviveu, e lixo de vida curta é exatamente o caso aqui. Precisa de
elevação, como o aplicativo: sem ela as fontes abrem pela metade e a medição
mede outra máquina.

Existe porque a alternativa era otimizar por leitura de código. Dá para contar as
alocações de um ciclo com o dedo na tela e chegar a um número grande; o que a
conta não diz é se esse número importa perto do que a varredura do hardware
custa sozinha.

Não importava. As 169 KB por ciclo dão **87 coletas de geração 0 por hora**, e
nenhuma de geração 1 ou 2 — reaproveitar objetos entre ciclos não ganharia nada.
O banco também descartou pular as sondas que encerram cada série: **0,14 ms de
56**. O que ele achou foi outra coisa, que só apareceu porque a medição foi feita
com privilégio administrativo e a fonte boa ativa — sem elevação o HWiNFO nem
abre, e o banco mede a fonte que o aplicativo não usa.

A seção *leitura dirigida* confere primeiro que o atalho não muda o resultado, e
só depois mede o ganho. O modo de falhar que interessa ali não é lentidão: é a
lista vir truncada sem ninguém perceber, porque os dois mostradores continuam
certos e o que some é o resto.

---

## Ferramentas

`tools\Probe.cs` — sonda interativa do protocolo, para varrer o que ainda não
foi mapeado: códigos de dígito acima de `0x0F`, os 56 bytes que o software
original sempre zera, outros ReportIDs e a cadência máxima que o firmware
aceita.

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\build-probe.ps1
.\bin\Probe.exe
```

Um laço de fundo reenvia o quadro atual a cada 400 ms — sem isso o watchdog
apaga o painel enquanto se olha para ele. **Não precisa de elevação:** falar HID
com o dispositivo não envolve driver.

| Comando | Efeito |
|---------|--------|
| `b <i> <hex>` | escreve um byte na posição `i` do quadro |
| `v <i>` | varre `00`–`FF` na posição `i`, passo a passo |
| `va <i> [ms]` | a mesma varredura, automática |
| `r <hex...>` | substitui o quadro inteiro |
| `hz <ms>` | muda a cadência de reenvio |
| `anim [ms]` | anima os dígitos, para medir o limite de atualização |
| `un` | percorre os dois nibbles de `report[4]`, um indicador de cada vez |
| `ids` | tenta outros ReportIDs |
| `q` | sai |

### Não há como apagar os indicadores de unidade

Um dos dois símbolos de cada par fica **sempre aceso**: `°C` ou `°F` em cima,
`%` ou `W` embaixo. O protocolo alterna dentro do par e não tem estado apagado.
Isso foi procurado a fundo e não existe — segue o que foi varrido, para que
ninguém precise refazer:

| Onde | Como | Resultado |
|------|------|-----------|
| Os seis bits sem mapa de `report[4]` | os 16 valores de cada nibble, com os dígitos fixos em `123`/`456` | só `bit0` e `bit4` fazem alguma coisa; os outros seis são inertes |
| Outros ReportIDs | `ids`, os 255 | só o `0x07` é aceito |
| Códigos de dígito de `0x10` a `0xFF` | sondagem nas posições `1` e `5` | nenhum apaga os símbolos |
| Os 56 bytes que o software original zera | sondagem em `8`, `9` e `10` | nada muda |

A explicação mais simples para o conjunto é que o firmware acende um dos dois
símbolos incondicionalmente, sem terceiro caso — e se os dois forem um par
complementar no mesmo pino, é impossível eletricamente, não só no protocolo.

Vale registrar a ausência com o mesmo cuidado de um achado: é a diferença entre
"ninguém tentou" e "foi tentado e não está aí", e só a segunda dispensa tentar
de novo.

**Consequência para quem usa o aplicativo:** ao pôr no mostrador uma métrica que
não é temperatura nem porcentagem — RPM, MHz, vazão — um símbolo errado vai
ficar aceso do lado do número, e a escolha possível é qual dos dois incomoda
menos. A única saída de verdade é física: uma fita opaca sobre o símbolo.

---


## Anexo: memória compartilhada do RTSS

Nada disto é do cooler — é a ponte que traz taxa de quadros e tempo de quadro
para o aplicativo. Fica registrado aqui pelo mesmo motivo do resto: foi aferido
contra a memória de uma máquina de verdade, e sem isso alguém refaz a conta.

Mapeamento `RTSSSharedMemoryV2`, aberto só para leitura, sem privilégio nenhum.
Conferido com o **RTSS 2.21** (`dwVersion` = `0x00020015`).

| Campo | Onde | Observado |
|-------|------|-----------|
| `dwSignature` | `+0` | `0x52545353` — em memória se lê **`SSTR`** |
| `dwVersion` | `+4` | `0x00020015` |
| `dwAppEntrySize` | `+8` | **12416** bytes nesta versão |
| `dwAppArrOffset` | `+12` | `0x00248FE0` |
| `dwAppArrSize` | `+16` | 256 posições |

Na entrada, os campos que interessam ficam todos no começo e sobreviveram às
versões: `dwProcessID` em `+0`, `szName[260]` em `+4`, `dwTime0` em `+268`,
`dwTime1` em `+272`, `dwFrames` em `+276` e `dwFrameTime` (µs) em `+280`.
FPS = `dwFrames × 1000 ÷ (dwTime1 − dwTime0)`.

**O tamanho da entrada sai do cabeçalho, nunca de constante nossa.** São 12416
bytes contra os 328 da estrutura documentada — um passo fixo leria o meio da
entrada seguinte e produziria números plausíveis e falsos.

> A assinatura é o literal multicaractere `'RTSS'` do C, que em little-endian
> cai na memória como `S`, `S`, `T`, `R`. Comparar os bytes na ordem em que se
> lê o nome derruba **toda** leitura, em silêncio — sem exceção e sem linha no
> registro, o aplicativo apenas diz "RTSS não encontrado" com o RTSS aberto na
> bandeja. O teste não pegou porque montava o mapeamento com a mesma ordem
> errada: um teste escrito a partir da suposição confirma a suposição. Quem
> desempatou foi despejar o cabeçalho de verdade, com o `RtssProbe`.

O diagnóstico está em `tools/RtssProbe.cs` (`tools/build-rtss-probe.ps1`).
Autônomo de propósito — não depende dos fontes do aplicativo, então pode ser
levado sozinho a outra máquina para separar as três causas que se parecem: RTSS
parado, jogo que ele não engancha, e memória invisível para quem lê.

---
