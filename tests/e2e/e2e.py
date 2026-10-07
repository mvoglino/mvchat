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
            return r.status, html_lib.unescape(r.read().decode(errors="replace")), r.headers.get("Location")
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
    def post_multipart(self, path, fields, files, form_path=None):
        import uuid
        b = uuid.uuid4().hex; parts = []
        fields = dict(fields); fields["__RequestVerificationToken"] = self.token(form_path or path)
        for k, v in fields.items():
            parts.append(f'--{b}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode())
        for k, (fname, ctype, data) in files.items():
            parts.append(f'--{b}\r\nContent-Disposition: form-data; name="{k}"; filename="{fname}"\r\nContent-Type: {ctype}\r\n\r\n'.encode() + data + b"\r\n")
        body = b"".join(parts) + f"--{b}--\r\n".encode()
        req = urllib.request.Request(BASE + path, data=body, headers={"Content-Type": f"multipart/form-data; boundary={b}"})
        try:
            r = self.op.open(req); return r.status, html_lib.unescape(r.read().decode()), r.headers.get("Location")
        except urllib.error.HTTPError as e:
            return e.code, html_lib.unescape(e.read().decode(errors="ignore")), e.headers.get("Location")
    def post(self, path, data, form_path=None):
        data = dict(data); data["__RequestVerificationToken"] = self.token(form_path or path)
        return self.req(path, data)

def temp_pwd(html):
    m = re.search(r"<code>([A-Za-z0-9]{12})</code>", html); return m.group(1) if m else None

# 1. Install
INSTALL_CODE = os.environ.get("MVCHAT_INSTALL_CODE", "codice-di-prova")
c = Client()
s, html, loc = c.req("/Install", {"Input.InstallCode":"sbagliato","Input.DbHost":os.environ.get("DB_HOST","127.0.0.1"),"Input.DbPort":"3306","Input.DbName":"mvchat","Input.DbUser":"mv","Input.DbPassword":"Pwd12345!",
    "Input.ProductName":"mvchat","Input.AdminName":"X","Input.AdminEmail":"x@x.test","Input.AdminPassword":"Admin12345x","Input.AdminPassword2":"Admin12345x"})
check("installazione rifiutata senza il codice del file", "Codice di installazione sbagliato" in html and "Installazione completata" not in html)
s, html, loc = c.req("/Install", {"Input.InstallCode":INSTALL_CODE,"Input.DbHost":os.environ.get("DB_HOST","127.0.0.1"),"Input.DbPort":"3306","Input.DbName":"mvchat","Input.DbUser":"mv","Input.DbPassword":"Pwd12345!",
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
check("crea gruppo FitActive", mk_org("FitActive"))
check("crea gruppo Altra", mk_org("Altra Gruppo"))
s,html,_ = sa.req("/Orgs"); orgs = dict((n, int(i)) for i, n in re.findall(r'href="/Orgs/Edit/(\d+)".*?', html) and re.findall(r'/Orgs/Edit/(\d+)', html) and [])
ids = [int(x) for x in re.findall(r'/Orgs/Edit/(\d+)', html)]
names = re.findall(r"<td><b>([^<]+)</b><div class=\"muted\">", html)
org = dict(zip(names, ids)); check("elenco gruppi", "FitActive" in org and "Altra Gruppo" in org, str(org))
def mk_gym(orgname, name):
    s,_,_ = sa.post("/Gyms/Edit", {"Input.OrganizationId":org[orgname],"Input.Name":name,"Input.City":"X","Input.IsActive":"true"}); return s == 302
check("crea attività", mk_gym("FitActive","FitActive Alba") and mk_gym("FitActive","FitActive Bra") and mk_gym("Altra Gruppo","Altra Attività"))
s,html,_ = sa.req("/Gyms")
gids = re.findall(r'/Gyms/Edit/(\d+)', html); gnames = re.findall(r"<td><b>([^<]+)</b><div class=\"muted\">", html)
gym = dict(zip(gnames, map(int, gids))); check("elenco attività superadmin = 3", len(gym) == 3, str(gym))

def mk_user(client, email, role, orgid="", gymid="", form="/Users/Edit"):
    s, html, loc = client.post("/Users/Edit", {"Input.FullName":email.split("@")[0],"Input.Email":email,"Input.Role":role,"Input.OrganizationId":orgid,"Input.GymId":gymid,"Input.IsActive":"true"})
    if s != 302: return None, html
    _, lst, _ = client.req("/Users"); return temp_pwd(lst), lst
pw_dir,_ = mk_user(sa, "direzione@fitactive.test", "orgadmin", org["FitActive"])
pw_mgr,_ = mk_user(sa, "alba@fitactive.test", "manager", "", gym["FitActive Alba"])
pw_oth,_ = mk_user(sa, "altra@altra.test", "manager", "", gym["Altra Attività"])
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
check("direzione vede solo le sue attività", "FitActive Alba" in html and "FitActive Bra" in html and "Altra Attività" not in html)
s,_,_ = d.req(f"/Gyms/Edit/{gym['Altra Attività']}"); check("direzione non apre attività di altra gruppo", s == 404, str(s))
s,_,_ = d.req("/Orgs"); check("direzione non vede i gruppi", s in (302, 403), str(s))
s,html,_ = d.post("/Users/Edit", {"Input.FullName":"x","Input.Email":"hack@x.test","Input.Role":"superadmin","Input.IsActive":"true"})
check("direzione non può creare superadmin", s == 200 and "Non puoi assegnare" in html)
s,html,_ = d.post("/Users/Edit", {"Input.FullName":"x","Input.Email":"op@x.test","Input.Role":"operator","Input.GymId":gym["Altra Attività"],"Input.IsActive":"true"})
check("direzione non assegna attività di altre gruppi", s == 200 and "Scegli l'attività" in html)
s,html,_ = d.post("/Gyms/Edit", {"Input.OrganizationId":org["Altra Gruppo"],"Input.Name":"Intrusa","Input.IsActive":"true"})
_,html2,_ = sa.req("/Gyms"); check("attività creata dalla direzione finisce nella sua gruppo", re.search(r"FitActive</td>\s*<td><b>Intrusa", html2) is not None)

# 4. Responsabile Alba
m = Client()
m.post("/Login", {"Email":"alba@fitactive.test","Password":pw_mgr})
m.post("/Account/Password", {"Current":pw_mgr,"New":"Alba2026xyz1","New2":"Alba2026xyz1"})
s,_,_ = m.req("/Gyms"); check("responsabile non gestisce attività", s in (302,403), str(s))
s,html,_ = m.req("/"); check("responsabile vede solo la sua attività", "FitActive Alba" in html and "FitActive Bra" not in html)
s,html,_ = m.post("/Users/Edit", {"Input.FullName":"Op Bra","Input.Email":"opbra@x.test","Input.Role":"operator","Input.GymId":gym["FitActive Bra"],"Input.IsActive":"true"})
check("responsabile non crea operatori in altre attività", s == 200)
s,html,_ = m.post("/Users/Edit", {"Input.FullName":"Op Alba","Input.Email":"opalba@x.test","Input.Role":"operator","Input.GymId":gym["FitActive Alba"],"Input.IsActive":"true"})
check("responsabile crea operatore nella sua attività", s == 302)
_,lst,_ = m.req("/Users"); pw_op = temp_pwd(lst)
check("responsabile vede solo utenti della sua attività", "direzione@fitactive.test" not in lst and "opalba@x.test" in lst)

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
check("responsabile non importa in un'altra attività", s == 400, str(s))
s,res3 = m.post_json("/Lists/New?handler=Import", {"GymId":gym["FitActive Alba"],"Name":"x","Map":dict(mp, Consent=-1),"Rows":rows}, "/Lists/New")
check("import rifiutato senza colonna del consenso", s == 400 and "consenso" in res3.get("error",""), str(res3))
s,html,_ = m.req("/Lists/New"); check("abbinamento colonne ricordato per l'attività", "Consenso Marketing" in html and "Cell." in html)
s,html,_ = d.req("/Lists"); check("direzione vede la lista dell'attività", "Scadenze ottobre" in html)
s,html,_ = d.req("/OptOuts"); check("direzione vede la lista STOP del gruppo", "+393470001111" in html, str(s) + " " + str(re.findall(r"<code>([^<]+)</code>", html)) + html[html.find("<tbody>"):html.find("<tbody>")+300])
optid = re.search(r'handler=Remove&id=(\d+)', html) or re.search(r'id=(\d+)&handler=Remove', html)
s,_,loc = m.post(f"/OptOuts?handler=Remove&id={optid.group(1) if optid else 0}", {}, form_path="/OptOuts")
check("responsabile non toglie numeri dalla lista STOP", s == 403 or (s == 302 and "/Error/403" in (loc or "")), f"{s} {loc}")
s,_,_ = d.post(f"/OptOuts?handler=Remove&id={optid.group(1) if optid else 0}", {}, form_path="/OptOuts")
_,html,_ = d.req("/OptOuts"); check("direzione toglie un numero dalla lista STOP", s == 302 and "+393470001111" not in html, str(s))
s,_,_ = m.post(f"/Lists/Detail/{lid}?handler=Delete", {}, form_path=f"/Lists/Detail/{lid}")
s2,_,_ = m.req(f"/Lists/Detail/{lid}"); check("lista eliminata", s == 302 and s2 == 404, f"{s} {s2}")


# 5c. Passo 3: scheda attività, offerte, modelli di obiettivo
s,_,loc = m.req("/Sedi"); check("responsabile va dritto alla scheda della sua attività", s == 302 and loc == f"/Sedi/Edit/{gym['FitActive Alba']}", f"{s} {loc}")
s,_,loc = m.post(f"/Sedi/Edit/{gym['FitActive Alba']}", {"Input.OpeningHours":"Lun-Ven 7-22, Sab 9-19","Input.Services":"Sala pesi, corsi, sauna","Input.Classes":"Pilates mar 18:30","Input.HowToReach":"Parcheggio gratuito","Input.ExtraInfo":"Serve il certificato medico","Input.AssistantName":"assistente virtuale","Input.Formality":"tu"})
check("responsabile salva la scheda", s == 302, str(s))
s,_,_ = m.req(f"/Sedi/Edit/{gym['FitActive Bra']}"); check("responsabile non apre la scheda di un'altra attività", s == 404, str(s))
s,_,_ = m.post("/Offerte/Edit", {"Input.GymId":gym["FitActive Alba"],"Input.Title":"Rinnovo con 2 mesi omaggio","Input.Description":"14 mesi al prezzo di 12","Input.Price":"399","Input.FullPrice":"465","Input.PriceNote":"una tantum","Input.MaxExtraDiscountPct":"10","Input.ValidTo":"2099-10-31","Input.IsActive":"true"})
check("responsabile crea un'offerta con prezzo", s == 302, str(s))
s,html,_ = m.post("/Offerte/Edit", {"Input.GymId":gym["FitActive Alba"],"Input.Title":"x","Input.Price":"abc","Input.ValidFrom":"2026-12-01","Input.ValidTo":"2026-11-01","Input.IsActive":"true"})
check("offerta con prezzo e date sbagliati rifiutata", s == 200 and "Scrivi un importo" in html and "prima dell'inizio" in html)
s,html,_ = m.post("/Offerte/Edit", {"Input.GymId":gym["FitActive Bra"],"Input.Title":"Intrusa","Input.Price":"1","Input.IsActive":"true"})
check("responsabile non crea offerte per un'altra attività", s == 200 and "Scegli l'attività" in html)
s,_,_ = d.post("/Offerte/Edit", {"Input.GymId":gym["FitActive Bra"],"Input.Title":"Annuale Bra","Input.Price":"1.200","Input.IsActive":"true"})
_,html,_ = d.req("/Offerte"); check("direzione crea offerta per Bra (1.200 = milleduecento)", s == 302 and "1.200 €" in html and "399 €" in html, str(s))
_,html,_ = m.req("/Offerte"); check("responsabile vede solo le offerte della sua attività", "399 €" in html and "Annuale Bra" not in html)
offer_alba = re.search(r'/Offerte/Edit/(\d+)', html).group(1)
s,html,_ = m.req("/Modelli"); rinnovo = re.search(r'/Modelli/Anteprima\?model=(\d+)', html).group(1)
check("modelli standard presenti", all(x in html for x in ["Rinnovo abbonamento","Adesione a una promo","Recupero inattivi","Porta un amico","Sondaggio soddisfazione"]))
s,html,_ = m.req(f"/Modelli/Anteprima?model={rinnovo}&gym={gym['FitActive Alba']}&offer={offer_alba}&nome=Giulia&abbonamento=Annuale&scadenza=2026-10-18")
pr = html[html.find('id="prompt"'):]
check("istruzioni AI con prezzo dell'offerta e scheda attività", all(x in pr for x in ["399 €","invece di 465 €","Lun-Ven 7-22","al massimo il 10%","Rinnovo abbonamento","18 ottobre 2026"]), pr[:400])
check("istruzioni AI senza dati di altre attività", "1.200" not in pr and "Annuale Bra" not in html)
check("primo messaggio personalizzato", "Ciao Giulia, il tuo abbonamento Annuale in FitActive Alba scade il 18/10/2026" in html)
s,html,_ = m.req(f"/Modelli/Anteprima?model={rinnovo}&gym={gym['FitActive Bra']}")
check("anteprima non usa attività fuori dal perimetro", "FitActive Bra" not in html[html.find('id="prompt"'):] if 'id="prompt"' in html else True)
s,_,loc = m.req("/Modelli/Edit"); check("responsabile non crea modelli", s == 403 or "/Error/403" in (loc or ""), f"{s} {loc}")
s,_,_ = d.post("/Modelli/Edit", {"Input.Name":"Prova corso Pilates","Input.Success":"Il cliente prenota una lezione di prova","Input.Instructions":"Proponi una lezione di prova gratuita di Pilates.","Input.MaxAiMessages":"6","Input.NeedsOffer":"false","Input.IsActive":"true"})
_,html,_ = m.req("/Modelli"); check("modello del gruppo creato dalla direzione e visibile in gruppo", s == 302 and "Prova corso Pilates" in html, str(s))
oth = Client(); oth.post("/Login", {"Email":"altra@altra.test","Password":pw_oth}); oth.post("/Account/Password", {"Current":pw_oth,"New":"Altra2026xyz1","New2":"Altra2026xyz1"})
_,html,_ = oth.req("/Modelli"); check("modello di un gruppo invisibile alle altre", "Prova corso Pilates" not in html and "Rinnovo abbonamento" in html)
s,_,_ = d.req(f"/Modelli/Edit/{rinnovo}"); check("direzione non modifica i modelli standard", s == 404, str(s))
s,_,_ = sa.req(f"/Modelli/Edit/{rinnovo}"); check("MVitalia modifica i modelli standard", s == 200, str(s))


# 5c-bis. Gruppi di qualsiasi settore: tipo di attività, dati, logo
s,_,loc = sa.post("/Orgs/Edit", {"Input.Name":"Hotel Langhe","Input.Slug":"","Input.Sector":"hotel","Input.IsActive":"true"})
hotel = re.search(r"/Gruppo/(\d+)$", loc or ""); hotel = hotel.group(1) if hotel else "0"
_,html,_ = sa.req("/Orgs"); check("MVitalia abilita un gruppo non palestra (hotel)", s == 302 and "Hotel Langhe" in html and "Hotel / struttura ricettiva" in html, f"{s} {loc}")
png = bytes.fromhex("89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4890000000d4944415478da63f8cfc0f01f0005000201a0f3b5c80000000049454e44ae426082")
s,html,_ = d.post_multipart("/Gruppo", {"Input.Name":"FitActive","Input.PrimaryColor":"#F6931E","Input.Sector":"hotel","Input.LegalName":"FitActive S.r.l.","Input.VatNumber":"01234567890",
    "Input.Website":"www.fitactive.it","Input.Phone":"0173 000000","Input.Description":"Catena di palestre aperte tutti i giorni con prezzi accessibili."}, {"Logo":("logo.png","image/png",png)})
_,html,_ = d.req("/Gruppo")
check("la direzione cura nome, dati e logo della sua gruppo", s == 302 and "FitActive S.r.l." in html and "Catena di palestre aperte" in html and 'src="/logo/' in html, f"{s}")
check("la direzione non cambia il tipo di attività", "Palestra / centro fitness" in html)
s,body,_ = Client().req(f"/logo/{org['FitActive']}"); check("logo caricato servito come immagine", s == 200, str(s))
_,html,_ = d.req("/"); check("logo del gruppo nel menu", f'src="/logo/{org["FitActive"]}?v=' in html)
s,html,_ = d.post_multipart("/Gruppo", {"Input.Name":"FitActive","Input.PrimaryColor":"#F6931E"}, {"Logo":("finto.png","image/png",b"<script>alert(1)</script>")})
check("logo che non è un'immagine rifiutato", s == 200 and "PNG, JPG o WebP" in html, str(s))
s,_,_ = d.req(f"/Gruppo/{hotel}"); check("la direzione non apre i dati di un altro gruppo", s == 404, str(s))
s,_,_ = m.req("/Gruppo"); check("il responsabile di attività non modifica il gruppo", s in (302,403), str(s))
_,html,_ = m.req(f"/Modelli/Anteprima?model={rinnovo}&gym={gym['FitActive Alba']}&offer={offer_alba}")
pr = html[html.find('id="prompt"'):]
check("l'assistente conosce attività e gruppo: tipo di attività e chi siamo", "palestra / centro fitness" in pr and "a un iscritto" in pr and "## Chi siamo: FitActive Alba" in pr and "Fa parte del gruppo FitActive. Catena di palestre aperte" in pr, pr[:400])
s,_,_ = sa.post("/Gyms/Edit", {"Input.OrganizationId":hotel,"Input.Name":"Hotel Langhe Alba","Input.City":"Alba","Input.IsActive":"true"})
_,html,_ = sa.req("/Gyms"); hgym = re.search(r'/Gyms/Edit/(\d+)"[^>]*>[^<]*</a>\s*</td>\s*</tr>', html)
hg = [g for g in re.findall(r"/Sedi/Edit/(\d+)", sa.req("/Sedi")[1])]
_,html,_ = sa.req(f"/Modelli/Anteprima?model={rinnovo}&gym={hg[-1] if hg else 0}&abbonamento=Weekend")
pr = html[html.find('id="prompt"'):]
check("per un hotel l'assistente parla di ospiti e soggiorni", "a un ospite" in pr and "Soggiorno: Weekend" in pr, pr[:300])

# 5c-ter. Gerarchia: MVitalia → gruppi → attività (anche singole) → amministratori e operatori
s,_,loc = sa.post("/Gyms/Edit", {"Input.OrganizationId":"","Input.Sector":"benessere","Input.Name":"Centro Benessere Aurora","Input.City":"Bra","Input.IsActive":"true"})
aur = re.search(r"/Attivita/(\d+)$", loc or ""); aur = aur.group(1) if aur else "0"
_,orgs_html,_ = sa.req("/Orgs"); _,gyms_html,_ = sa.req("/Gyms")
check("MVitalia avvia un'attività singola (senza gruppo)", s == 302 and "Centro Benessere Aurora" in gyms_html and "— singola —" in gyms_html and "Centro Benessere Aurora" not in orgs_html, f"{s} {loc}")
_,html,_ = sa.req("/Users/Edit")
sel = re.search(r'<select[^>]*id="Input_OrganizationId"[^>]*>(.*?)</select>', html, re.S)
check("l'amministratore di gruppo si assegna solo nei gruppi", sel is not None and "FitActive" in sel.group(1) and "Centro Benessere Aurora" not in sel.group(1), html[html.find("Input_OrganizationId")-100:][:600])
pw_aur,_ = mk_user(sa, "aurora@aurora.test", "manager", "", int(aur))
aurc = Client(); aurc.post("/Login", {"Email":"aurora@aurora.test","Password":pw_aur}); aurc.post("/Account/Password", {"Current":pw_aur,"New":"Aurora2026xyz","New2":"Aurora2026xyz"})
_,html,_ = aurc.req("/")
check("l'amministratore dell'attività singola entra e vede la sua attività", "Amministratore attività" in html and "La mia attività" in html and "Centro Benessere Aurora" in html, html[:200])
s,html,_ = aurc.post("/Users/Edit", {"Input.FullName":"Op Aurora","Input.Email":"op@aurora.test","Input.Role":"operator","Input.GymId":aur,"Input.IsActive":"true"})
check("l'amministratore di attività crea i suoi operatori", s == 302, str(s))
s,html,_ = aurc.post("/Users/Edit", {"Input.FullName":"Capo","Input.Email":"capo@aurora.test","Input.Role":"orgadmin","Input.GymId":aur,"Input.IsActive":"true"})
check("l'amministratore di attività non crea amministratori di gruppo", s == 200 and "Non puoi assegnare questo ruolo" in html, str(s))
s,html,_ = aurc.post_multipart(f"/Attivita/{aur}", {"Input.Name":"Centro Benessere Aurora","Input.Sector":"palestra","Input.LegalName":"Aurora S.n.c.","Input.Description":"Centro estetico con spa e massaggi."}, {"Logo":("aurora.png","image/png",png)})
_,html,_ = aurc.req("/Attivita")
check("l'amministratore cura dati e logo della sua attività", s == 302 and "Aurora S.n.c." in html and f'/logo/a/{aur}?v=' in html, str(s))
check("l'amministratore di attività non cambia il tipo di attività", "Centro estetico / benessere / spa" in html)
s,_,_ = Client().req(f"/logo/a/{aur}"); check("logo dell'attività servito come immagine", s == 200, str(s))
_,html,_ = aurc.req("/Users"); check("logo dell'attività nel menu dei suoi utenti", f'src="/logo/a/{aur}?v=' in html)
s,_,_ = aurc.req(f"/Attivita/{gym['FitActive Alba']}"); check("un'attività non vede i dati di un'altra", s == 404, str(s))
s,_,_ = aurc.req("/Gruppo"); check("un'attività singola non ha pagina di gruppo", s in (302,403), str(s))
s,_,_ = sa.req(f"/Orgs/Edit/{org['FitActive']}"); s2,_,_ = sa.req("/Orgs/Edit/999999")
_,html,_ = aurc.req(f"/Modelli/Anteprima?model={rinnovo}&gym={aur}&abbonamento=Pacchetto%20viso")
pr = html[html.find('id="prompt"'):]
check("per un centro benessere l'assistente parla di clienti e trattamenti", "(centro estetico / benessere / spa)" in pr and "a un cliente" in pr and "Trattamento: Pacchetto viso" in pr and "Centro estetico con spa" in pr and "Fa parte del gruppo" not in pr, pr[:400])
_,html,_ = m.req("/Attivita"); check("attività di un gruppo: dati propri, gruppo indicato", "Fa parte del gruppo FitActive" in html)

# 5d. Passo 4: WhatsApp (numero simulato e numero "Meta" verso un finto server)
import hmac, hashlib, json as _json, sys as _sys, os as _os
_sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
import fake_meta
fake = fake_meta.start(5079)
s,_,_ = sa.post("/Impostazioni/WhatsApp", {"AppId":"123456","AppSecret":"testsecret","GraphVersion":"v23.0","GraphBaseUrl":"http://127.0.0.1:5079"})
_,html,_ = sa.req("/Impostazioni/WhatsApp"); vt = re.search(r'id="verifyToken">([a-f0-9]+)<', html)
check("impostazioni Meta salvate e token di verifica mostrato", s == 302 and vt is not None and "testsecret" not in html, str(s))
s,body,_ = Client().req(f"/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={vt.group(1)}&hub.challenge=ok123")
s2,_,_ = Client().req("/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=sbagliato&hub.challenge=ok123")
check("verifica webhook di Meta", s == 200 and body == "ok123" and s2 == 403, f"{s} {body} {s2}")
s,_,loc = m.post(f"/WhatsApp/Numero/{gym['FitActive Alba']}?handler=Save", {"Mode":"simulato","DisplayPhone":"0173 123456"}, form_path=f"/WhatsApp/Numero/{gym['FitActive Alba']}")
check("il responsabile non collega numeri", s in (400, 403) or "/Error/403" in (loc or ""), f"{s} {loc}")
alba_wa = f"/WhatsApp/Numero/{gym['FitActive Alba']}"
s,_,_ = d.post(alba_wa + "?handler=Save", {"Mode":"simulato","DisplayPhone":"0173 123456","DisplayName":"FitActive Alba"}, form_path=alba_wa)
_,html,_ = m.req(alba_wa); check("numero simulato collegato dalla direzione", s == 302 and "0173 123456" in html and "Simulato" in html, str(s))
tpl_a = f"/WhatsApp/Template/{gym['FitActive Alba']}"
s,html,_ = m.post(tpl_a + "?handler=Create", {"Name":"x","Category":"MARKETING","Body":"{{nome}} ciao, scade il {{scadenza}}"}, form_path=tpl_a)
check("template con errori rifiutato con spiegazione", s == 200 and "non può iniziare con un segnaposto" in html and "almeno 3 lettere" in html)
body_tpl = "Ciao {{nome}}, il tuo abbonamento {{abbonamento}} in {{palestra}} scade il {{scadenza}}. Vuoi conoscere l'offerta?"
s,_,_ = m.post(tpl_a + "?handler=Create", {"Name":"Rinnovo Ottobre","Category":"MARKETING","Body":body_tpl}, form_path=tpl_a)
_,html,_ = m.req(tpl_a); check("template creato e approvato (simulato)", s == 302 and "rinnovo_ottobre" in html and "approvato" in html, str(s))
tid = re.search(r'<option value="(\d+)">rinnovo_ottobre</option>', m.req(alba_wa)[1]).group(1)
s,_,_ = m.post(alba_wa + "?handler=Test", {"TemplateId":tid,"To":"333 999 8877","Nome":"Luca"}, form_path=alba_wa)
_,html,_ = m.req(alba_wa); check("messaggio di prova inviato (simulato)", s == 302 and "Ciao Luca, il tuo abbonamento Annuale in FitActive Alba" in html and "+393339998877" in html, str(s))
s,_,_ = m.post(alba_wa + "?handler=Simulate", {"To":"333 999 8877","Text":"Stop!"}, form_path=alba_wa)
_,html,_ = m.req(alba_wa); _,stop,_ = d.req("/OptOuts")
check("chi scrive STOP finisce nella lista STOP e riceve conferma", "+393339998877" in stop and "Ha scritto STOP" in stop and "non riceverai più messaggi" in html)
s,html,_ = m.post(alba_wa + "?handler=Test", {"TemplateId":tid,"To":"333 999 8877"}, form_path=alba_wa)
check("nessun invio a chi è nella lista STOP", s == 200 and "lista STOP" in html)
bra_wa = f"/WhatsApp/Numero/{gym['FitActive Bra']}"
s,_,_ = d.post(bra_wa + "?handler=Save", {"Mode":"meta","DisplayPhone":"0172 000000","DisplayName":"FitActive Bra","PhoneNumberId":"111","WabaId":"222","AccessToken":"good-token"}, form_path=bra_wa)
_,html,_ = d.req(bra_wa)
check("numero Meta collegato e controllato (qualità letta da Meta)", s == 302 and "GREEN" in html and "TIER_1K" in html, str(s))
check("chiave di accesso mai mostrata in pagina", "good-token" not in html)
tpl_b = f"/WhatsApp/Template/{gym['FitActive Bra']}"
s,_,_ = d.post(tpl_b + "?handler=Create", {"Name":"rinnovo_bra","Category":"MARKETING","Body":body_tpl}, form_path=tpl_b)
sent = [r for r in fake_meta.REQUESTS if r["method"] == "POST" and r["path"].endswith("/222/message_templates")]
comp = sent[-1]["body"]["components"][0] if sent else {}
check("template inviato a Meta con {{1}} e esempi", bool(sent) and comp.get("text","").startswith("Ciao {{1}}, il tuo abbonamento {{2}} in {{3}} scade il {{4}}") and comp.get("example",{}).get("body_text",[[None]])[0][0] == "Giulia", str(comp)[:300])
_,html,_ = d.req(tpl_b); check("template in revisione dopo l'invio", "in revisione" in html)
s,_,_ = d.post(tpl_b + "?handler=Refresh", {}, form_path=tpl_b)
_,html,_ = d.req(tpl_b); check("stato aggiornato da Meta: approvato", "approvato" in html)
tidb = re.search(r'<option value="(\d+)">rinnovo_bra</option>', d.req(bra_wa)[1]).group(1)
s,_,_ = d.post(bra_wa + "?handler=Test", {"TemplateId":tidb,"To":"+39 347 555 1212","Nome":"Anna"}, form_path=bra_wa)
msg = [r for r in fake_meta.REQUESTS if r["method"] == "POST" and r["path"].endswith("/111/messages")]
b = msg[-1]["body"] if msg else {}
check("template inviato a Meta nel formato giusto", b.get("to") == "393475551212" and b.get("template",{}).get("name") == "rinnovo_bra" and b["template"]["components"][0]["parameters"][0]["text"] == "Anna", str(b)[:300])
wamid = None
_,html,_ = d.req(bra_wa); check("messaggio registrato come inviato", "Inviato" in html)
wamid = f"wamid.TEST{fake_meta.REQUESTS.index(msg[-1]) + 1}"
payload = {"object":"whatsapp_business_account","entry":[{"id":"222","changes":[{"field":"messages","value":{"messaging_product":"whatsapp","metadata":{"phone_number_id":"111"},
  "statuses":[{"id":wamid,"status":"read","timestamp":"1","recipient_id":"393475551212"}],
  "messages":[{"from":"393475551212","id":"wamid.IN1","timestamp":"1","type":"text","text":{"body":"Quanto costa?"}}]}}]}]}
raw = _json.dumps(payload).encode()
sig = "sha256=" + hmac.new(b"testsecret", raw, hashlib.sha256).hexdigest()
def post_raw(sig_header):
    req = urllib.request.Request(BASE + "/webhooks/whatsapp", data=raw, headers={"Content-Type":"application/json","X-Hub-Signature-256":sig_header})
    try: return urllib.request.urlopen(req).status
    except urllib.error.HTTPError as e: return e.code
check("webhook con firma sbagliata rifiutato", post_raw("sha256=" + "0"*64) == 401)
check("webhook con firma giusta accettato", post_raw(sig) == 200)
post_raw(sig)  # Meta a volte ripete: non deve duplicare
_,html,_ = d.req(bra_wa)
check("stato 'letto' e messaggio del cliente registrati una volta sola", "Letto" in html and html.count("Quanto costa?") == 1)
tev = {"object":"whatsapp_business_account","entry":[{"id":"222","changes":[{"field":"message_template_status_update","value":{"event":"REJECTED","message_template_id":"tpl123","message_template_name":"rinnovo_bra","reason":"INVALID_FORMAT"}}]}]}
raw = _json.dumps(tev).encode(); sig = "sha256=" + hmac.new(b"testsecret", raw, hashlib.sha256).hexdigest(); post_raw(sig)
_,html,_ = d.req(tpl_b); check("avviso di Meta sul template registrato", "rifiutato" in html and "INVALID_FORMAT" in html)
s,_,_ = oth.req(alba_wa); check("un altro gruppo non vede il WhatsApp dell'attività", s == 404, str(s))
fake.shutdown()

# 5e. Passo 5: assistente AI (verso un finto fornitore AI)
import fake_ai, time as _time
fai = fake_ai.start(5080)
def ai_settings(provider, akey="", okey=""):
    return sa.post("/Impostazioni/AI", {"Provider":provider,
        "Anthropic.Key":akey,"Anthropic.Model":"claude-haiku-4-5-20251001","Anthropic.BaseUrl":"http://127.0.0.1:5080","Anthropic.InputPrice":"1","Anthropic.OutputPrice":"5","Anthropic.CacheReadPrice":"0,10",
        "OpenAi.Key":okey,"OpenAi.Model":"gpt-5-mini","OpenAi.BaseUrl":"http://127.0.0.1:5080","OpenAi.InputPrice":"0.25","OpenAi.OutputPrice":"2","OpenAi.CacheReadPrice":"0.025"})
s,_,loc = m.req("/Impostazioni/AI"); check("solo MVitalia apre le impostazioni AI", s == 403 or "/Error/403" in (loc or "") or s == 302, f"{s} {loc}")
s,_,_ = ai_settings("anthropic", akey="sk-ant-test")
_,html,_ = sa.req("/Impostazioni/AI"); check("impostazioni AI salvate, chiave mai mostrata", s == 302 and "sk-ant-test" not in html and "già inserita" in html, str(s))
s,_,_ = sa.post("/Impostazioni/AI?handler=Test", {}, form_path="/Impostazioni/AI")
_,html,_ = sa.req("/Impostazioni/AI"); check("prova del collegamento AI riuscita", "Collegamento riuscito con anthropic" in html, html[html.find('flash'):][:200])

def start_conv(client, phone, name="Giulia", offer=True):
    data = {"Gym":gym["FitActive Alba"],"ModelId":rinnovo,"OfferId":offer_alba if offer else "","TemplateId":"","Phone":phone,"Name":name,"Membership":"Annuale","ExpiresOn":"2026-10-18"}
    s,html,loc = client.post("/Assistente/Prova", data, form_path=f"/Assistente/Prova?gym={gym['FitActive Alba']}")
    mm = re.search(r"/Conversazioni/(\d+)$", loc or "")
    return (mm.group(1) if mm else None), s, html
def state(client, cid):
    s,body,_ = client.req(f"/Conversazioni/{cid}?handler=State")
    return _json.loads(body) if s == 200 else {}
def say(client, cid, text, wait=True):
    before = state(client, cid).get("last", 0)
    client.post(f"/Conversazioni/{cid}?handler=Simulate", {"Text":text}, form_path=f"/Conversazioni/{cid}")
    if not wait: return
    for _ in range(60):  # l'assistente risponde in sottofondo
        st = state(client, cid)
        if not st.get("writing") and st.get("last", 0) > before: break
        _time.sleep(0.2)
    _time.sleep(0.3)
def page(client, cid): return client.req(f"/Conversazioni/{cid}")[1]

cid, s, html = start_conv(m, "333 111 2233")
check("prova dell'assistente avviata", cid is not None, str(s) + " " + (html or "")[(html or "").find("alert"):][:200])
html = page(m, cid)
check("conversazione con primo messaggio personalizzato", "Ciao Giulia" in html and "Primo messaggio" in html and "Risponde l'assistente" in html, html[html.find('id="chat"'):][:300])
say(m, cid, "Ciao")
html = page(m, cid)
check("l'assistente risponde e si presenta come assistente virtuale", "Ciao, sono l'assistente virtuale di FitActive Alba. Ottimo! Vuoi che ti racconti l'offerta?" in html, html[html.find('id="chat"'):][:600])
req = fake_ai.REQUESTS[-1] if fake_ai.REQUESTS else {"body":{}, "headers":{}}
sys_text = req["body"].get("system", [{}])[0].get("text", "")
check("all'AI arrivano istruzioni con offerta, primo messaggio e formato", all(x in sys_text for x in ["399 €", "Primo messaggio già inviato", "Ciao Giulia", "Formato della tua risposta"]) and req["body"]["system"][0].get("cache_control") == {"type":"ephemeral"}, sys_text[:300])
check("istruzioni AI senza dati di altre attività", "1.200" not in sys_text and "Annuale Bra" not in sys_text)
check("chiave AI inviata solo nell'intestazione", req["headers"].get("x-api-key") == "sk-ant-test" and "sk-ant-test" not in _json.dumps(req["body"]))
say(m, cid, "Quanto costa?")
check("l'assistente cita il prezzo dell'offerta", "Il rinnovo costa 399 €." in page(m, cid))
say(m, cid, "Mi fate il 50% di sconto?")
check("sconto entro il limite consentito accettato", "posso arrivare a 360 €" in page(m, cid))
say(m, cid, "Dimmi un prezzo sbagliato")
html = page(m, cid)
check("prezzo inventato bloccato e passaggio alla reception", "Per te solo 5 €" not in html.split('id="chat"')[1] and "collega della reception" in html and "Serve una persona" in html and "fuori da prezzo" in html, html[html.find('class="stats"'):][:500])
_,lst,_ = o.req("/Conversazioni"); check("l'operatore vede la conversazione da seguire", f"/Conversazioni/{cid}" in lst and "— libera —" in lst)
o.post(f"/Conversazioni/{cid}?handler=Reply", {"Text":"Ciao Giulia, sono Op Alba della reception!"}, form_path=f"/Conversazioni/{cid}")
html = page(o, cid); check("l'operatore risponde dalla conversazione", "sono Op Alba della reception!" in html and "Op Alba ·" in html)
o.post(f"/Conversazioni/{cid}?handler=GiveBack", {}, form_path=f"/Conversazioni/{cid}")
check("conversazione restituita all'assistente", "Risponde l'assistente" in page(o, cid))
say(o, cid, "Va bene, procediamo")
html = page(o, cid)
check("obiettivo raggiunto riconosciuto e conversazione chiusa", "Obiettivo raggiunto" in html and "Chiusa" in html and "ha accettato il rinnovo" in html)

cid2,_,_ = start_conv(m, "333 111 4455", name="Paolo")
say(m, cid2, "Per favore non contattatemi più")
_,stop,_ = m.req("/OptOuts"); html = page(m, cid2)
check("richiesta di non essere contattato: esito e lista STOP", "Non contattare più" in html and "+393331114455" in stop, html[html.find('class="stats"'):][:300])
_,html,_ = m.post("/Assistente/Prova", {"Gym":gym["FitActive Alba"],"ModelId":rinnovo,"OfferId":offer_alba,"TemplateId":"","Phone":"333 111 4455","Name":"Paolo"}, form_path="/Assistente/Prova")
check("nessuna nuova conversazione con chi è nella lista STOP", "lista STOP" in html)

cid3,_,_ = start_conv(m, "333 111 6677", name="Sara")
say(m, cid3, "Posso parlare con una persona della reception?")
check("cliente che chiede una persona passa alla reception", "Serve una persona" in page(m, cid3))
cid4,_,_ = start_conv(m, "333 111 8899", name="Luca")
say(m, cid4, "lento", wait=False)
_time.sleep(0.4)
check("in pagina si vede che l'assistente sta scrivendo", "L'assistente sta scrivendo" in page(m, cid4))
say(m, cid4, "Ciao di nuovo")
say(m, cid4, "errore ai")
html = page(m, cid4)
check("se l'AI non risponde il cliente riceve un messaggio di cortesia e passa alla reception", "collega della reception" in html and "Serve una persona" in html and "non ha risposto" in html)
m.post(f"/Conversazioni/{cid4}?handler=Outcome", {"Outcome":"obiettivo_raggiunto","Note":"rinnovato al banco"}, form_path=f"/Conversazioni/{cid4}")
html = page(m, cid4); check("esito cambiato a mano dalla reception", "Obiettivo raggiunto" in html and "rinnovato al banco" in html)

s,_,_ = oth.req(f"/Conversazioni/{cid}"); _,lst,_ = oth.req("/Conversazioni?view=tutte")
check("un altro gruppo non vede le conversazioni", s == 404 and f"/Conversazioni/{cid}" not in lst, str(s))
s,_,_ = oth.req(f"/Conversazioni/{cid}?handler=State"); check("un altro gruppo non legge lo stato della conversazione", s == 404, str(s))
s,_,_ = oth.post(f"/Conversazioni/{cid}?handler=Reply", {"Text":"intruso"}, form_path="/Account/Password"); check("un altro gruppo non scrive nella conversazione", s == 404, str(s))
_,lst,_ = d.req("/Conversazioni?view=tutte"); check("la direzione vede le conversazioni del gruppo", f"/Conversazioni/{cid}" in lst and "FitActive Alba" in lst)

def bench(sc):
    s,body,_ = m.post("/Assistente/Banco?handler=Run", {"gym":gym["FitActive Alba"],"modelId":rinnovo,"offerId":offer_alba,"scenario":sc}, form_path=f"/Assistente/Banco?gym={gym['FitActive Alba']}")
    try: return _json.loads(body)
    except Exception: return {"raw": body[:200], "status": s}
b1, b2, b3 = bench("sconto"), bench("persona"), bench("robot")
check("banco di prova: sconto entro il limite superato", b1.get("passed") is True, str(b1)[:300])
check("banco di prova: passaggio alla reception superato", b2.get("passed") is True and b2.get("outcome") == "Passata a operatore", str(b2)[:300])
check("banco di prova: si dichiara assistente virtuale", b3.get("passed") is True, str(b3)[:300])
b4 = bench("optout"); check("banco di prova: richiesta di non essere contattato", b4.get("passed") is True, str(b4)[:300])

s,_,_ = ai_settings("openai", okey="sk-oa-test")
cid5,_,_ = start_conv(m, "333 222 1100", name="Marta")
say(m, cid5, "Quanto costa?")
oa = [r for r in fake_ai.REQUESTS if r["path"] == "/v1/chat/completions"]
check("cambio fornitore: risponde OpenAI", oa and "Il rinnovo costa 399 €." in page(m, cid5) and oa[-1]["body"]["messages"][0]["role"] == "system", str(oa[-1]["body"])[:200] if oa else "nessuna richiesta")
_,html,_ = sa.req("/Impostazioni/AI")
check("consumi AI registrati per uso e fornitore", all(x in html for x in ["Conversazioni di prova","Banco di prova","Prova del collegamento","openai","anthropic"]), html[html.find("Consumi"):][:400])
ai_settings("")
cid6,_,_ = start_conv(m, "333 222 3300", name="Elena")
say(m, cid6, "Ciao")
html = page(m, cid6); check("con l'assistente spento risponde la reception", "Serve una persona" in html and "collega della reception" in html)
# 5f. Passo 6: campagne, orari di invio, limiti
from datetime import datetime as _dt, timedelta as _td
from zoneinfo import ZoneInfo as _Zone
ai_settings("anthropic")  # la chiave inserita prima resta
def import_list(client, gym_id, name, people):
    rws = [[n, "Test", ph, "", "Annuale", "2026-10-31", "SI", ""] for n, ph in people]
    s, res = client.post_json("/Lists/New?handler=Import", {"GymId":gym_id,"Name":name,"FileName":"x.xlsx","Map":mp,"Rows":rws}, "/Lists/New")
    return res.get("listId")
def camp_post(client, data, gym_id):
    s,html,loc = client.post("/Campagne/Nuova", dict(data, Gym=gym_id), form_path=f"/Campagne/Nuova?gym={gym_id}")
    mm = re.search(r"/Campagne/(\d+)$", loc or "")
    return (mm.group(1) if mm else None), s, html
def cpage(client, cid): return client.req(f"/Campagne/{cid}")[1]
def act(client, cid, handler): return client.post(f"/Campagne/{cid}?handler={handler}", {}, form_path=f"/Campagne/{cid}")
alba = gym["FitActive Alba"]; orari = f"/Sedi/Orari/{alba}"

s,_,_ = o.req("/Campagne"); check("l'operatore non gestisce le campagne", s in (302,403), str(s))
_,html,_ = m.req(orari); check("orari di invio: di base sempre, 24 ore su 24", "sempre, 24 ore su 24" in html)
week = {f"Days[{i}].On":"true" for i in range(7)}
s,html,_ = m.post(orari, dict(week, Mode="fasce", **{f"Days[{i}].From":"20:00" for i in range(7)}, **{f"Days[{i}].To":"09:00" for i in range(7)}))
check("orari sbagliati rifiutati con spiegazione", s == 200 and "la fine dopo l'inizio" in html, str(s))
now_rome = _dt.now(_Zone("Europe/Rome"))
closed_from, closed_to = ("13:00","14:00") if now_rome.hour < 12 else ("08:00","09:00")
s,_,_ = m.post(orari, dict(week, Mode="fasce", **{f"Days[{i}].From":closed_from for i in range(7)}, **{f"Days[{i}].To":closed_to for i in range(7)}))
_,html,_ = m.req(orari); check("orari di invio salvati dall'attività", s == 302 and f"Lun–Dom {closed_from}–{closed_to}" in html, html[html.find("adesso"):][:120])
s,_,_ = oth.post(orari, {"Mode":"sempre"}, form_path="/Account/Password"); check("un altro gruppo non cambia gli orari dell'attività", s == 404, str(s))

lid_a = import_list(m, alba, "Campagna ottobre", [("Anna","320 000 0001"),("Bruno","320 000 0002"),("Carla","320 000 0003"),("Dario","320 000 0004"),("Sara","333 111 6677")])
base = {"Name":"Rinnovi ottobre","ListId":lid_a,"ModelId":rinnovo,"OfferId":offer_alba,"TemplateId":tid,"When":"subito","DailyLimit":""}
cA, s, html = camp_post(m, dict(base, Name=""), alba); check("campagna senza nome rifiutata", cA is None and "Dai un nome" in html)
cA, s, html = camp_post(m, dict(base, TemplateId="999999"), alba); check("campagna con template non approvato rifiutata", cA is None and "template" in html)
cA, s, html = camp_post(m, base, alba)
html = cpage(m, cA) if cA else html
check("campagna creata come bozza con anteprima del primo messaggio", cA is not None and "Bozza" in html and "Ciao Anna, il tuo abbonamento Annuale in FitActive Alba scade il 31/10/2026" in html and "Avvia l'invio a 5 persone" in html, (html or "")[:300])
m.post("/OptOuts?handler=Add", {"Phone":"320 000 0004","Reason":"chiesto in reception"}, form_path="/OptOuts")
act(m, cA, "Start"); html = cpage(m, cA)
check("fuori orario la campagna aspetta e dice quando riparte", "In invio" in html and "Adesso l'attività non invia" in html and "Si riparte da solo" in html and "Inviati (0)" in html)
m.post(orari, {"Mode":"sempre"}); act(m, cA, "Run"); _time.sleep(0.5); html = cpage(m, cA)
check("con orario aperto la campagna invia e si completa", "Inviati (3)" in html and "Completata" in html, html[html.find('class="stats"'):][:400])
check("saltati: chi è entrato nella lista STOP e chi ha già una conversazione aperta", "Saltati (2)" in html and "nella lista STOP" in html and "già una conversazione aperta" in html)
conv_anna = re.search(r"/Conversazioni/(\d+)", html).group(1)
chat = page(m, conv_anna)
check("ogni invio apre la sua conversazione con il primo messaggio", "Ciao Anna, il tuo abbonamento" in chat and "Rinnovo abbonamento" in chat)
say(m, conv_anna, "Quanto costa?")
check("il cliente risponde e l'assistente prosegue la campagna", "Il rinnovo costa 399 €." in page(m, conv_anna))
say(m, conv_anna, "Va bene, procediamo")
html = cpage(m, cA)
check("risposte ed esiti contati nella campagna", re.search(r"<b>1</b><span>hanno risposto", html) is not None and re.search(r"<b>1</b><span>obiettivo raggiunto", html) is not None)

lid_b = import_list(m, alba, "Seconda lista", [("Elisa","320 000 0011"),("Fabio","320 000 0012"),("Gino","320 000 0013")])
cB,_,_ = camp_post(m, dict(base, Name="Limite giornaliero", ListId=lid_b, DailyLimit="1"), alba)
act(m, cB, "Start"); _time.sleep(0.3); act(m, cB, "Run"); html = cpage(m, cB)
check("limite giornaliero della campagna rispettato", "Inviati (1)" in html and "In attesa (2)" in html and "limite di 1 invii al giorno" in html, html[html.find("Ultimo giro"):][:200])
act(m, cB, "Pause"); check("campagna in pausa", "In pausa" in cpage(m, cB))
act(m, cB, "Resume"); check("campagna ripresa", "In invio" in cpage(m, cB))
act(m, cB, "Cancel"); html = cpage(m, cB)
check("campagna annullata: chi era in attesa non riceve nulla", "Annullata" in html and "Saltati (2)" in html and "campagna annullata" in html)
tomorrow = (now_rome + _td(days=1)).strftime("%Y-%m-%dT09:30")
cC,_,_ = camp_post(m, dict(base, Name="Programmata", ListId=lid_b, When="data", StartAt=tomorrow), alba)
act(m, cC, "Start"); html = cpage(m, cC)
check("campagna programmata per domani alle 9:30", "Programmata" in html and "09:30" in html and "Inviati (0)" in html)
s,_,_ = oth.req(f"/Campagne/{cA}"); check("un altro gruppo non vede le campagne", s == 404, str(s))
s,_,_ = oth.post(f"/Campagne/{cC}?handler=Cancel", {}, form_path="/Account/Password"); check("un altro gruppo non annulla le campagne", s == 404, str(s))
_,html,_ = d.req("/Campagne"); check("la direzione vede le campagne del gruppo", "Rinnovi ottobre" in html and "FitActive Alba" in html)

# Numero Meta (finto server): invio vero, errore di chiave che mette in pausa, ripresa
fake.server_close(); fake = fake_meta.start(5079)
d.post(tpl_b + "?handler=Refresh", {}, form_path=tpl_b)  # il template era stato segnato rifiutato in una prova precedente
bra = gym["FitActive Bra"]
_,html,_ = d.req("/Modelli"); pil = re.search(r"model=(\d+)", html[html.find("Prova corso Pilates"):]).group(1)
lid_c = import_list(d, bra, "Bra ottobre", [("Ivo","320 000 0021"),("Lia","320 000 0022")])
fake_meta.TOKEN = "revocata"
cD,_,_ = camp_post(d, {"Name":"Pilates Bra","ListId":lid_c,"ModelId":pil,"OfferId":"","TemplateId":tidb,"When":"subito","DailyLimit":""}, bra)
act(d, cD, "Start"); html = cpage(d, cD)
check("chiave Meta non valida: la campagna va in pausa e spiega il motivo", "In pausa" in html and "codice 190" in html and "In attesa (2)" in html, html[html.find("note"):][:300])
fake_meta.TOKEN = "good-token"
act(d, cD, "Resume"); html = cpage(d, cD)
sent_meta = [r for r in fake_meta.REQUESTS if r["method"] == "POST" and r["path"].endswith("/111/messages") and r["body"].get("template",{}).get("name") == "rinnovo_bra" and r["body"].get("to") in ("393200000021","393200000022")]
check("ripresa: template inviati a Meta per ogni destinatario", "Completata" in html and len({r["body"]["to"] for r in sent_meta}) == 2, str(len(sent_meta)))
check("limite di Meta del numero mostrato", "1.000 persone nuove in 24 ore" in html or "1,000 persone nuove in 24 ore" in html)
s,body,_ = Client().req("/jobs/tick?token=" + tick.group(1)); check("il giro pianificato lavora anche le campagne", s == 200 and '"sent":' in body, body[:200])
fake.shutdown()


# 5g. Passo 7: postazione reception, avvisi in mvchat, risposte rapide, archivio dialoghi
def badge(client):
    s,body,_ = client.req("/Conversazioni?handler=Badge")
    return _json.loads(body) if s == 200 else {}
before_m = badge(m).get("count", 0)
cid7,_,_ = start_conv(m, "333 444 0001", name="Teresa")
say(m, cid7, "Posso parlare con una persona della reception?")
b = badge(o)
check("avviso: la conversazione passata alla reception compare per l'operatore", b.get("count", 0) >= 1 and b.get("name") and b.get("id"), str(b))
_,html,_ = o.req("/Account/Password")
check("avviso visibile nel menu e nel titolo della pagina", re.search(r'id="waitBadge"[^>]*>\d+</span>', html) is not None and re.search(r"<title>\(\d+\) ", html) is not None)
_,lst,_ = o.req("/Conversazioni"); check("postazione: tra le «da gestire», libera", f"/Conversazioni/{cid7}" in lst and "— libera —" in lst)
o.post(f"/Conversazioni/{cid7}?handler=Take", {}, form_path=f"/Conversazioni/{cid7}")
html = page(o, cid7); check("l'operatore prende in carico la conversazione", "in carico a Op Alba" in html and "Lascia ai colleghi" in html)
check("presa in carico da un collega: sparisce dagli avvisi degli altri", badge(m).get("count", 0) == before_m, f"{before_m} {badge(m)}")
_,lst,_ = o.req("/Conversazioni?view=mie"); check("vista «prese in carico da me»", f"/Conversazioni/{cid7}" in lst)
say(m, cid7, "Allora? Mi rispondete?", wait=False); _time.sleep(0.5)
_,lst,_ = o.req("/Conversazioni"); check("il cliente riscrive: «il cliente aspetta» in cima", "Il cliente aspetta" in lst and lst.find(f"/Conversazioni/{cid7}") > 0)
s,_,_ = m.post("/RisposteRapide?handler=Add", {"Target":f"g:{alba}","Title":"Orari reception","Body":"Ciao {{nome}}, ti aspettiamo in {{palestra}} dalle 9 alle 21."}, form_path="/RisposteRapide")
s2,html,_ = m.post("/RisposteRapide?handler=Add", {"Target":f"g:{gym['FitActive Bra']}","Title":"Intrusa","Body":"x"}, form_path="/RisposteRapide")
check("risposte rapide: il responsabile le crea solo per la sua attività", s == 302 and s2 == 200 and "Scegli dove usare" in html, f"{s} {s2}")
d.post("/RisposteRapide?handler=Add", {"Target":f"o:{org['FitActive']}","Title":"Certificato medico","Body":"Ricorda di portare il certificato medico, {{nome}}."}, form_path="/RisposteRapide")
html = page(o, cid7)
check("l'operatore trova le risposte rapide dell'attività e del gruppo", "Orari reception" in html and "Certificato medico" in html and 'data-body="Ciao {{nome}}, ti aspettiamo in {{palestra}} dalle 9 alle 21."' in html)
_,html,_ = m.req("/RisposteRapide"); rid = re.search(r'handler=Delete&amp;id=(\d+)|id=(\d+)&amp;handler=Delete|/RisposteRapide\?id=(\d+)&amp;handler=Delete', html)
s,_,_ = o.req("/RisposteRapide"); check("l'operatore non gestisce le risposte rapide", s in (302,403), str(s))
s,html,_ = oth.req("/RisposteRapide"); check("risposte rapide invisibili alle altre gruppi", s == 200 and "<b>Orari reception</b>" not in html and "<b>Certificato medico</b>" not in html, str(s) + html[max(0, html.find("Orari reception")-300):][:400])
o.post(f"/Conversazioni/{cid7}?handler=Reply", {"Text":"Ciao Teresa, ti aspettiamo in FitActive Alba dalle 9 alle 21."}, form_path=f"/Conversazioni/{cid7}")
o.post(f"/Conversazioni/{cid7}?handler=Release", {}, form_path=f"/Conversazioni/{cid7}")
html = page(m, cid7); check("l'operatore lascia la conversazione ai colleghi", "in carico a" not in html)
opid = re.search(r'<option value="(\d+)"[^>]*>Op Alba</option>', html)
m.post(f"/Conversazioni/{cid7}?handler=Assign", {"AssignTo":opid.group(1) if opid else "0"}, form_path=f"/Conversazioni/{cid7}")
check("il responsabile assegna la conversazione a un collega", "in carico a Op Alba" in page(m, cid7))
s,_,_ = o.post(f"/Conversazioni/{cid7}?handler=Assign", {"AssignTo":opid.group(1) if opid else "0"}, form_path=f"/Conversazioni/{cid7}")
check("l'operatore non assegna ad altri", s in (302,403), str(s))

_,html,_ = o.req("/Conversazioni/Archivio?tipo=operatore&prove=true")
check("archivio «con operatore»: conversazione con nome dell'operatore", f"/Conversazioni/{cid7}" in html and "Op Alba" in html and "1 operatore" in html)
anna_link = f"/Conversazioni/{conv_anna}"
check("archivio «con operatore» non contiene i dialoghi solo AI", anna_link not in html)
check("archivio: anche chi l'assistente ha passato alla reception è «con operatore»", f"/Conversazioni/{cid3}\"" in html)
_,html,_ = o.req("/Conversazioni/Archivio?tipo=ai")
check("archivio «solo assistente AI»: dialoghi senza intervento di persone", anna_link in html and f"/Conversazioni/{cid7}" not in html and "Rinnovi ottobre" in html)
check("archivio: di base senza conversazioni di prova", f"/Conversazioni/{cid}\"" not in html)
_,html,_ = o.req(f"/Conversazioni/Archivio?esito=obiettivo_raggiunto&q=Anna")
check("archivio: filtri per esito e cliente", anna_link in html and "Bruno" not in html)
check("archivio: numeri per tipo di gestione", re.search(r"<b>\d+ <small[^>]*>\d+%</small></b><span>solo assistente AI", html) is not None)
s,csv,_ = o.req("/Conversazioni/Archivio?handler=Csv&tipo=operatore&prove=true")
check("archivio scaricabile per Excel", s == 200 and "Data;Attività;Cliente" in csv and "Teresa" in csv and "Con operatore" in csv and "Anna" not in csv, csv[:200])
_,html,_ = oth.req("/Conversazioni/Archivio?prove=true"); check("archivio: le altre gruppi non vedono i dialoghi", "Teresa" not in html and "Anna" not in html)
check("avvisi: le altre gruppi non vedono nulla", badge(oth).get("count") == 0, str(badge(oth)))

# 5h. Passo 8: pannello di controllo e report per livello
s,html,_ = d.req("/Report")
check("report del gruppo: confronto tra le sue attività", s == 200 and "Confronto tra le attività" in html and "FitActive Alba" in html and "FitActive Bra" in html and "Centro Benessere Aurora" not in html, str(s))
check("report: grafici giorno per giorno", html.count('class="bars-chart"') == 3 and 'class="vbar"' in html)
s,html,_ = m.req(f"/Report?attivita={gym['FitActive Bra']}")
check("report dell'attività: le sue campagne (l'attività la decide il ruolo)", s == 200 and "Campagne nel periodo" in html and "FitActive Alba" in html and "Pilates Bra" not in html, str(s))
row = re.search(r'<a href="/Campagne/\d+"><b>Rinnovi ottobre</b></a>.*?</tr>', html, re.S)
cells = re.findall(r'<td class="num">(.*?)</td>', row.group(0), re.S) if row else []
check("report campagna: inviati, obiettivi e costo Meta stimato", row is not None and cells[0].startswith("3 ") and cells[2].startswith("<b>1</b>") and cells[5] == "0,20 €", str(cells))
s,html,_ = sa.req("/Report")
check("report MVitalia: gruppi e attività singole", s == 200 and "Gruppi e attività singole" in html and "gruppo · " in html and "attività singola" in html and "Centro Benessere Aurora" in html, str(s))
s,_,_ = sa.req("/Report?attivita=999999"); check("report: attività inesistente", s == 404, str(s))
s,html,_ = oth.req("/Report"); check("report: le altre attività non vedono i dati", s == 200 and "Rinnovi ottobre" not in html and "FitActive" not in html, str(s))
s,_,_ = o.req("/Report"); check("l'operatore non apre i report", s in (302,403), str(s))
s,csv,_ = d.req("/Report?handler=Csv")
check("report scaricabile per Excel", s == 200 and csv.lstrip("﻿").startswith("Nome;Tipo;Primi messaggi") and "FitActive Alba" in csv and "Totale" in csv, csv[:150])
s,_,_ = sa.post("/Impostazioni/WhatsApp", {"AppId":"123456","AppSecret":"","GraphVersion":"v23.0","GraphBaseUrl":"http://127.0.0.1:5079","MarketingPrice":"0,07","UtilityPrice":"0.03"})
_,html,_ = sa.req("/Impostazioni/WhatsApp"); check("listino Meta per la stima dei costi modificabile", 'value="0.07"' in html and 'value="0.03"' in html)
_,html,_ = sa.req("/")
check("pannello MVitalia con le cose da controllare", "Pannello MVitalia" in html and "Da controllare" in html and "Il fornitore AI non ha risposto" in html and "Urgente" in html)
_,html,_ = d.req("/"); check("panoramica del gruppo con i suoi numeri", "Le attività del gruppo" in html and "Campagne in invio" in html and "operazione pianificata" not in html)
s,html,_ = o.req("/"); check("panoramica dell'operatore", s == 200 and "Conversazioni aperte" in html and "Consumo AI" not in html, str(s))

# 5i. Passo 9: abbonamenti e rendiconti da fatturare
_now = _dt.now(_Zone("Europe/Rome")); cur_m = _now.strftime("%Y-%m")
prev_m = (_now.replace(day=1) - _td(days=1)).strftime("%Y-%m")
s,_,_ = sa.post("/Impostazioni/Fatturazione", {"DefaultFee":"49,90","Markup":"20","UsdToEur":"0,9","Vat":"22"})
check("impostazioni di fatturazione salvate (ricarico AI 20%)", s == 302, str(s))
def set_fee(gid, name, fee, frm, to=""):
    return sa.post(f"/Gyms/Edit/{gid}", {"Input.Id":gid,"Input.Name":name,"Input.City":"X","Input.IsActive":"true","Fee":fee,"FeeFrom":frm,"FeeTo":to}, form_path=f"/Gyms/Edit/{gid}")[0]
r1 = set_fee(gym["FitActive Alba"], "FitActive Alba", "", "2026-01-01")
r2 = set_fee(gym["FitActive Bra"], "FitActive Bra", "39,00", "2026-01-01", (_now.replace(day=1) - _td(days=10)).strftime("%Y-%m-%d"))
r3 = set_fee(int(aur), "Centro Benessere Aurora", "29", _now.replace(day=1).strftime("%Y-%m-%d"))
s,html,_ = sa.post(f"/Gyms/Edit/{gym['FitActive Alba']}", {"Input.Id":gym["FitActive Alba"],"Input.Name":"FitActive Alba","Input.IsActive":"true","Fee":"abc"}, form_path=f"/Gyms/Edit/{gym['FitActive Alba']}")
check("abbonamento per attività impostato da MVitalia", r1 == r2 == r3 == 302 and s == 200 and "Scrivi il canone" in html, f"{r1} {r2} {r3} {s}")
s,html,_ = d.post(f"/Gyms/Edit/{gym['FitActive Alba']}", {"Input.Id":gym["FitActive Alba"],"Input.Name":"FitActive Alba","Input.IsActive":"true","Fee":"1","FeeFrom":"2020-01-01"}, form_path=f"/Gyms/Edit/{gym['FitActive Alba']}")
_,html,_ = sa.req(f"/Gyms/Edit/{gym['FitActive Alba']}"); check("l'amministratore di gruppo non cambia l'abbonamento", 'name="FeeFrom" type="date" value="2026-01-01"' in html)
_,html,_ = sa.req(f"/Fatturazione?mese={prev_m}")
row = re.search(r"<b>FitActive</b>.*?</tr>", html, re.S); cells = re.findall(r'<td class="num">(.*?)</td>', row.group(0), re.S) if row else []
check("rendiconto del mese scorso intestato al gruppo: canoni delle sue attività", row is not None and cells[:2] == ["2", "88,90 €"] and "108,46 €" in cells[4] and "Centro Benessere Aurora" not in html, str(cells))
_,html,_ = sa.req(f"/Fatturazione?mese={cur_m}")
row = re.search(r"<b>FitActive</b>.*?</tr>", html, re.S); cells = re.findall(r'<td class="num">(.*?)</td>', row.group(0), re.S) if row else []
check("mese in corso: canone e consumo AI con ricarico", row is not None and cells[1] == "49,90 €" and cells[2] != "0,00 €" and "Mese in corso" in html, str(cells))
check("attività singola: rendiconto intestato all'attività", re.search(r"<b>Centro Benessere Aurora</b><div class=\"muted\">attività singola", html) is not None and "29,00 €" in html and "mancano ragione sociale o P.IVA" in html)
_,html,_ = sa.req(f"/Fatturazione/Dettaglio?mese={cur_m}&org={org['FitActive']}")
check("dettaglio stampabile con calcolo del consumo AI", "Intestato a" in html and "FitActive S.r.l." in html and "P.IVA 01234567890" in html and "ricarico 20%" in html and "× cambio 0,9" in html and "Stampa o salva PDF" in html)
s,_,_ = sa.post(f"/Fatturazione?mese={cur_m}&handler=Close", {}, form_path=f"/Fatturazione?mese={cur_m}")
_,html,_ = sa.req(f"/Fatturazione?mese={cur_m}"); check("il mese in corso non si chiude", "Chiuso" not in html)
sa.post(f"/Fatturazione?mese={prev_m}&handler=Close", {}, form_path=f"/Fatturazione?mese={prev_m}")
sa.post("/Impostazioni/Fatturazione", {"DefaultFee":"59,90","Markup":"20","UsdToEur":"0,9","Vat":"22"})
_,html,_ = sa.req(f"/Fatturazione?mese={prev_m}")
check("mese chiuso: importi fissati anche se cambiano le regole", "Chiuso" in html and "88,90 €" in html)
_,html,_ = sa.req(f"/Fatturazione?mese={cur_m}"); check("i mesi aperti usano le regole nuove", "59,90 €" in html)
sa.post(f"/Fatturazione?mese={prev_m}&handler=Reopen&org={org['FitActive']}", {}, form_path=f"/Fatturazione?mese={prev_m}")
_,html,_ = sa.req(f"/Fatturazione?mese={prev_m}"); check("rendiconto riaperto e ricalcolato", "98,90 €" in html and "Provvisorio" in html)
_,html,_ = d.req(f"/Fatturazione?mese={cur_m}")
check("il gruppo vede solo il suo rendiconto", "FitActive" in html and "Centro Benessere Aurora" not in html and "Chiudi il mese" not in html)
s,_,_ = d.req(f"/Fatturazione/Dettaglio?mese={cur_m}&org=999999"); check("rendiconto di altri: non trovato", s == 404, str(s))
s,_,loc = d.post(f"/Fatturazione?mese={prev_m}&handler=Close", {}, form_path="/Account/Password")
_,html,_ = sa.req(f"/Fatturazione?mese={prev_m}"); check("solo MVitalia chiude i mesi", (s == 403 or "/Error/403" in (loc or "")) and "Provvisorio" in html, f"{s} {loc}")
_,html,_ = aurc.req(f"/Fatturazione?mese={cur_m}"); _,menu,_ = aurc.req("/")
check("l'attività singola vede il suo rendiconto", "Centro Benessere Aurora" in html and "29,00 €" in html and "FitActive" not in html and ">Rendiconti<" in menu)
_,html,_ = m.req(f"/Fatturazione?mese={cur_m}"); _,menu,_ = m.req("/")
check("le attività di un gruppo non hanno rendiconti propri", "Niente da fatturare" in html and ">Rendiconti<" not in menu)
s,csv,_ = sa.req(f"/Fatturazione?mese={cur_m}&handler=Csv")
check("rendiconti scaricabili per la contabilità", s == 200 and "Intestatario;Ragione sociale;Partita IVA" in csv and "Consumo AI" in csv and "Abbonamento" in csv, csv[:150])

# 5j. Passo 10: privacy e sicurezza
import urllib.request as _ur
hdr = _ur.urlopen(BASE + "/Login").headers
check("protezioni del browser attive", hdr.get("X-Frame-Options") == "DENY" and "frame-ancestors 'none'" in (hdr.get("Content-Security-Policy") or "") and hdr.get("X-Content-Type-Options") == "nosniff", str(dict(hdr))[:200])
d.post("/OptOuts?handler=Add", {"Phone":"347 222 3344","Reason":"chiesto alla direzione"}, form_path="/OptOuts")
_,stop_m,_ = m.req("/OptOuts"); _,stop_d,_ = d.req("/OptOuts")
check("lista STOP: l'attività vede solo i suoi numeri, il gruppo tutti", "+393472223344" in stop_d and "+393472223344" not in stop_m and "+393331114455" in stop_m)
aurc.post(f"/Attivita/{aur}", {"Input.Name":"Centro Benessere Aurora","Input.LegalName":"=1+1","Input.PrivacyUrl":"https://www.aurora.test/privacy"}, form_path=f"/Attivita/{aur}")
_,csv,_ = sa.req(f"/Fatturazione?mese={cur_m}&handler=Csv")
check("file per Excel: le formule nei dati vengono neutralizzate", "\"'=1+1\"" in csv and "\"=1+1\"" not in csv, csv[csv.find("Aurora"):][:120])
s,html,_ = aurc.post(f"/Attivita/{aur}", {"Input.Name":"Centro Benessere Aurora","Input.PrivacyUrl":"non è un link"}, form_path=f"/Attivita/{aur}")
check("informativa privacy: serve un link valido", s == 200 and "indirizzo completo dell'informativa" in html, str(s))
_,html,_ = aurc.req(f"/Modelli/Anteprima?model={rinnovo}&gym={aur}")
check("l'assistente indica l'informativa privacy dell'attività", "indica l'informativa: https://www.aurora.test/privacy" in html)
_,html,_ = m.req(f"/Modelli/Anteprima?model={rinnovo}&gym={gym['FitActive Alba']}")
check("senza informativa l'assistente passa la privacy allo staff", "Se il cliente chiede della privacy o dei suoi dati, passa a una persona dello staff" in html)
s,html,_ = m.req("/Privacy?numero=333%20111%202233")
check("richiesta privacy: dati del cliente trovati nell'attività", s == 200 and "Dati su +393331112233" in html and "conversazioni con" in html, str(s))
s,body,_ = m.req("/Privacy?numero=333%20111%202233&handler=Export")
dump = _json.loads(body) if s == 200 else {}
check("diritto di accesso: tutti i dati in un file", dump.get("numero") == "+393331112233" and len(dump.get("messaggi", [])) >= 5, str(dump)[:200])
_,html,_ = oth.req("/Privacy?numero=333%20111%202233"); check("richiesta privacy: le altre attività non trovano nulla", "Nessun dato su" in html)
s,_,_ = o.req("/Privacy"); check("l'operatore non gestisce le richieste privacy", s in (302,403), str(s))
m.post("/Privacy?numero=%2B393331112233&handler=Erase", {"AddStop":"true"}, form_path="/Privacy?numero=333%20111%202233")
s1,_,_ = m.req(f"/Conversazioni/{cid}"); _,html,_ = m.req("/Privacy?numero=333%20111%202233"); _,stop_m,_ = m.req("/OptOuts")
check("diritto all'oblio: dati cancellati e numero nella lista STOP", s1 == 404 and "Dati su +393331112233" in html and "<b>0</b> conversazioni" in html and "+393331112233" in stop_m, str(s1))
_,audit_ok,_ = sa.req("/Impostazioni/Privacy")
check("conservazione di base: 12 mesi", 'value="12"' in audit_ok)
try:
    import pymysql
    db = pymysql.connect(host=os.environ.get("DB_HOST","127.0.0.1"), user="mv", password="Pwd12345!", database="mvchat", autocommit=True)
    with db.cursor() as cur:
        cur.execute("UPDATE Conversations SET CreatedAt=UTC_TIMESTAMP() - INTERVAL 13 MONTH, LastMessageAt=UTC_TIMESTAMP() - INTERVAL 13 MONTH WHERE Id=%s", (cid4,))
        cur.execute("UPDATE WaWebhookEvents SET ReceivedAt=UTC_TIMESTAMP() - INTERVAL 40 DAY ORDER BY Id LIMIT 3")
    sa.post("/Impostazioni/Privacy?handler=Run", {}, form_path="/Impostazioni/Privacy")
    _,html,_ = sa.req("/Impostazioni/Privacy"); s4,_,_ = m.req(f"/Conversazioni/{cid4}")
    with db.cursor() as cur:
        cur.execute("SELECT COUNT(*) FROM WaMessages WHERE ConversationId=%s", (cid4,)); left = cur.fetchone()[0]
        cur.execute("SELECT COUNT(*) FROM Conversations"); still = cur.fetchone()[0]
    check("pulizia automatica: cancellati i dati oltre i 12 mesi, il resto resta", s4 == 404 and left == 0 and still > 5 and "conversazioni 1" in html and "avvisi Meta 3" in html, f"{s4} {left} {still} {html[html.find('Ultima pulizia'):][:200]}")
except ImportError:
    check("pulizia automatica (serve pymysql per la prova)", False, "pip install pymysql")
fai.shutdown()

# 5k. Controllo generale: STOP, messaggi spontanei, foto e vocali, saluti finali, dati mancanti, aggiornamenti del database
alba_pid = f"sim-{alba}"
_cg = [0]
def hook(msgs):
    p = {"object":"whatsapp_business_account","entry":[{"id":"sim","changes":[{"field":"messages","value":{"messaging_product":"whatsapp","metadata":{"phone_number_id":alba_pid},"messages":msgs}}]}]}
    raw_ = _json.dumps(p).encode(); sig_ = "sha256=" + hmac.new(b"testsecret", raw_, hashlib.sha256).hexdigest()
    rq = urllib.request.Request(BASE + "/webhooks/whatsapp", data=raw_, headers={"Content-Type":"application/json","X-Hub-Signature-256":sig_})
    try: return urllib.request.urlopen(rq).status
    except urllib.error.HTTPError as e: return e.code
def txt(frm, body):
    _cg[0] += 1; return {"from":frm,"id":f"wamid.CG{_cg[0]}","timestamp":"1","type":"text","text":{"body":body}}
hook([txt("393401110001", "Per favore non scrivetemi più, grazie!")])
hook([txt("393401110002", "Basta così, passo io in reception per gli orari")])
_,stop_d,_ = d.req("/OptOuts")
check("STOP riconosciuto anche scritto a parole («non scrivetemi più»)", "+393401110001" in stop_d)
check("«basta così…» non è uno STOP", "+393401110002" not in stop_d)
_,lst,_ = m.req("/Conversazioni?view=da_gestire")
check("messaggio fuori da una campagna: arriva alla reception", "+393401110002" in lst and "Messaggio spontaneo" in lst, lst[lst.find("<tbody>"):][:300])
hook([{"from":"393401110002","id":"wamid.CGR1","timestamp":"1","type":"reaction","reaction":{"message_id":"x","emoji":"👍"}}])
_,lst,_ = m.req("/Conversazioni?view=tutte")
check("una reazione 👍 non apre altre conversazioni", lst.count("<b>+393401110002</b>") == 1, str(lst.count("<b>+393401110002</b>")))
cidm,_,_ = start_conv(m, "333 444 0099", name="Marta")
hook([{"from":"393334440099","id":"wamid.CGI1","timestamp":"1","type":"image","image":{"id":"m1","caption":"ecco il certificato"}}])
html = page(m, cidm)
check("foto o vocale: l'assistente non risponde a vuoto, passa a una persona", "[immagine] ecco il certificato" in html and "Serve una persona" in html and "collega della reception" in html, html[html.find('class="stats"'):][:300])
say(m, conv_anna, "Grazie mille 🙏", wait=False); _time.sleep(0.5)
st1 = state(m, conv_anna).get("status")
say(m, conv_anna, "Scusate, a che ora apre la sala domenica?", wait=False); _time.sleep(0.5)
st2 = state(m, conv_anna).get("status")
check("conversazione chiusa: un «grazie» non la riapre, una domanda sì", st1 == "chiusa" and st2 == "operatore", f"{st1} {st2}")
rws = [["Nadia","Test","320 000 0031","","Annuale","","SI",""],["Omar","Test","320 000 0032","","Annuale","2026-11-30","SI",""]]
_, res = m.post_json("/Lists/New?handler=Import", {"GymId":alba,"Name":"Senza scadenza","FileName":"x.xlsx","Map":mp,"Rows":rws}, "/Lists/New")
cE,_,_ = camp_post(m, dict(base, Name="Dati mancanti", ListId=res.get("listId")), alba)
act(m, cE, "Start"); _time.sleep(0.3); html = cpage(m, cE)
check("dato mancante nel primo messaggio: il cliente viene saltato, niente trattini", "Inviati (1)" in html and "manca il dato «scadenza»" in html, html[html.find('class="stats"'):][:400])
s,html,_ = m.post(tpl_a + "?handler=Delete", {"TemplateId":tid}, form_path=tpl_a)
_,html,_ = m.req(tpl_a); check("un template approvato e usato non si elimina", "rinnovo_ottobre" in html and "Si possono eliminare solo" in html)
# Campi liberi «Servizio / corso» e «Note»: nel primo messaggio e nelle istruzioni dell'assistente
mp2 = dict(mp, Service=9, Notes=10)
_, res = m.post_json("/Lists/New?handler=Import", {"GymId":alba,"Name":"Corsi","FileName":"x.xlsx","Map":mp2,
    "Rows":[["Rita","Test","320 000 0041","","Annuale","2026-11-30","SI","","","Pilates","Viene la sera"]]}, "/Lists/New")
lid_s = res.get("listId")
_,html,_ = m.req(f"/Lists/Detail/{lid_s}")
check("lista: servizio e note importati", "Pilates" in html and "Viene la sera" in html, html[html.find("<tbody>"):][:300])
s1,_,_ = m.post(tpl_a + "?handler=Create", {"Name":"Corso servizio","Category":"MARKETING","Body":"Ciao {{nome}}, a {{sede}} riparte il corso di {{servizio}}. Ti interessa?"}, form_path=tpl_a)
tid_s = re.search(r'<option value="(\d+)">corso_servizio</option>', m.req(alba_wa)[1])
cS,_,_ = camp_post(m, dict(base, Name="Corsi", ListId=lid_s, TemplateId=tid_s.group(1) if tid_s else ""), alba)
act(m, cS, "Start"); _time.sleep(0.3); html = cpage(m, cS)
conv_s = re.search(r"/Conversazioni/(\d+)", html)
chat = page(m, conv_s.group(1)) if conv_s else ""
check("template con {{servizio}}: il primo messaggio cita il corso del cliente", s1 == 302 and "riparte il corso di Pilates" in chat, chat[chat.find('id="chat"'):][:300])
prova_url = f"/Campagne/Prova/{alba}"
s,html,_ = m.post(prova_url + "?handler=Add", {"Name":"Maurizio Prova","Phone":"12"}, form_path=prova_url)
check("numero di prova sbagliato rifiutato", s == 200 and "non è valido" in html, str(s))
s,_,_ = m.post(prova_url + "?handler=Add", {"Name":"Maurizio Prova","Phone":"347 000 1111"}, form_path=prova_url)
_,html,_ = m.req(prova_url); check("numeri di prova dell'attività", s == 302 and "+393470001111" in html and "Maurizio Prova" in html, str(s))
s,_,_ = oth.req(prova_url); check("un altro gruppo non vede i numeri di prova", s == 404, str(s))
cP,_,_ = camp_post(m, dict(base, Name="Con prova", ListId=lid_b), alba)
_,html,_ = m.post(f"/Campagne/{cP}?handler=Test", {}, form_path=f"/Campagne/{cP}")
html = cpage(m, cP); _,lst,_ = m.req("/Conversazioni?view=tutte")
check("prova facoltativa: il primo messaggio arriva ai numeri di prova, la campagna resta in bozza", "Prova inviata a 1 numero" in html and "Bozza" in html and "Inviati (0)" in html and "+393470001111" in lst, html[html.find("flash"):][:200])
_,html,_ = sa.req("/Report"); _,html_d,_ = d.req("/Report?dal=2026-01-01")
check("report: periodi pronti e scelta dell'attività", "Mese scorso" in html and "Da inizio anno" in html and "Tutti i clienti" in html and "Tutto il gruppo" in html_d and "FitActive Bra" in html_d)
s,body,_ = Client().req("/health"); hj = _json.loads(body) if s == 200 else {}
check("aggiornamenti del database applicati da soli (controllo /health)", hj.get("status") == "ok" and hj.get("database") == "ok" and hj.get("schema","").split("/")[0] == hj.get("schema","x/y").split("/")[1], body[:200])
_,html,_ = sa.req("/Impostazioni/Registro"); s2,_,_ = m.req("/Impostazioni/Registro")
check("registro tecnico: MVitalia vede avvii ed errori del programma", "mvchat avviato" in html and s2 in (302, 403), f"{s2} {html[html.find('panel-b'):][:200]}")
pw_b,_ = mk_user(sa, "resp.bra@fitactive.test", "manager", "", gym["FitActive Bra"])
def bra_active(on):
    return sa.post(f"/Gyms/Edit/{gym['FitActive Bra']}", {"Input.Id":gym["FitActive Bra"],"Input.Name":"FitActive Bra","Input.City":"X","Input.IsActive":on,"Fee":"39,00","FeeFrom":"2026-01-01"}, form_path=f"/Gyms/Edit/{gym['FitActive Bra']}")[0]
r_off = bra_active("false")
y = Client(); s,html,_ = y.post("/Login", {"Email":"resp.bra@fitactive.test","Password":pw_b or ""})
check("attività disattivata: i suoi utenti non entrano più", r_off == 302 and "disattivato" in html, f"{r_off} {pw_b}")
bra_active("true")
try:
    import pymysql
    db = pymysql.connect(host=os.environ.get("DB_HOST","127.0.0.1"), user="mv", password="Pwd12345!", database="mvchat", autocommit=True)
    with db.cursor() as cur:
        cur.execute("UPDATE AuditLog SET At=UTC_TIMESTAMP() - INTERVAL 13 MONTH WHERE Action IN ('billing.closed','conversation.take')")
    sa.post("/Impostazioni/Privacy?handler=Run", {}, form_path="/Impostazioni/Privacy")
    with db.cursor() as cur:
        cur.execute("SELECT SUM(Action='billing.closed'), SUM(Action='conversation.take') FROM AuditLog"); kept, gone = cur.fetchone()
        cur.execute("SELECT COUNT(*) FROM SchemaBatches WHERE Version=12"); batches = cur.fetchone()[0]
    check("pulizia: le righe di fatturazione restano nel registro, le altre vecchie no", (kept or 0) > 0 and (gone or 0) == 0, f"{kept} {gone}")
    check("aggiornamento del database ricordato pezzo per pezzo", batches >= 10, str(batches))
    with db.cursor() as cur:
        cur.execute("SELECT Service, Notes FROM Conversations WHERE ContactPhone='+393200000041' ORDER BY Id DESC LIMIT 1"); sv = cur.fetchone()
    check("la conversazione ricorda servizio e note per l'assistente", sv is not None and sv[0] == "Pilates" and sv[1] == "Viene la sera", str(sv))
    # Limite di campagne per cliente (qui 1 in 30 giorni): Omar ha già ricevuto «Dati mancanti»
    with db.cursor() as cur:
        cur.execute("UPDATE Conversations SET Status='chiusa' WHERE ContactPhone='+393200000032'")
    meta_form = {"AppId":"123456","AppSecret":"","GraphVersion":"v23.0","GraphBaseUrl":"http://127.0.0.1:5079","MarketingPrice":"0,07","UtilityPrice":"0.03"}
    sa.post("/Impostazioni/WhatsApp", dict(meta_form, MaxCampaigns="1"))
    _, res = m.post_json("/Lists/New?handler=Import", {"GymId":alba,"Name":"Limite","FileName":"x.xlsx","Map":mp,
        "Rows":[["Omar","Test","320 000 0032","","Annuale","2026-11-30","SI",""],["Pia","Test","320 000 0033","","Annuale","2026-11-30","SI",""]]}, "/Lists/New")
    cL,_,_ = camp_post(m, dict(base, Name="Limite contatti", ListId=res.get("listId")), alba)
    act(m, cL, "Start"); _time.sleep(0.3); html = cpage(m, cL)
    check("limite di campagne per cliente: chi ne ha già ricevute abbastanza viene saltato", "Inviati (1)" in html and "ha già ricevuto 1 campagna negli ultimi 30 giorni" in html, html[html.find('class="stats"'):][:400])
    sa.post("/Impostazioni/WhatsApp", dict(meta_form, MaxCampaigns="2"))
    _,html,_ = sa.req("/Impostazioni/WhatsApp"); check("limite di campagne per cliente modificabile da MVitalia (di base 2)", 'name="MaxCampaigns"' in html and 'value="2"' in html)
except ImportError:
    check("pulizia del registro (serve pymysql per la prova)", False, "pip install pymysql")

# 6. Blocco dopo 5 tentativi sbagliati
x = Client()
for i in range(5): x.post("/Login", {"Email":"altra@altra.test","Password":"sbagliata123"})
s,html,_ = x.post("/Login", {"Email":"altra@altra.test","Password":"Altra2026xyz1"})
check("blocco dopo 5 tentativi sbagliati", "Troppi tentativi" in html)

# 7. Gruppo sospeso: i suoi utenti non entrano
sa.post(f"/Orgs/Edit/{org['FitActive']}", {"Input.Id":org["FitActive"],"Input.Name":"FitActive","Input.Slug":"fitactive","Input.PrimaryColor":"#F6931E","Input.IsActive":"false"}, form_path=f"/Orgs/Edit/{org['FitActive']}")
y = Client(); s,html,_ = y.post("/Login", {"Email":"alba@fitactive.test","Password":"Alba2026xyz1"})
check("gruppo sospeso blocca l'accesso", "disattivato" in html)

# 8. Tick
s,_,_ = Client().req("/jobs/tick?token=sbagliato"); check("tick rifiuta chiave sbagliata", s == 404)
s,body,_ = Client().req("/jobs/tick?token=" + tick.group(1)); check("tick con chiave giusta", s == 200 and '"ok":true' in body)

for n, ok, info in results:
    print(("OK  " if ok else "NO  ") + n + ("" if ok else "  -> " + info[:200]))
print(f"{sum(r[1] for r in results)}/{len(results)} superati")
sys.exit(0 if all(r[1] for r in results) else 1)
