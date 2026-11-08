package br.mhiagos.casa;

import android.content.Context;
import android.content.SharedPreferences;

import java.util.ArrayList;
import java.util.List;

/**
 * Enderecos do painel, guardados nas preferencias do app: o externo (https pela internet,
 * tentado primeiro) e o interno (rede local, a reserva quando o externo nao responde).
 */
final class Config {
    static final String PADRAO = "http://192.168.1.100:8765";
    private static final String PREFS = "casa";
    private static final String CHAVE_INTERNO = "url";       // nome antigo: quem ja tinha o app mantem o endereco
    private static final String CHAVE_EXTERNO = "externo";
    /** Depois de o externo falhar, o interno vai primeiro por este tempo. */
    private static final long RESERVA_MS = 60 * 1000;
    private static long externoFalhouEm = 0;

    private Config() {}

    private static SharedPreferences prefs(Context c) {
        return c.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    static String interno(Context c) {
        String u = prefs(c).getString(CHAVE_INTERNO, PADRAO);
        return u == null ? "" : u.trim();
    }

    static String externo(Context c) {
        String u = prefs(c).getString(CHAVE_EXTERNO, "");
        return u == null ? "" : u.trim();
    }

    /** Vazio em um dos dois e permitido (usa so o outro); os dois vazios voltam ao padrao interno. */
    static void salvar(Context c, String externo, String interno) {
        prefs(c).edit().putString(CHAVE_EXTERNO, externo == null ? "" : externo)
                .putString(CHAVE_INTERNO, interno == null ? "" : interno).apply();
    }

    /** Muda quando qualquer endereco muda: a tela principal recarrega ao voltar dos ajustes. */
    static String assinatura(Context c) {
        return externo(c) + "|" + interno(c);
    }

    /** A ordem de tentativa: externo e interno (so os preenchidos), invertida logo apos o externo falhar. */
    static List<String> enderecos(Context c) {
        String ext = externo(c), in = interno(c);
        List<String> l = new ArrayList<String>();
        boolean internoPrimeiro = System.currentTimeMillis() - externoFalhouEm < RESERVA_MS;
        if (internoPrimeiro) {
            if (!in.isEmpty()) l.add(in);
            if (!ext.isEmpty()) l.add(ext);
        } else {
            if (!ext.isEmpty()) l.add(ext);
            if (!in.isEmpty()) l.add(in);
        }
        if (l.isEmpty()) l.add(PADRAO);
        return l;
    }

    static void externoFalhou() { externoFalhouEm = System.currentTimeMillis(); }

    static void externoFuncionou() { externoFalhouEm = 0; }

    /**
     * Aceita endereco sem esquema (assume o dado: "http" no interno, "https" no externo)
     * e tira a barra final. Null se invalido; texto vazio e valido e devolve "".
     */
    static String normalizar(String entrada, String esquemaPadrao) {
        if (entrada == null) return null;
        String u = entrada.trim();
        if (u.isEmpty()) return "";
        if (!u.matches("(?i)^https?://.*")) u = esquemaPadrao + "://" + u;
        while (u.endsWith("/")) u = u.substring(0, u.length() - 1);
        if (!u.matches("(?i)^https?://[^/\\s]+(/\\S*)?$")) return null;
        return u;
    }
}
