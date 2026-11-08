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
# copie o conteúdo de servidor/ para cá (Dockerfile, docker-compose.yml, requirements.lock, app/...)
(umask 077; echo "MHIAGOS_CHAVE=$(openssl rand -hex 24)" > .env)
docker compose up -d --build
```

O serviço roda sem root (uid 10001), com o sistema de arquivos só de leitura
(grava só em `/data`), sem capacidades extras do kernel, com limite de memória e
de processos e com o log limitado a 3 arquivos de 5 MB. O serviço `permissoes`
do compose acerta o dono do volume a cada subida (quem vem da 1.2 tinha os
arquivos como root) e termina.

## Atualizar

Copie a pasta `servidor/` de novo para `/opt/mhiagos-casa` (sem apagar o `.env`)
e rode `docker compose up -d --build`. O banco ganha sozinho o que a versão nova
precisar.

As dependências ficam travadas com hash em `requirements.lock`. Para subir de
versão, edite `requirements.txt` e gere o lock de novo, conferindo os testes:

```sh
pip-compile --generate-hashes --allow-unsafe --strip-extras --output-file requirements.lock requirements.txt
```

## Backup

`backup.sh` copia o `casa.db` para `/opt/mhiagos-casa/backups` com a API de backup
do SQLite (consistente com o serviço gravando), confere a cópia e guarda as 14
últimas. No cron do root:

```
30 3 * * * /opt/mhiagos-casa/backup.sh >> /var/log/mhiagos-casa-backup.log 2>&1
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
| `GET/POST/DELETE /api/agendamentos` | comandos com hora marcada; se a nuvem falha, tenta de novo (60, 120, 180 s) até 15 min depois da hora; vencidos há mais de 15 min sem tentativa são descartados |
| `GET /api/agendamentos/{id}/resultado` | `pendente`, `feito`, `falhou`, `descartado`, `cancelado` ou `desconhecido` (o PC pergunta antes de dar o timer como feito) |
| `GET /api/eventos?desde=<epoch>` | início e fim de ciclo, erros, comandos |
| `GET /api/mudancas?desde=<versao>` | espera longa: responde quando algo muda (ou em 25 s) com a versão nova |
| `GET /api/resumo-mes` | custo estimado do mês, do anterior e do anterior até o mesmo dia |
| `GET/POST/DELETE /api/conta`, `/api/conta/login`, `/api/conta/codigo` | login ConnectLife do servidor |

Variáveis: `MHIAGOS_PERIODO` (s entre leituras, 60), `MHIAGOS_DIAS_AR` (dias de
histórico do ar, 400).

## Testes

De dentro de `servidor/`, com as dependências do `requirements.txt`:

```sh
python -m unittest discover -s tests -v
```

A ConnectLife é simulada; a CI roda os mesmos testes a cada PR.

## No celular como app

Abra o painel no navegador do celular e use "Adicionar à tela inicial". O ícone
é o do Mhiagos Control. No iPhone abre em tela cheia. No Android o Chrome só
instala como app de verdade (tela cheia, sem barra de endereço) em HTTPS: pelo
http da rede local ele vira um atalho que abre no navegador.

## Segurança

- A chave do `.env` é a única proteção da API: o painel só escuta na rede local
  (`192.168.1.100:8765`). Não abra essa porta no roteador. Ela viaja sem HTTPS:
  troque a chave se o Wi-Fi de casa tiver convidados.
- A API confere tipo e faixa de tudo que grava (preferências, histórico importado,
  agendamentos, comandos) e responde 422 ou 400 em vez de guardar lixo. A página
  vai com `Content-Security-Policy` e não abre dentro de outro site.
- O login colado precisa ser do pedido que o servidor abriu (o `state` confere).
- Troque a senha do root se ela já passou por algum chat ou arquivo: `passwd`
  no servidor. O acesso de manutenção usa chave SSH.
- Para acessar de fora de casa, o jeito mais seguro é o Tailscale só na tailnet:
  `sudo tailscale serve --bg http://192.168.1.100:8765` (HTTPS em
  `https://home01.<tailnet>.ts.net`, sem abrir porta no roteador).
- Publicado na internet (`tailscale funnel`), a chave é a única barreira. Desde a
  1.4.0 o servidor bloqueia o IP depois de 8 chaves erradas em 10 min (15 min,
  dobrando a cada reincidência, até 24 h; responde 429), usa o IP real do
  `X-Forwarded-For` só quando o par é privado/Tailscale, manda HSTS quando o proxy
  diz que veio em HTTPS, recusa corpo sem `Content-Length` (411) e limita o corpo a
  256 KB (32 MB só em `/api/historico/importar`). Use uma chave longa (`openssl rand -hex 24`).
