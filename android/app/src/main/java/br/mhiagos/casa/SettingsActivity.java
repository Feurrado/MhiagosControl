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

/** Tela unica para trocar o endereco do painel (rede local hoje, Tailscale depois). */
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
        titulo.setText("Endereço do painel");
        titulo.setTextColor(0xFFFFFFFF);
        titulo.setTextSize(22);
        raiz.addView(titulo);

        TextView dica = new TextView(this);
        dica.setText("Ex.: http://192.168.1.100:8765 ou, com Tailscale, https://home01.seu-nome.ts.net");
        dica.setTextColor(0xFFB0B0B8);
        dica.setPadding(0, p / 2, 0, p / 2);
        raiz.addView(dica);

        final EditText campo = new EditText(this);
        campo.setText(Config.url(this));
        campo.setSingleLine(true);
        campo.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        campo.setTextColor(0xFFFFFFFF);
        raiz.addView(campo, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        LinearLayout linha = new LinearLayout(this);
        linha.setGravity(Gravity.END);
        linha.setPadding(0, p, 0, 0);

        Button padrao = new Button(this);
        padrao.setText("Padrão");
        padrao.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) { campo.setText(Config.PADRAO); }
        });
        linha.addView(padrao);

        Button salvar = new Button(this);
        salvar.setText("Salvar");
        salvar.setOnClickListener(new View.OnClickListener() {
            @Override public void onClick(View v) {
                String u = Config.normalizar(campo.getText().toString());
                if (u == null) {
                    Toast.makeText(SettingsActivity.this, "Endereço inválido", Toast.LENGTH_SHORT).show();
                    return;
                }
                Config.salvar(SettingsActivity.this, u);
                // Reabre o painel limpo, ja no endereco novo.
                android.content.Intent i = new android.content.Intent(SettingsActivity.this, MainActivity.class);
                i.addFlags(android.content.Intent.FLAG_ACTIVITY_CLEAR_TOP | android.content.Intent.FLAG_ACTIVITY_NEW_TASK);
                startActivity(i);
                finish();
            }
        });
        linha.addView(salvar);
        raiz.addView(linha);

        setContentView(raiz);
    }
}
