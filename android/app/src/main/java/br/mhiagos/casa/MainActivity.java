package br.mhiagos.casa;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.net.http.SslError;
import android.os.Bundle;
import android.os.Handler;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.SslErrorHandler;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.util.ArrayList;
import java.util.List;

/** Painel da Casa em tela cheia: uma WebView apontando para o servidor do Mhiagos Control. */
public class MainActivity extends Activity {
    private WebView web;
    private LinearLayout erro;
    private TextView erroTexto, carregando;
    private final List<String> falhas = new ArrayList<String>();
    /** Quanto esperar um endereco que tem reserva antes de passar para ela. */
    private static final int LIMITE_COM_RESERVA_MS = 5000;
    /** O ultimo endereco da lista tambem tem limite: sem ele a tela ficava vazia para sempre. */
    private static final int LIMITE_DO_ULTIMO_MS = 12000;

    private final Handler relogio = new Handler();
    private List<String> enderecos;
    private int indice;
    private String urlAtual = "";
    private String assinatura = "";
    private boolean falhou;
    /** O app saiu da tela com um carregamento em curso (o limite de tempo foi cancelado): recomeca ao voltar. */
    private boolean recomecar;
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

        carregando = new TextView(this);
        carregando.setTextColor(0xFFB0B0B8);
        carregando.setGravity(Gravity.CENTER);
        carregando.setBackgroundColor(0xFF1C1C1F);
        raiz.addView(carregando, new FrameLayout.LayoutParams(
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
        falhas.clear();
        abrir();
    }

    private void abrir() {
        relogio.removeCallbacks(aoEsgotar);
        urlAtual = enderecos.get(indice);
        falhou = false;
        erro.setVisibility(View.GONE);
        carregando.setText("Conectando a\n" + urlAtual + "\n…");
        carregando.setVisibility(View.VISIBLE);
        web.loadUrl(urlAtual);
        // Todo endereco tem limite de tempo: com reserva, passa para ela; sem, mostra o erro.
        relogio.postDelayed(aoEsgotar, indice < enderecos.size() - 1 ? LIMITE_COM_RESERVA_MS : LIMITE_DO_ULTIMO_MS);
    }

    private boolean ehExterno(String url) {
        String ext = Config.externo(this);
        return !ext.isEmpty() && ext.equalsIgnoreCase(url);
    }

    /** O endereco atual nao respondeu: tenta o proximo; sem mais nenhum, mostra o erro. */
    private void tentarOutroOuFalhar(String motivo) {
        relogio.removeCallbacks(aoEsgotar);
        falhas.add(urlAtual + "  (" + motivo + ")");
        if (ehExterno(urlAtual)) Config.externoFalhou();
        if (indice < enderecos.size() - 1) {
            indice++;
            abrir();
            return;
        }
        falhou = true;
        web.stopLoading();
        StringBuilder t = new StringBuilder();
        for (String f : falhas) t.append(f).append('\n');
        erroTexto.setText(t.toString()
                + "\nConfira a internet, o Wi-Fi de casa e se o servidor está no ar.");
        carregando.setVisibility(View.GONE);
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
        if (recomecar || !Config.assinatura(this).equals(assinatura)) carregar();
        recomecar = false;
        web.onResume();
    }

    @Override
    protected void onPause() {
        relogio.removeCallbacks(aoEsgotar);
        recomecar = carregando.getVisibility() == View.VISIBLE;
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
            carregando.setVisibility(View.GONE);
            erro.setVisibility(View.GONE);
        }

        // A pagina inicial respondeu erro (5xx do Funnel sem o servidor atras, 4xx do proxy): conta como falha do endereco.
        @Override
        public void onReceivedHttpError(WebView v, WebResourceRequest r, WebResourceResponse resp) {
            if (!r.isForMainFrame() || !r.getUrl().toString().startsWith(urlAtual)) return;
            if (resp.getStatusCode() >= 400) tentarOutroOuFalhar("HTTP " + resp.getStatusCode());
        }

        // Sem isto a WebView cancela em silencio e fica a tela vazia.
        @Override
        public void onReceivedSslError(WebView v, SslErrorHandler h, SslError e) {
            h.cancel();
            tentarOutroOuFalhar("certificado recusado");
        }
    }
}
