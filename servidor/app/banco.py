"""SQLite em /data: historico do ar, ciclos da lavadora, agendamentos e a conta.

Uma conexao por thread (as do FastAPI, a da leitura, a dos agendamentos). Antes
era uma so, compartilhada sem trava: operacoes de varios passos (importar,
fechar ciclo, agendar) podiam se cruzar. Agora o que tem mais de um passo roda
em transacao(), e o SQLite serializa quem escreve (WAL deixa ler junto).
"""

import json
import os
import sqlite3
import threading
from contextlib import contextmanager

ARQUIVO = os.path.join(os.environ.get("MHIAGOS_DADOS", "/data"), "casa.db")

_local = threading.local()
_esquema_em = None      # arquivo que ja tem as tabelas (os testes trocam de arquivo)
_trava_esquema = threading.Lock()

ESQUEMA = """
    CREATE TABLE IF NOT EXISTS amostras_ar (
        puid TEXT NOT NULL, t INTEGER NOT NULL, amb REAL, alvo REAL,
        ligado INTEGER NOT NULL, modo INTEGER NOT NULL, PRIMARY KEY (puid, t));
    CREATE TABLE IF NOT EXISTS ciclos (
        puid TEXT NOT NULL, inicio INTEGER NOT NULL, fim INTEGER NOT NULL, programa INTEGER,
        agua REAL, energia REAL, concluido INTEGER, PRIMARY KEY (puid, inicio));
    CREATE TABLE IF NOT EXISTS em_curso (
        puid TEXT PRIMARY KEY, inicio INTEGER NOT NULL, programa INTEGER, agua REAL, energia REAL);
    CREATE TABLE IF NOT EXISTS agendamentos (
        id INTEGER PRIMARY KEY AUTOINCREMENT, puid TEXT NOT NULL, quando INTEGER NOT NULL,
        props TEXT NOT NULL, nome TEXT, criado INTEGER NOT NULL);
    CREATE TABLE IF NOT EXISTS resultados (
        id INTEGER PRIMARY KEY, puid TEXT, nome TEXT, quando INTEGER, estado TEXT NOT NULL,
        t INTEGER NOT NULL, tentativas INTEGER NOT NULL DEFAULT 0, detalhe TEXT);
    CREATE TABLE IF NOT EXISTS eventos (
        id INTEGER PRIMARY KEY AUTOINCREMENT, t INTEGER NOT NULL, puid TEXT, tipo TEXT, texto TEXT);
    CREATE TABLE IF NOT EXISTS kv (k TEXT PRIMARY KEY, v TEXT);
"""


def _colunas(c, tabela):
    return {r["name"] for r in c.execute("PRAGMA table_info(%s)" % tabela).fetchall()}


def _migrar(c):
    """Colunas novas em bancos de versoes anteriores (1.2 nao tinha nova tentativa)."""
    cols = _colunas(c, "agendamentos")
    if "tentativas" not in cols:
        c.execute("ALTER TABLE agendamentos ADD COLUMN tentativas INTEGER NOT NULL DEFAULT 0")
    if "proxima" not in cols:
        c.execute("ALTER TABLE agendamentos ADD COLUMN proxima INTEGER")
    if "erro" not in cols:
        c.execute("ALTER TABLE agendamentos ADD COLUMN erro TEXT")


def con():
    """A conexao desta thread (criada na primeira vez)."""
    global _esquema_em
    c = getattr(_local, "con", None)
    if c is not None and getattr(_local, "arquivo", None) != ARQUIVO:   # testes trocam o arquivo
        c.close()
        c = None
    if c is None:
        os.makedirs(os.path.dirname(ARQUIVO), exist_ok=True)
        # timeout: quem chega com outra transacao escrevendo espera em vez de falhar.
        c = sqlite3.connect(ARQUIVO, timeout=15, isolation_level=None, check_same_thread=False)
        c.row_factory = sqlite3.Row
        c.execute("PRAGMA journal_mode=WAL")
        c.execute("PRAGMA synchronous=NORMAL")
        c.execute("PRAGMA busy_timeout=15000")
        with _trava_esquema:
            if _esquema_em != ARQUIVO:
                c.executescript(ESQUEMA)
                _migrar(c)
                _esquema_em = ARQUIVO
        _local.con = c
        _local.arquivo = ARQUIVO
    return c


@contextmanager
def transacao():
    """Varios passos que precisam acontecer juntos (ou nenhum): BEGIN IMMEDIATE ... COMMIT."""
    c = con()
    if c.in_transaction:          # aninhada: faz parte da de fora
        yield c
        return
    c.execute("BEGIN IMMEDIATE")
    try:
        yield c
    except BaseException:
        c.execute("ROLLBACK")
        raise
    c.execute("COMMIT")


def ler_kv(k, padrao=None):
    r = con().execute("SELECT v FROM kv WHERE k=?", (k,)).fetchone()
    return json.loads(r["v"]) if r else padrao


def gravar_kv(k, v):
    con().execute("INSERT INTO kv(k,v) VALUES(?,?) ON CONFLICT(k) DO UPDATE SET v=excluded.v", (k, json.dumps(v)))


def apagar_kv(k):
    con().execute("DELETE FROM kv WHERE k=?", (k,))


def linhas(sql, args=()):
    return [dict(r) for r in con().execute(sql, args).fetchall()]


def fechar():
    """Testes: solta a conexao desta thread (o arquivo pode ser apagado depois)."""
    global _esquema_em
    c = getattr(_local, "con", None)
    if c is not None:
        c.close()
        _local.con = None
    _esquema_em = None
