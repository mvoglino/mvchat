"""Finto server Meta per le prove automatiche: risponde come la WhatsApp Cloud API e ricorda le richieste ricevute."""
import json, threading, re
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

REQUESTS = []
TOKEN = "good-token"
MEDIA = {}  # file mandati dai clienti: id → (tipo, contenuto)

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def _send(self, code, obj):
        body = json.dumps(obj).encode()
        self.send_response(code); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(body)))
        self.end_headers(); self.wfile.write(body)
    def _auth(self):
        if self.headers.get("Authorization") != "Bearer " + TOKEN:
            self._send(401, {"error": {"message": "Invalid OAuth access token", "code": 190}}); return False
        return True
    def do_GET(self):
        REQUESTS.append({"method": "GET", "path": self.path})
        if not self._auth(): return
        if self.path.startswith("/download/"):
            mid = self.path.split("/")[-1]
            if mid not in MEDIA: return self._send(404, {"error": {"message": "not found"}})
            mime, data = MEDIA[mid]
            self.send_response(200); self.send_header("Content-Type", mime); self.send_header("Content-Length", str(len(data)))
            self.end_headers(); self.wfile.write(data); return
        mm = re.match(r"^/v[\d.]+/(media-[\w-]+)$", self.path)
        if mm:
            mid = mm.group(1)
            if mid not in MEDIA: return self._send(400, {"error": {"message": "Unsupported get request", "code": 100}})
            return self._send(200, {"url": f"http://127.0.0.1:5079/download/{mid}", "mime_type": MEDIA[mid][0], "file_size": len(MEDIA[mid][1]), "id": mid})
        if "/message_templates" in self.path:
            return self._send(200, {"data": [{"id": "tpl123", "name": "x", "status": "APPROVED", "language": "it"}]})
        return self._send(200, {"display_phone_number": "+39 0172 000000", "verified_name": "FitActive Bra", "quality_rating": "GREEN", "messaging_limit_tier": "TIER_1K", "id": "111"})
    def do_POST(self):
        n = int(self.headers.get("Content-Length", "0")); body = json.loads(self.rfile.read(n) or b"{}")
        REQUESTS.append({"method": "POST", "path": self.path, "body": body})
        if not self._auth(): return
        if self.path.endswith("/message_templates"):
            return self._send(200, {"id": "tpl123", "status": "PENDING", "category": body.get("category")})
        if self.path.endswith("/messages"):
            return self._send(200, {"messaging_product": "whatsapp", "contacts": [{"wa_id": body.get("to")}], "messages": [{"id": f"wamid.TEST{len(REQUESTS)}"}]})
        return self._send(404, {"error": {"message": "unknown"}})

def start(port=5079):
    srv = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    threading.Thread(target=srv.serve_forever, daemon=True).start()
    return srv
