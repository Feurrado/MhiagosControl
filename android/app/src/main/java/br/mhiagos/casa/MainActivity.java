package br.mhiagos.casa;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.util.List;

/** Painel da Casa em tela cheia: uma WebView apontando para o servidor do Mhiagos Control. */
public class MainActivity extends Activity {
    private WebView web;
    private LinearLayout erro;
    private TextView erroTexto;
    /** Quanto esperar o externo antes de usar o interno (a WebView sozinha esperaria meio minuto). */
    private static final int LIMITE_DO_EXTERNO_MS = 10000;

    private final Handler relogio = new Handler();
    private List<String> enderecos;
    private int indice;
    private String urlAtual = "";
    private String assinatura = "";
    private boolean falhou;
    private final Runnable aoEsgotar = new Runnable() {
        @Override public void run() { tentarOutroOuFalhar("tempo esgotado"); }
    };

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        getWindow().setStatusBarColor(0xFF17171A);
        getWindow().setNavigationBarColor(0xFF17171A);
        getWindow().getDecorView().setSystemUiVisibility(0); // icones claros sobre barra escura

        FrameLayout raiz = new FrameLayout(this);
        raiz.setBackgroundColor(0xFF1C1C1F);

        web = new WebView(this);
        web.setBackgroundColor(0xFF1C1C1F);
        web.setVerticalScrollBarEnabled(false);
        web.setHorizontalScrollBarEnabled(false);
        web.setOverScrollMode(View.OVER_SCROLL_NEVER);
        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true); // a chave da API fica no localStorage da pagina
        s.setCacheMode(WebSettings.LOAD_DEFAULT);
        web.setWebViewClient(new Cliente());
        raiz.addView(web, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        erro = montarErro();
        erro.setVisibility(View.GONE);
        raiz.addView(erro, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        setContentView(raiz);
        carregar();
    }

    private LinearLayout montarErro() {
        int p = (int) (24 * getResources().getDisplayMetrics().density);
        LinearLayout l = new LinearLayout(this);
        l.setOrientation(LinearLayout.VERTICAL);
        l.setGravity(Gravity.CENTER);
        l.setPadding(p, p, p, p);
        l.setBackgroundColor(0xFF1C1C1F);

        TextView t = new TextView(this);
        t.setText("Não foi possível abrir o painel");
        t.setTextColor(Color.WHITE);
        t.setTextSize(20);
        t.setGravity(Gravity.CENTER);
        l.addView(t);

        erroTexto = new TextView(this);
        erroTexto.setTextColor(0xFFB0B0B8);
        erroTexto.setGravity(Gravity.CENTER);
        erroTexto.setPadding(0, p / 2, 0, p);
        l.addView(erroTexto);

        Button de = new Button(this);
        de.setText("Tentar de novo");
        de.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { carregar(); }
        });
        l.addView(de);

        Button end = new Button(this);
        end.setText("Mudar endereço");
        end.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                startActivity(new Intent(MainActivity.this, SettingsActivity.class));
            }
        });
        l.addView(end);
        return l;
    }

    /** Recomeca pela ordem de enderecos (externo primeiro, salvo se ele acabou de falhar). */
    private void carregar() {
        assinatura = Config.assinatura(this);
        enderecos = Config.enderecos(this);
        indice = 0;
        abrir();
    }

    private void abrir() {
        relogio.removeCallbacks(aoEsgotar);
        urlAtual = enderecos.get(indice);
        falhou = false;
        erro.setVisibility(View.GONE);
        web.loadUrl(urlAtual);
        // Com outro endereco de reserva, nao espera o limite longo da WebView.
        if (indice < enderecos.size() - 1) relogio.postDelayed(aoEsgotar, LIMITE_DO_EXTERNO_MS);
    }

    private boolean ehExterno(String url) {
        String ext = Config.externo(this);
        return !ext.isEmpty() && ext.equalsIgnoreCase(url);
    }

    /** O endereco atual nao respondeu: tenta o proximo; sem mais nenhum, mostra o erro. */
    private void tentarOutroOuFalhar(String motivo) {
        relogio.removeCallbacks(aoEsgotar);
        if (ehExterno(urlAtual)) Config.externoFalhou();
        if (indice < enderecos.size() - 1) {
            indice++;
            abrir();
            return;
        }
        falhou = true;
        StringBuilder t = new StringBuilder();
        for (String e : enderecos) t.append(e).append('\n');
        erroTexto.setText(t.toString() + "\n" + motivo
                + "\n\nConfira a internet, o Wi-Fi de casa e se o servidor está no ar.");
        erro.setVisibility(View.VISIBLE);
    }

    @Override
    protected void onNewIntent(Intent i) {
        super.onNewIntent(i);
        carregar();
    }

    @Override
    protected void onResume() {
        super.onResume();
        // Voltando da tela de endereco: recarrega se mudou.
        if (!Config.assinatura(this).equals(assinatura)) carregar();
        web.onResume();
    }

    @Override
    protected void onPause() {
        relogio.removeCallbacks(aoEsgotar);
        web.onPause();
        super.onPause();
    }

    @Override
    public void onBackPressed() {
        if (erro.getVisibility() == View.GONE && web.canGoBack()) web.goBack();
        else super.onBackPressed();
    }

    private final class Cliente extends WebViewClient {
        @Override
        public boolean shouldOverrideUrlLoading(WebView v, WebResourceRequest r) {
            Uri u = r.getUrl();
            // Fica dentro do app o que e de um dos enderecos do painel; o resto abre fora.
            for (String e : enderecos) {
                String base = Uri.parse(e).getHost();
                if (base != null && base.equalsIgnoreCase(u.getHost())) return false;
            }
            try { startActivity(new Intent(Intent.ACTION_VIEW, u)); } catch (Exception ignorado) {}
            return true;
        }

        @Override
        public void onReceivedError(WebView v, WebResourceRequest r, WebResourceError e) {
            if (!r.isForMainFrame()) return;
            // So vale o erro do endereco que esta sendo tentado (o de um carregamento abandonado, nao).
            if (!r.getUrl().toString().startsWith(urlAtual)) return;
            tentarOutroOuFalhar(String.valueOf(e.getDescription()));
        }

        @Override
        public void onPageFinished(WebView v, String url) {
            if (falhou || !url.startsWith(urlAtual)) return;
            relogio.removeCallbacks(aoEsgotar);
            if (ehExterno(urlAtual)) Config.externoFuncionou();
            erro.setVisibility(View.GONE);
        }
    }
}
