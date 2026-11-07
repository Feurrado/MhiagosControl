# Base fixada (versao e digest): atualizar de proposito, rodando os testes antes.
FROM python:3.12.16-slim-trixie@sha256:4840d514c583a4d3d8cad3940e68dee6f0f6f3635c12fcd0a3d74c21a5b93a54
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 MHIAGOS_DADOS=/data TZ=America/Sao_Paulo
WORKDIR /srv
# Dependencias com hash conferido (requirements.lock, gerado pelo pip-compile).
COPY requirements.lock .
RUN pip install --no-cache-dir --require-hashes -r requirements.lock
COPY app ./app
# Sem root: o processo so escreve em /data (o volume), dono deste usuario.
RUN useradd --system --uid 10001 --no-create-home --shell /usr/sbin/nologin mhiagos \
 && mkdir -p /data && chown 10001:10001 /data
USER 10001:10001
VOLUME /data
EXPOSE 8765
HEALTHCHECK --interval=60s --timeout=5s CMD python -c "import urllib.request;urllib.request.urlopen('http://127.0.0.1:8765/api/saude')" || exit 1
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8765", "--no-access-log", "--no-server-header"]
