# Mhiagos Casa (servidor)

Backend da página Casa do Mhiagos Control para rodar 24 h num servidor da rede
local. Lê a nuvem ConnectLife a cada 60 s com login próprio, guarda o histórico
do ar e os ciclos da lavadora em SQLite, roda os agendamentos (timer de desligar,
retorno das cenas) e serve uma página de controle que funciona no celular.

O Mhiagos Control continua falando direto com a nuvem. Com o servidor
configurado em Casa > Ajustes > Servidor da casa, ele troca o histórico com o
servidor ao abrir e a cada minuto e entrega os timers para o servidor
disparar com o PC desligado.

## Instalar

```sh
mkdir -p /opt/mhiagos-casa && cd /opt/mhiagos-casa
# copie Dockerfile, docker-compose.yml, requirements.txt e app/ para cá
(umask 077; echo "MHIAGOS_CHAVE=$(openssl rand -hex 24)" > .env)
docker compose up -d --build
```

Depois abra `http://<servidor>:8765`, cole a chave do `.env`, clique em
"Abrir o login", entre com a conta ConnectLife e cole o endereço final do
my.home-assistant.io. Os dados ficam no volume `mhiagos-casa_dados`.

## API

Todas as rotas pedem `Authorization: Bearer <MHIAGOS_CHAVE>`, menos `/api/saude`.

| Rota | O que faz |
| --- | --- |
| `GET /api/saude` | versão, se tem conta, última leitura |
| `GET /api/aparelhos` | estado de cada aparelho (e o que o modelo do ar declara) |
| `POST /api/aparelhos/ler` | lê a nuvem agora |
| `POST /api/aparelhos/{puid}/comando` | `{"props": {"t_power": "1"}, "semBipe": true}` |
| `GET /api/aparelhos/{puid}/diagnostico` | autodiagnóstico oficial |
| `GET /api/historico/ar?desde=<epoch>` | amostras do ar (uma por leitura, no máximo a cada 50 s) |
| `GET /api/historico/ciclos?desde=<epoch>` | ciclos terminados depois de `desde` e o que está em curso |
| `POST /api/historico/importar` | o PC manda o que leu com o servidor parado; repetido não duplica |
| `GET/POST/DELETE /api/agendamentos` | comandos com hora marcada; vencidos há mais de 15 min são descartados |
| `GET /api/eventos?desde=<epoch>` | início e fim de ciclo, erros, comandos |
| `GET /api/mudancas?desde=<versao>` | espera longa: responde quando algo muda (ou em 25 s) com a versão nova |
| `GET /api/resumo-mes` | custo estimado do mês, do anterior e do anterior até o mesmo dia |
| `GET/POST/DELETE /api/conta`, `/api/conta/login`, `/api/conta/codigo` | login ConnectLife do servidor |

Variáveis: `MHIAGOS_PERIODO` (s entre leituras, 60), `MHIAGOS_DIAS_AR` (dias de
histórico do ar, 400).

## No celular como app

Abra o painel no navegador do celular e use "Adicionar à tela inicial". O ícone
é o do Mhiagos Control. No iPhone abre em tela cheia. No Android o Chrome só
instala como app de verdade (tela cheia, sem barra de endereço) em HTTPS: pelo
http da rede local ele vira um atalho que abre no navegador.

## Segurança

- A chave do `.env` é a única proteção da API: o painel só escuta na rede local
  (`192.168.1.100:8765`). Não abra essa porta no roteador.
- Troque a senha do root se ela já passou por algum chat ou arquivo: `passwd`
  no servidor. O acesso de manutenção usa chave SSH.
- Para acessar de fora de casa, prefira uma VPN (Tailscale ou WireGuard) a
  expor a porta. Se um dia expor, ponha um proxy com HTTPS na frente (Caddy ou
  o Nginx Proxy Manager) e continue exigindo a chave.
