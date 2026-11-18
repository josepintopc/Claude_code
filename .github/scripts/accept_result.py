#!/usr/bin/env python3
"""Valida o resultado enviado por uma issue "[resultado] ..." e o adiciona a community/results.json.

Entrada: corpo da issue em ISSUE_BODY e número em ISSUE_NUMBER (variáveis de ambiente, nunca interpoladas no shell).
Saída: escreve status=accepted|rejected|duplicate e message=... em GITHUB_OUTPUT (ou na saída padrão).
As regras são as mesmas de CommunityResults.IsValid no app (src/SupernovaBench.Core/Extras/CommunityResults.cs).
"""
import json
import math
import os
import re
import sys
import uuid
from datetime import datetime, timezone

TESTS = {"cpu.multi", "cpu.single", "cpu.sustained", "gpu.scene", "mem.bench", "disk.bench"}
TEXT = {"cpuName": True, "gpuName": False, "device": False, "preset": True, "engineVersion": True, "appVersion": False,
        "resolution": False, "unit": False}
NUMBERS = {"avgFps", "low1Fps", "seconds"}
INTS = {"threads", "samples", "passes"}
MAX_RESULTS = 5000


def extract(body: str):
    m = re.search(r"```json\s*\n(.*?)\n```", body or "", re.S)
    if not m:
        raise ValueError("bloco ```json não encontrado")
    if len(m.group(1)) > 20000:
        raise ValueError("bloco grande demais")
    data = json.loads(m.group(1))
    if not isinstance(data, dict):
        raise ValueError("o bloco não é um objeto JSON")
    return data


def finite(v):
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def clean(data: dict) -> dict:
    out = {}
    try:
        out["id"] = str(uuid.UUID(str(data.get("id"))))
    except ValueError:
        raise ValueError("id inválido")
    test = data.get("test")
    if test not in TESTS:
        raise ValueError("teste desconhecido")
    out["test"] = test
    score = data.get("score")
    if not finite(score) or not 0 < score < 10_000_000:
        raise ValueError("pontuação inválida")
    out["score"] = round(float(score), 1)
    for key, required in TEXT.items():
        v = data.get(key)
        if v is None:
            if required:
                raise ValueError(f"campo obrigatório ausente: {key}")
            continue
        if not isinstance(v, str) or len(v) > 120 or (required and not v.strip()) or any(ord(c) < 32 or c in "<>`|" for c in v):
            raise ValueError(f"campo inválido: {key}")
        out[key] = v.strip()
    for key in NUMBERS:
        v = data.get(key)
        if v is not None:
            if not finite(v) or v < 0 or v > 1e7:
                raise ValueError(f"campo inválido: {key}")
            out[key] = float(v)
    for key in INTS:
        v = data.get(key)
        if v is not None:
            if not isinstance(v, int) or isinstance(v, bool) or v < 0 or v > 10**12:
                raise ValueError(f"campo inválido: {key}")
            out[key] = v
    if out.get("threads", 0) > 4096:
        raise ValueError("threads inválido")
    metrics = data.get("metrics")
    if metrics is not None:
        if not isinstance(metrics, dict) or len(metrics) > 64:
            raise ValueError("métricas inválidas")
        out["metrics"] = {}
        for k, v in metrics.items():
            if not isinstance(k, str) or not re.fullmatch(r"[A-Za-z0-9_.]{1,40}", k) or not finite(v):
                raise ValueError("métricas inválidas")
            out["metrics"][k] = float(v)
    ts = data.get("timestamp")
    if isinstance(ts, str) and re.fullmatch(r"[0-9T:.+\-Z]{10,40}", ts):
        out["timestamp"] = ts
    return out


def main(path: str) -> int:
    body = os.environ.get("ISSUE_BODY", "")
    issue = os.environ.get("ISSUE_NUMBER", "")
    try:
        entry = clean(extract(body))
        status, message = "accepted", f"Resultado adicionado: {entry['test']} · {entry.get('device') or entry.get('gpuName') or entry['cpuName']} · {entry['score']:.0f} pts."
    except (ValueError, json.JSONDecodeError) as e:
        entry, status, message = None, "rejected", f"Resultado não aceito: {e}."

    if entry is not None:
        with open(path, encoding="utf-8") as f:
            db = json.load(f)
        if db.get("format") != "supernova-community":
            raise SystemExit("results.json com formato inesperado")
        results = db.setdefault("results", [])
        if any(r.get("id") == entry["id"] for r in results):
            status, message = "duplicate", "Este resultado já está na comparação online."
        else:
            if issue.isdigit():
                entry["issue"] = int(issue)
            results.append(entry)
            del results[:-MAX_RESULTS]
            db["updated"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
            with open(path, "w", encoding="utf-8") as f:
                json.dump(db, f, ensure_ascii=False, indent=2)
                f.write("\n")

    out = os.environ.get("GITHUB_OUTPUT")
    lines = f"status={status}\nmessage={message}\n"
    if out:
        with open(out, "a", encoding="utf-8") as f:
            f.write(lines)
    else:
        sys.stdout.write(lines)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "community/results.json"))
