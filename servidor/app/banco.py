"""SQLite em /data: historico do ar, ciclos da lavadora, agendamentos e a conta."""

import json
import os
import sqlite3
import threading

ARQUIVO = os.path.join(os.environ.get("MHIAGOS_DADOS", "/data"), "casa.db")

_trava = threading.Lock()
_con = None


def con():
    global _con
    with _trava:
        if _con is None:
            os.makedirs(os.path.dirname(ARQUIVO), exist_ok=True)
            _con = sqlite3.connect(ARQUIVO, check_same_thread=False, isolation_level=None)
            _con.row_factory = sqlite3.Row
            _con.execute("PRAGMA journal_mode=WAL")
            _con.executescript("""
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
                CREATE TABLE IF NOT EXISTS eventos (
                    id INTEGER PRIMARY KEY AUTOINCREMENT, t INTEGER NOT NULL, puid TEXT, tipo TEXT, texto TEXT);
                CREATE TABLE IF NOT EXISTS kv (k TEXT PRIMARY KEY, v TEXT);
            """)
        return _con


def ler_kv(k, padrao=None):
    r = con().execute("SELECT v FROM kv WHERE k=?", (k,)).fetchone()
    return json.loads(r["v"]) if r else padrao


def gravar_kv(k, v):
    con().execute("INSERT INTO kv(k,v) VALUES(?,?) ON CONFLICT(k) DO UPDATE SET v=excluded.v", (k, json.dumps(v)))


def apagar_kv(k):
    con().execute("DELETE FROM kv WHERE k=?", (k,))


def linhas(sql, args=()):
    return [dict(r) for r in con().execute(sql, args).fetchall()]
