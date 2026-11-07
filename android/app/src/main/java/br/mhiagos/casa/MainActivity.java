package br.mhiagos.casa;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
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

/** Painel da Casa em tela cheia: uma WebView apontando para o servidor do Mhiagos Control. */
public class MainActivity extends Activity {
    private WebView web;
    private LinearLayout erro;
    private TextView erroTexto;
    private String urlAtual;
    private boolean falhou;

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

    private void carregar() {
        urlAtual = Config.url(this);
        falhou = false;
        erro.setVisibility(View.GONE);
        web.loadUrl(urlAtual);
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
        if (!Config.url(this).equals(urlAtual)) carregar();
        web.onResume();
    }

    @Override
    protected void onPause() {
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
            String base = Uri.parse(urlAtual).getHost();
            if (base != null && base.equalsIgnoreCase(u.getHost())) return false;
            try { startActivity(new Intent(Intent.ACTION_VIEW, u)); } catch (Exception ignorado) {}
            return true;
        }

        @Override
        public void onReceivedError(WebView v, WebResourceRequest r, WebResourceError e) {
            if (!r.isForMainFrame()) return;
            falhou = true;
            erroTexto.setText(urlAtual + "\n\n" + e.getDescription()
                    + "\n\nConfira se o celular está no Wi-Fi de casa (ou na VPN) e se o servidor está no ar.");
            erro.setVisibility(View.VISIBLE);
        }

        @Override
        public void onPageFinished(WebView v, String url) {
            if (!falhou) erro.setVisibility(View.GONE);
        }
    }
}
