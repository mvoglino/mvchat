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


# 5c. Passo 3: scheda sede, offerte, modelli di obiettivo
s,_,loc = m.req("/Sedi"); check("responsabile va dritto alla scheda della sua sede", s == 302 and loc == f"/Sedi/Edit/{gym['FitActive Alba']}", f"{s} {loc}")
s,_,loc = m.post(f"/Sedi/Edit/{gym['FitActive Alba']}", {"Input.OpeningHours":"Lun-Ven 7-22, Sab 9-19","Input.Services":"Sala pesi, corsi, sauna","Input.Classes":"Pilates mar 18:30","Input.HowToReach":"Parcheggio gratuito","Input.ExtraInfo":"Serve il certificato medico","Input.AssistantName":"assistente virtuale","Input.Formality":"tu"})
check("responsabile salva la scheda", s == 302, str(s))
s,_,_ = m.req(f"/Sedi/Edit/{gym['FitActive Bra']}"); check("responsabile non apre la scheda di un'altra sede", s == 404, str(s))
s,_,_ = m.post("/Offerte/Edit", {"Input.GymId":gym["FitActive Alba"],"Input.Title":"Rinnovo con 2 mesi omaggio","Input.Description":"14 mesi al prezzo di 12","Input.Price":"399","Input.FullPrice":"465","Input.PriceNote":"una tantum","Input.MaxExtraDiscountPct":"10","Input.ValidTo":"2099-10-31","Input.IsActive":"true"})
check("responsabile crea un'offerta con prezzo", s == 302, str(s))
s,html,_ = m.post("/Offerte/Edit", {"Input.GymId":gym["FitActive Alba"],"Input.Title":"x","Input.Price":"abc","Input.ValidFrom":"2026-12-01","Input.ValidTo":"2026-11-01","Input.IsActive":"true"})
check("offerta con prezzo e date sbagliati rifiutata", s == 200 and "Scrivi un importo" in html and "prima dell'inizio" in html)
s,html,_ = m.post("/Offerte/Edit", {"Input.GymId":gym["FitActive Bra"],"Input.Title":"Intrusa","Input.Price":"1","Input.IsActive":"true"})
check("responsabile non crea offerte per un'altra sede", s == 200 and "Scegli la palestra" in html)
s,_,_ = d.post("/Offerte/Edit", {"Input.GymId":gym["FitActive Bra"],"Input.Title":"Annuale Bra","Input.Price":"1.200","Input.IsActive":"true"})
_,html,_ = d.req("/Offerte"); check("direzione crea offerta per Bra (1.200 = milleduecento)", s == 302 and "1.200 €" in html and "399 €" in html, str(s))
_,html,_ = m.req("/Offerte"); check("responsabile vede solo le offerte della sua sede", "399 €" in html and "Annuale Bra" not in html)
offer_alba = re.search(r'/Offerte/Edit/(\d+)', html).group(1)
s,html,_ = m.req("/Modelli"); rinnovo = re.search(r'/Modelli/Anteprima\?model=(\d+)', html).group(1)
check("modelli standard presenti", all(x in html for x in ["Rinnovo abbonamento","Adesione a una promo","Recupero inattivi","Porta un amico","Sondaggio soddisfazione"]))
s,html,_ = m.req(f"/Modelli/Anteprima?model={rinnovo}&gym={gym['FitActive Alba']}&offer={offer_alba}&nome=Giulia&abbonamento=Annuale&scadenza=2026-10-18")
pr = html[html.find('id="prompt"'):]
check("istruzioni AI con prezzo dell'offerta e scheda sede", all(x in pr for x in ["399 €","invece di 465 €","Lun-Ven 7-22","al massimo il 10%","Rinnovo abbonamento","18 ottobre 2026"]), pr[:400])
check("istruzioni AI senza dati di altre sedi", "1.200" not in pr and "Annuale Bra" not in html)
check("primo messaggio personalizzato", "Ciao Giulia, il tuo abbonamento Annuale in FitActive Alba scade il 18/10/2026" in html)
s,html,_ = m.req(f"/Modelli/Anteprima?model={rinnovo}&gym={gym['FitActive Bra']}")
check("anteprima non usa sedi fuori dal perimetro", "FitActive Bra" not in html[html.find('id="prompt"'):] if 'id="prompt"' in html else True)
s,_,loc = m.req("/Modelli/Edit"); check("responsabile non crea modelli", s == 403 or "/Error/403" in (loc or ""), f"{s} {loc}")
s,_,_ = d.post("/Modelli/Edit", {"Input.Name":"Prova corso Pilates","Input.Success":"Il cliente prenota una lezione di prova","Input.Instructions":"Proponi una lezione di prova gratuita di Pilates.","Input.MaxAiMessages":"6","Input.NeedsOffer":"false","Input.IsActive":"true"})
_,html,_ = m.req("/Modelli"); check("modello della catena creato dalla direzione e visibile in catena", s == 302 and "Prova corso Pilates" in html, str(s))
oth = Client(); oth.post("/Login", {"Email":"altra@altra.test","Password":pw_oth}); oth.post("/Account/Password", {"Current":pw_oth,"New":"Altra2026xyz1","New2":"Altra2026xyz1"})
_,html,_ = oth.req("/Modelli"); check("modello di una catena invisibile alle altre", "Prova corso Pilates" not in html and "Rinnovo abbonamento" in html)
s,_,_ = d.req(f"/Modelli/Edit/{rinnovo}"); check("direzione non modifica i modelli standard", s == 404, str(s))
s,_,_ = sa.req(f"/Modelli/Edit/{rinnovo}"); check("MVitalia modifica i modelli standard", s == 200, str(s))


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
s,_,_ = oth.req(alba_wa); check("un'altra catena non vede il WhatsApp della palestra", s == 404, str(s))
fake.shutdown()

# 6. Blocco dopo 5 tentativi sbagliati
x = Client()
for i in range(5): x.post("/Login", {"Email":"altra@altra.test","Password":"sbagliata123"})
s,html,_ = x.post("/Login", {"Email":"altra@altra.test","Password":"Altra2026xyz1"})
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
