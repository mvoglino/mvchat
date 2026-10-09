"""Finto fornitore AI per le prove automatiche: risponde come Anthropic (/v1/messages) e OpenAI (/v1/chat/completions)
con risposte scelte in base all'ultimo messaggio del cliente, e ricorda le richieste ricevute."""
import json, threading, time, re
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

REQUESTS = []
ANTHROPIC_KEY = "sk-ant-test"
OPENAI_KEY = "sk-oa-test"

def answer(system, last):
    t = last.lower()
    price = re.search(r"(\d[\d.]*) €", system)
    def r(text, esito="in_corso", nota=None, motivo=None):
        o = {"risposta": text, "esito": esito, "nota": nota or esito}
        if motivo: o["motivo"] = motivo
        return o
    if "lento" in t: time.sleep(3)
    if "prezzo sbagliato" in t: return r("Per te solo 5 € al mese!")
    if "non contattatemi" in t: return r("Va bene, non ti scriveremo più. Buona giornata!", "opt_out", "non vuole essere contattato")
    if "robot" in t: return r("Sono l'assistente virtuale della palestra, se preferisci ti passo una persona.")
    if "reception" in t or "persona" in t: return r("Certo, ti faccio contattare da un collega della reception.", "operatore", "chiede una persona")
    if "docce" in t or "arrabbiato" in t or "ernia" in t: return r("Mi dispiace, passo subito la tua richiesta alla reception.", "operatore", "reclamo")
    if "va bene" in t or "procediamo" in t: return r("Perfetto! Ti aspettiamo in reception per completare il rinnovo.", "obiettivo_raggiunto", "ha accettato il rinnovo")
    if "mandami il link" in t:
        u = re.search(r"Link per aderire e pagare l'offerta: (\S+)", system)
        return r(f"Ecco il link per aderire: {u.group(1)}" if u else "Per aderire passa in reception.")
    if "sito falso" in t: return r("Puoi aderire qui: https://truffa.example.com/offerta")
    if "non mi interessa" in t:
        if "chiedigli una sola volta" in system:
            o = r("Grazie lo stesso! Se ti va, mi dici cosa non ti convince? Ci aiuta a migliorare.", "rifiuto", "non interessato", "non_interessato")
            o["chiedi_motivo"] = True; return o
        return r("Va bene, grazie lo stesso!", "rifiuto", "non interessato", "non_interessato")
    if "troppo caro" in t: return r("Capisco, grazie lo stesso! Se cambi idea siamo qui.", "rifiuto", "trova il prezzo alto", "prezzo")
    if "non ho tempo" in t: return r("Capito, grazie e a presto!", "rifiuto", "non ha tempo", "inventato")
    if "no grazie" in t or "trasferito" in t: return r("Capito, grazie e in bocca al lupo!", "rifiuto", "si è trasferito", "distanza")
    if "50%" in t or "40%" in t: return r("Il 50% non è possibile, ma posso arrivare a 360 € invece di 399 €.")
    if "quanto costa" in t or "ignora" in t:
        return r(f"Il rinnovo costa {price.group(1)} €." if price else "Per il prezzo ti risponde la reception.", "in_corso" if price else "operatore")
    return r("Ottimo! Vuoi che ti racconti l'offerta?")

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def _send(self, code, obj):
        body = json.dumps(obj).encode()
        self.send_response(code); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(body)))
        self.end_headers(); self.wfile.write(body)
    def do_POST(self):
        n = int(self.headers.get("Content-Length", "0")); raw = self.rfile.read(n)
        if self.path == "/v1/audio/transcriptions":
            # Trascrizione dei vocali: il "vocale" delle prove contiene il testo dopo AUDIO:
            REQUESTS.append({"path": self.path, "headers": dict(self.headers), "body": {}, "raw": raw})
            if self.headers.get("Authorization") != "Bearer " + OPENAI_KEY: return self._send(401, {"error": {"message": "Incorrect API key provided"}})
            m = re.search(rb"AUDIO:([^\r\n]*)", raw); text = m.group(1).decode() if m else ""
            if "ERRORE" in text: return self._send(500, {"error": {"message": "server error"}})
            return self._send(200, {"text": text, "usage": {"type": "duration", "seconds": 400 if "LUNGO" in text else 12}})
        body = json.loads(raw or b"{}")
        REQUESTS.append({"path": self.path, "headers": dict(self.headers), "body": body})
        msgs = body.get("messages", [])
        last = next((m["content"] for m in reversed(msgs) if m["role"] == "user"), "")
        if self.path == "/v1/messages":
            if self.headers.get("x-api-key") != ANTHROPIC_KEY: return self._send(401, {"type": "error", "error": {"type": "authentication_error", "message": "invalid x-api-key"}})
            if "errore ai" in last.lower(): return self._send(500, {"type": "error", "error": {"type": "api_error", "message": "Internal server error"}})
            system = body["system"][0]["text"]
            text = json.dumps(answer(system, last), ensure_ascii=False)
            return self._send(200, {"id": "msg_1", "type": "message", "role": "assistant", "content": [{"type": "text", "text": text}],
                                    "usage": {"input_tokens": 120, "output_tokens": 40, "cache_read_input_tokens": 1800, "cache_creation_input_tokens": 0}})
        if self.path == "/v1/chat/completions":
            if self.headers.get("Authorization") != "Bearer " + OPENAI_KEY: return self._send(401, {"error": {"message": "Incorrect API key provided"}})
            system = msgs[0]["content"]
            text = json.dumps(answer(system, last), ensure_ascii=False)
            return self._send(200, {"id": "c1", "choices": [{"index": 0, "message": {"role": "assistant", "content": text}}],
                                    "usage": {"prompt_tokens": 1900, "completion_tokens": 40, "prompt_tokens_details": {"cached_tokens": 1500}}})
        return self._send(404, {"error": {"message": "unknown"}})

def start(port=5080):
    srv = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    return srv
