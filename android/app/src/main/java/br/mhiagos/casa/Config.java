package br.mhiagos.casa;

import android.content.Context;
import android.content.SharedPreferences;

/** Endereco do painel, guardado nas preferencias do app. */
final class Config {
    static final String PADRAO = "http://192.168.1.100:8765";
    private static final String PREFS = "casa";
    private static final String CHAVE_URL = "url";

    private Config() {}

    static String url(Context c) {
        SharedPreferences p = c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        String u = p.getString(CHAVE_URL, PADRAO);
        return u == null || u.trim().isEmpty() ? PADRAO : u.trim();
    }

    static void salvar(Context c, String url) {
        c.getSharedPreferences(PREFS, Context.MODE_PRIVATE).edit().putString(CHAVE_URL, url).apply();
    }

    /** Aceita "192.168.1.100:8765" sem esquema (assume http) e tira a barra final. Null se invalido. */
    static String normalizar(String entrada) {
        if (entrada == null) return null;
        String u = entrada.trim();
        if (u.isEmpty()) return null;
        if (!u.matches("(?i)^https?://.*")) u = "http://" + u;
        while (u.endsWith("/")) u = u.substring(0, u.length() - 1);
        if (!u.matches("(?i)^https?://[^/\\s]+(/\\S*)?$")) return null;
        return u;
    }
}
