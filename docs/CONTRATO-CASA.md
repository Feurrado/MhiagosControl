# Contrato da Casa entre o servidor e os clientes

O servidor da casa (`servidor/`) é a fonte da Casa para o painel web, o app Android e o app do PC.
Este documento fixa o formato que os três leem. A versão vai no campo `contrato` de `/api/saude`
e de `/api/aparelhos`.

- **Versão atual: 1** (servidor 1.7.0, app do PC 2.32.0).
- Acrescentar um campo não muda a versão. Tirar um campo, ou mudar o tipo ou o sentido dele, sobe a versão.
- O app do PC só usa o servidor como fonte se `contrato` for o que ele conhece ou maior
  (`ServidorCasa.ContratoMinimo`). Abaixo disso ele lê a nuvem direto, como antes.
- A amostra `servidor/tests/contrato-aparelhos.json` é conferida pelos dois lados:
  `servidor/tests/test_contrato.py` e `tools/ContratoCasaTests.cs`.

## GET /api/aparelhos

Campos do topo:

- `contrato` (inteiro): versão deste formato.
- `agora` (inteiro, época em segundos): relógio do servidor.
- `logado` (booleano): o servidor tem a conta ConnectLife.
- `ultimaLeitura` (inteiro, época): última leitura da nuvem que deu certo. Zero se nenhuma.
- `erro` (texto ou nulo): falha da última leitura.
- `periodo` (inteiro, segundos): intervalo atual de leitura.
- `lista`: os aparelhos (abaixo).
- `agendamentos`, `agendamentosFalhos`: timers e retornos de cena.

Cada aparelho de `lista`:

- `puid`, `deviceId`, `apelido`, `tipo`, `tipoNome`, `modelo`, `codigoDoModelo`, `comodo` (textos).
- `online` (booleano).
- `estado`: dicionário de texto para texto, as propriedades como a nuvem manda (`t_power`, `t_temp`...).
- `info`, `diagnostico`: o que o painel web mostra já descrito. O app do PC não usa.

## POST /api/aparelhos/{puid}/comando

Corpo: `props` (texto para texto, até 12), `semBipe` (booleano ou nulo para usar a preferência),
`manual` (booleano; `true` cancela o retorno da cena no servidor). O app do PC manda `manual: false`,
porque ele mesmo cancela o retorno pelo id do agendamento.

Respostas: 200 feito, 400 propriedade recusada, 404 aparelho desconhecido, 409 servidor sem conta,
502 a nuvem falhou.

## Como o app do PC usa (2.32.0)

- Com o servidor configurado, a leitura e os comandos vão por ele. A nuvem direta é a reserva:
  entra quando o servidor não responde, está sem conta, com contrato antigo ou com leitura parada
  há mais de 5 minutos, e fica por 2 minutos antes de tentar o servidor de novo.
- A cada 6 horas uma leitura vai direto à nuvem, para o login do PC não vencer por falta de uso.
- `servidorfonte=0` no `casa.ini` desliga isso e volta ao comportamento anterior.
