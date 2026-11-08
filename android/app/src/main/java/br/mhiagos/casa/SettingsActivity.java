package br.mhiagos.casa;

import android.app.Activity;
import android.os.Bundle;
import android.text.InputType;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

/** Tela unica para trocar os enderecos do painel: externo (internet) e interno (rede de casa). */
public class SettingsActivity extends Activity {
    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        int p = (int) (20 * getResources().getDisplayMetrics().density);

        LinearLayout raiz = new LinearLayout(this);
        raiz.setOrientation(LinearLayout.VERTICAL);
        raiz.setPadding(p, p * 2, p, p);
        raiz.setBackgroundColor(0xFF1C1C1F);

        TextView titulo = new TextView(this);
        titulo.setText("Endereços do painel");
        titulo.setTextColor(0xFFFFFFFF);
        titulo.setTextSize(22);
        raiz.addView(titulo);

        TextView dica = new TextView(this);
        dica.setText("O externo (https, pela internet) é tentado primeiro; se não responder, o app usa o interno (rede de casa). Deixe um deles vazio para usar só o outro. A chave de acesso é pedida uma vez em cada endereço.");
        dica.setTextColor(0xFFB0B0B8);
        dica.setPadding(0, p / 2, 0, p / 2);
        raiz.addView(dica);

        final EditText externo = campo(raiz, "Endereço externo", Config.externo(this), "https://home01.seu-nome.ts.net");
        final EditText interno = campo(raiz, "Endereço interno", Config.interno(this), "192.168.1.100:8765");

        LinearLayout linha = new LinearLayout(this);
        linha.setGravity(Gravity.END);
        linha.setPadding(0, p, 0, 0);

        Button padrao = new Button(this);
        padrao.setText("Padrão");
        padrao.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { interno.setText(Config.PADRAO); }
        });
        linha.addView(padrao);

        Button salvar = new Button(this);
        salvar.setText("Salvar");
        salvar.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                String e = Config.normalizar(externo.getText().toString(), "https");
                String i = Config.normalizar(interno.getText().toString(), "http");
                if (e == null || i == null) {
                    Toast.makeText(SettingsActivity.this, "Endereço inválido", Toast.LENGTH_SHORT).show();
                    return;
                }
                if (e.isEmpty() && i.isEmpty()) {
                    Toast.makeText(SettingsActivity.this, "Preencha pelo menos um endereço", Toast.LENGTH_SHORT).show();
                    return;
                }
                Config.salvar(SettingsActivity.this, e, i);
                // Reabre o painel limpo, ja nos enderecos novos.
                android.content.Intent it = new android.content.Intent(SettingsActivity.this, MainActivity.class);
                it.addFlags(android.content.Intent.FLAG_ACTIVITY_CLEAR_TOP | android.content.Intent.FLAG_ACTIVITY_NEW_TASK);
                startActivity(it);
                finish();
            }
        });
        linha.addView(salvar);
        raiz.addView(linha);

        setContentView(raiz);
    }

    private EditText campo(LinearLayout raiz, String rotulo, String valor, String exemplo) {
        TextView l = new TextView(this);
        l.setText(rotulo);
        l.setTextColor(0xFFFFFFFF);
        l.setPadding(0, (int) (12 * getResources().getDisplayMetrics().density), 0, 0);
        raiz.addView(l);
        EditText c = new EditText(this);
        c.setText(valor);
        c.setHint(exemplo);
        c.setHintTextColor(0xFF70707A);
        c.setSingleLine(true);
        c.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        c.setTextColor(0xFFFFFFFF);
        raiz.addView(c, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
        return c;
    }
}
