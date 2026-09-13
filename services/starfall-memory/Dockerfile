FROM python:3.13-slim
WORKDIR /app
COPY core.py server.py ./
RUN groupadd --gid 10001 starfall && useradd --uid 10001 --gid starfall --no-create-home starfall && mkdir /data && chown starfall:starfall /data
USER 10001:10001
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
CMD ["python", "server.py", "--container", "--config", "/run/secrets/starfall_memory_config", "--data-dir", "/data", "--port", "17864"]
