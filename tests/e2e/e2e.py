import re, urllib.request, urllib.parse, http.cookiejar, sys, html as html_lib
import os
BASE = os.environ.get("MVCHAT_URL", "http://127.0.0.1:5078")
results = []
def check(name, cond, info=""):
    results.append((name, bool(cond), info))

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k): return None

class Client:
    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar), NoRedirect)
    def req(self, path, data=None):
        body = urllib.parse.urlencode(data).encode() if data is not None else None
        try:
            r = self.op.open(urllib.request.Request(BASE + path, data=body))
            return r.status, html_lib.unescape(r.read().decode()), r.headers.get("Location")
        except urllib.error.HTTPError as e:
            return e.code, e.read().decode(errors="ignore"), e.headers.get("Location")
    def token(self, path):
        s, html, _ = self.req(path)
        m = re.search(r'name="__RequestVerificationToken" type="hidden" value="([^"]+)"', html)
        return m.group(1) if m else ""
    def post_json(self, path, obj, form_path):
        import json
        tok = self.token(form_path)
        req = urllib.request.Request(BASE + path, data=json.dumps(obj).encode(), headers={"Content-Type": "application/json", "RequestVerificationToken": tok})
        try:
            r = self.op.open(req); return r.status, json.loads(r.read().decode() or "{}")
        except urllib.error.HTTPError as e:
            body = e.read().decode(errors="ignore")
            try: return e.code, json.loads(body)
            except Exception: return e.code, {"raw": body[:300]}
    def post(self, path, data, form_path=None):
        data = dict(data); data["__RequestVerificationToken"] = self.token(form_path or path)
        return self.req(path, data)

def temp_pwd(html):
    m = re.search(r"<code>([A-Za-z0-9]{12})</code>", html); return m.group(1) if m else None

# 1. Install
c = Client()
s, html, loc = c.req("/Install", {"Input.DbHost":os.environ.get("DB_HOST","127.0.0.1"),"Input.DbPort":"3306","Input.DbName":"mvchat","Input.DbUser":"mv","Input.DbPassword":"Pwd12345!",
    "Input.ProductName":"mvchat","Input.AdminName":"Maurizio Voglino","Input.AdminEmail":"admin@mvitalia.test","Input.AdminPassword":"Admin12345x","Input.AdminPassword2":"Admin12345x"})
check("installazione completata", "Installazione completata" in html, html[:300] if "Installazione completata" not in html else "")
check("indirizzo tick mostrato", "/jobs/tick?token=" in html)
tick = re.search(r"/jobs/tick\?token=([a-f0-9]+)", html)
s,_,loc = c.req("/Install"); check("installazione bloccata dopo il primo uso", s == 302 and loc.endswith("/Login"))

# 2. Superadmin
sa = Client()
s,_,loc = sa.post("/Login", {"Email":"admin@mvitalia.test","Password":"Admin12345x"})
check("login superadmin", s == 302 and loc == "/", f"{s} {loc}")
def mk_org(name):
    s,_,loc = sa.post("/Orgs/Edit", {"Input.Name":name,"Input.Slug":"","Input.PrimaryColor":"#F6931E","Input.IsActive":"true"})
    return s == 302
check("crea catena FitActive", mk_org("FitActive"))
check("crea catena Altra", mk_org("Altra Catena"))
s,html,_ = sa.req("/Orgs"); orgs = dict((n, int(i)) for i, n in re.findall(r'href="/Orgs/Edit/(\d+)".*?', html) and re.findall(r'/Orgs/Edit/(\d+)', html) and [])
ids = [int(x) for x in re.findall(r'/Orgs/Edit/(\d+)', html)]
names = re.findall(r"<td><b>([^<]+)</b><div class=\"muted\">", html)
org = dict(zip(names, ids)); check("elenco catene", "FitActive" in org and "Altra Catena" in org, str(org))
def mk_gym(orgname, name):
    s,_,_ = sa.post("/Gyms/Edit", {"Input.OrganizationId":org[orgname],"Input.Name":name,"Input.City":"X","Input.IsActive":"true"}); return s == 302
check("crea palestre", mk_gym("FitActive","FitActive Alba") and mk_gym("FitActive","FitActive Bra") and mk_gym("Altra Catena","Altra Palestra"))
s,html,_ = sa.req("/Gyms")
gids = re.findall(r'/Gyms/Edit/(\d+)', html); gnames = re.findall(r"<td><b>([^<]+)</b><div class=\"muted\">", html)
gym = dict(zip(gnames, map(int, gids))); check("elenco palestre superadmin = 3", len(gym) == 3, str(gym))

def mk_user(client, email, role, orgid="", gymid="", form="/Users/Edit"):
    s, html, loc = client.post("/Users/Edit", {"Input.FullName":email.split("@")[0],"Input.Email":email,"Input.Role":role,"Input.OrganizationId":orgid,"Input.GymId":gymid,"Input.IsActive":"true"})
    if s != 302: return None, html
    _, lst, _ = client.req("/Users"); return temp_pwd(lst), lst
pw_dir,_ = mk_user(sa, "direzione@fitactive.test", "orgadmin", org["FitActive"])
pw_mgr,_ = mk_user(sa, "alba@fitactive.test", "manager", "", gym["FitActive Alba"])
pw_oth,_ = mk_user(sa, "altra@altra.test", "manager", "", gym["Altra Palestra"])
check("utenti creati con password provvisoria", all([pw_dir, pw_mgr, pw_oth]), str([pw_dir,pw_mgr,pw_oth]))
_, lst, _ = sa.req("/Users"); check("password provvisoria mostrata una sola volta", temp_pwd(lst) is None)

# 3. Direzione FitActive
d = Client()
s,_,loc = d.post("/Login", {"Email":"direzione@fitactive.test","Password":pw_dir})
check("primo accesso porta al cambio password", loc == "/Account/Password", str(loc))
s,_,loc = d.req("/Gyms"); check("finché non cambia password non usa il pannello", s == 302 and loc == "/Account/Password")
s,_,loc = d.post("/Account/Password", {"Current":pw_dir,"New":"Direzione2026x","New2":"Direzione2026x"})
check("cambio password", s == 302 and loc == "/", f"{s} {loc}")
s,html,_ = d.req("/Gyms")
check("direzione vede solo le sue palestre", "FitActive Alba" in html and "FitActive Bra" in html and "Altra Palestra" not in html)
s,_,_ = d.req(f"/Gyms/Edit/{gym['Altra Palestra']}"); check("direzione non apre palestra di altra catena", s == 404, str(s))
s,_,_ = d.req("/Orgs"); check("direzione non vede le catene", s in (302, 403), str(s))
s,html,_ = d.post("/Users/Edit", {"Input.FullName":"x","Input.Email":"hack@x.test","Input.Role":"superadmin","Input.IsActive":"true"})
check("direzione non può creare superadmin", s == 200 and "Non puoi assegnare" in html)
s,html,_ = d.post("/Users/Edit", {"Input.FullName":"x","Input.Email":"op@x.test","Input.Role":"operator","Input.GymId":gym["Altra Palestra"],"Input.IsActive":"true"})
check("direzione non assegna palestre di altre catene", s == 200 and "Scegli la palestra" in html)
s,html,_ = d.post("/Gyms/Edit", {"Input.OrganizationId":org["Altra Catena"],"Input.Name":"Intrusa","Input.IsActive":"true"})
_,html2,_ = sa.req("/Gyms"); check("palestra creata dalla direzione finisce nella sua catena", re.search(r"FitActive</td>\s*<td><b>Intrusa", html2) is not None)

# 4. Responsabile Alba
m = Client()
m.post("/Login", {"Email":"alba@fitactive.test","Password":pw_mgr})
m.post("/Account/Password", {"Current":pw_mgr,"New":"Alba2026xyz1","New2":"Alba2026xyz1"})
s,_,_ = m.req("/Gyms"); check("responsabile non gestisce palestre", s in (302,403), str(s))
s,html,_ = m.req("/"); check("responsabile vede solo la sua palestra", "FitActive Alba" in html and "FitActive Bra" not in html)
s,html,_ = m.post("/Users/Edit", {"Input.FullName":"Op Bra","Input.Email":"opbra@x.test","Input.Role":"operator","Input.GymId":gym["FitActive Bra"],"Input.IsActive":"true"})
check("responsabile non crea operatori in altre palestre", s == 200)
s,html,_ = m.post("/Users/Edit", {"Input.FullName":"Op Alba","Input.Email":"opalba@x.test","Input.Role":"operator","Input.GymId":gym["FitActive Alba"],"Input.IsActive":"true"})
check("responsabile crea operatore nella sua palestra", s == 302)
_,lst,_ = m.req("/Users"); pw_op = temp_pwd(lst)
check("responsabile vede solo utenti della sua palestra", "direzione@fitactive.test" not in lst and "opalba@x.test" in lst)

# 5. Operatore
o = Client(); o.post("/Login", {"Email":"opalba@x.test","Password":pw_op}); o.post("/Account/Password", {"Current":pw_op,"New":"Operatore2026","New2":"Operatore2026"})
s,_,_ = o.req("/Users"); check("operatore non gestisce utenti", s in (302,403), str(s))


# 5b. Passo 2: liste contatti
s,_,_ = o.req("/Lists"); check("operatore non vede le liste", s in (302,403), str(s))
s,_,loc = m.post("/OptOuts?handler=Add", {"Phone":"347 000 1111","Reason":"chiesto in reception"}, form_path="/OptOuts")
check("responsabile aggiunge numero alla lista STOP", s == 302, str(s))
header = ["NOME","COGNOME","Cell.","E-mail","Abbonamento","Data scadenza","Consenso Marketing","Note"]
rows = [
  ["GIULIA","ROSSI","333 123 4567","g@x.it","Annuale","2026-10-18","SI",""],     # valida
  ["Marco","Bianchi","+39 345 765 4321","","Mensile","09/10/2026","sì",""],       # valida
  ["anna","verdi","0039 328 111 2233","","","","X",""],                            # valida
  ["Paolo","Neri","347 999 0001","","","","NO",""],                                 # senza consenso
  ["Luca","Gialli","0173 123456","","","","SI",""],                                 # fisso
  ["Sara","Blu","12345","","","","SI",""],                                          # non valido
  ["Giulia","Rossi","3331234567","","","","1",""],                                  # doppione
  ["","Senza","3401112233","","","","SI",""],                                       # senza nome
  ["Piero","Stop","3470001111","","","","SI",""],                                   # in lista STOP
  ["John","Smith","+44 7700 900123","","","","yes",""],                             # valida estera
  ["","","","","","","",""],                                                        # vuota, ignorata
]
mp = {"FirstName":0,"LastName":1,"Phone":2,"Email":3,"Membership":4,"ExpiresOn":5,"Consent":6,"ConsentDate":-1,"ConsentSource":-1,"Headers":{"FirstName":"NOME","Phone":"Cell.","Consent":"Consenso Marketing"}}
s, res = m.post_json("/Lists/New?handler=Import", {"GymId":gym["FitActive Alba"],"Name":"Scadenze ottobre","FileName":"scadenze.xlsx","Map":mp,"Rows":rows}, "/Lists/New")
check("import lista riuscito", s == 200 and "listId" in res, str(res))
exp = {"rowsRead":10,"valid":4,"noConsent":1,"badPhone":2,"duplicates":1,"optedOut":1,"noName":1}
check("conteggi import corretti", all(res.get(k) == v for k, v in exp.items()), str(res))
lid = res.get("listId")
s,html,_ = m.req(f"/Lists/Detail/{lid}")
check("numeri salvati in formato internazionale", all(x in html for x in ["+393331234567","+393457654321","+393281112233","+447700900123"]), str(re.findall(r"<code>([^<]+)</code>", html)))
check("nomi sistemati (GIULIA ROSSI -> Giulia Rossi)", "Giulia Rossi" in html and "Anna Verdi" in html)
check("scarti con motivo e riga Excel", all(x in html for x in ["Numero fisso","Manca il consenso","Ha chiesto di non essere contattato","Numero ripetuto","Manca il nome","<td>6</td>"]))
s,res2 = m.post_json("/Lists/New?handler=Import", {"GymId":gym["FitActive Bra"],"Name":"x","Map":mp,"Rows":rows}, "/Lists/New")
check("responsabile non importa in un'altra palestra", s == 400, str(s))
s,res3 = m.post_json("/Lists/New?handler=Import", {"GymId":gym["FitActive Alba"],"Name":"x","Map":dict(mp, Consent=-1),"Rows":rows}, "/Lists/New")
check("import rifiutato senza colonna del consenso", s == 400 and "consenso" in res3.get("error",""), str(res3))
s,html,_ = m.req("/Lists/New"); check("abbinamento colonne ricordato per la palestra", "Consenso Marketing" in html and "Cell." in html)
s,html,_ = d.req("/Lists"); check("direzione vede la lista della palestra", "Scadenze ottobre" in html)
s,html,_ = d.req("/OptOuts"); check("direzione vede la lista STOP della catena", "+393470001111" in html, str(s) + " " + str(re.findall(r"<code>([^<]+)</code>", html)) + html[html.find("<tbody>"):html.find("<tbody>")+300])
optid = re.search(r'handler=Remove&id=(\d+)', html) or re.search(r'id=(\d+)&handler=Remove', html)
s,_,loc = m.post(f"/OptOuts?handler=Remove&id={optid.group(1) if optid else 0}", {}, form_path="/OptOuts")
check("responsabile non toglie numeri dalla lista STOP", s == 403 or (s == 302 and "/Error/403" in (loc or "")), f"{s} {loc}")
s,_,_ = d.post(f"/OptOuts?handler=Remove&id={optid.group(1) if optid else 0}", {}, form_path="/OptOuts")
_,html,_ = d.req("/OptOuts"); check("direzione toglie un numero dalla lista STOP", s == 302 and "+393470001111" not in html, str(s))
s,_,_ = m.post(f"/Lists/Detail/{lid}?handler=Delete", {}, form_path=f"/Lists/Detail/{lid}")
s2,_,_ = m.req(f"/Lists/Detail/{lid}"); check("lista eliminata", s == 302 and s2 == 404, f"{s} {s2}")

# 6. Blocco dopo 5 tentativi sbagliati
x = Client()
for i in range(5): x.post("/Login", {"Email":"altra@altra.test","Password":"sbagliata123"})
s,html,_ = x.post("/Login", {"Email":"altra@altra.test","Password":pw_oth})
check("blocco dopo 5 tentativi sbagliati", "Troppi tentativi" in html)

# 7. Catena sospesa: i suoi utenti non entrano
sa.post(f"/Orgs/Edit/{org['FitActive']}", {"Input.Id":org["FitActive"],"Input.Name":"FitActive","Input.Slug":"fitactive","Input.PrimaryColor":"#F6931E","Input.IsActive":"false"}, form_path=f"/Orgs/Edit/{org['FitActive']}")
y = Client(); s,html,_ = y.post("/Login", {"Email":"alba@fitactive.test","Password":"Alba2026xyz1"})
check("catena sospesa blocca l'accesso", "disattivato" in html)

# 8. Tick
s,_,_ = Client().req("/jobs/tick?token=sbagliato"); check("tick rifiuta chiave sbagliata", s == 404)
s,body,_ = Client().req("/jobs/tick?token=" + tick.group(1)); check("tick con chiave giusta", s == 200 and '"ok":true' in body)

for n, ok, info in results:
    print(("OK  " if ok else "NO  ") + n + ("" if ok else "  -> " + info[:200]))
print(f"{sum(r[1] for r in results)}/{len(results)} superati")
sys.exit(0 if all(r[1] for r in results) else 1)
