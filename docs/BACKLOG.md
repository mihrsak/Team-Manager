# Backlog Team Manager

Izvor: službeni Redmine vodič i dokumentacija, pregledani 2026-11-08:
- https://www.redmine.org/projects/redmine/wiki/Guide
- https://www.redmine.org/projects/redmine/wiki/Features
- https://www.redmine.org/projects/redmine/wiki/RedmineRoles
- https://www.redmine.org/projects/redmine/wiki/RedmineIssueTrackingSetup
- https://www.redmine.org/projects/redmine/wiki/RedmineIssues
- https://www.redmine.org/projects/redmine/wiki/RedmineTimeTracking

Ovo je vlastita .NET implementacija funkcionalnog uzora, ne kopija izvornog koda. Popis obuhvaća glavne module i proširenja iz službenog vodiča. Status „gotovo” znači implementirano i provjereno za opisani opseg; ne znači potpunu ekvivalentnost Redmineu.

| ID | Cjelina / kriterij prihvata | Status |
|---|---|---|
| AUTH-01 | Prijava brojem radnika, hash lozinke, odjava, privremeno zaključavanje | Gotovo |
| AUTH-02 | Prvi admin/admin samo u praznoj bazi, neobvezni podsjetnik za promjenu | Gotovo |
| AUTH-03 | Profil, funkcija, organizacijska jedinica, promjena lozinke | Gotovo |
| AUTH-04 | Deaktivacija, reset lozinke, opoziv aktivnih sesija | Backlog |
| AUTH-05 | Registracija s odobrenjem / potvrdom e-pošte | Backlog |
| AUTH-06 | AD/LDAP i autentikacija kroz bankarski SSO | Backlog |
| ROLE-01 | Prilagodljive role, zbroj globalnih ovlasti, serverske provjere | Gotovo |
| ROLE-02 | Članstva po timu/projektu, izolacija ovlasti između projekata | Gotovo: jedna rola po članstvu |
| ROLE-03 | Više rola po članstvu, grupe korisnika i nasljeđivanje | Backlog |
| ROLE-04 | Pravila vidljivosti privatnih zadataka i pojedinih polja | Backlog |
| ORG-01 | Unos organizacijskih jedinica, timova i projekata | Gotovo |
| PROJ-01 | Pregled projekata i pripadajućih zadataka prema ovlastima | Gotovo |
| PROJ-02 | Hijerarhija, opis, zatvaranje/arhiviranje, privatni/javni projekti | Gotovo: javno prijavljenim korisnicima; bez nasljeđivanja ovlasti i stanja |
| PROJ-03 | Uključivanje modula i postavke po projektu | Gotovo za postojeće module: zadaci/komentari i vrijeme; novi moduli dodaju se s vlastitim stavkama |
| ISSUE-01 | Unos i uređivanje zadataka, tip, prioritet, status, rok, nositelj | Gotovo |
| ISSUE-02 | Komentari i transakcijska povijest promjena | Gotovo: povijest s detaljima prije/poslije |
| ISSUE-03 | Filtri po projektu/statusu/tekstu | Gotovo |
| ISSUE-04 | Prilagodljivi tipovi, statusi i prijelazi prema roli i tipu | Gotovo: dodavanje šifrarnika, dodavanje/uklanjanje prijelaza; početni status Novo |
| ISSUE-05 | Podzadaci, veze, ovisnosti, duplikati | Backlog |
| ISSUE-06 | Skupno uređivanje, kopiranje/premještanje i CSV izvoz | Backlog |
| ISSUE-07 | Promatrači, privatne bilješke i obavijesti | Backlog |
| QUERY-01 | Spremljeni upiti, stupci, grupiranje, sortiranje i sažeci | Backlog |
| PLAN-01 | Verzije, prekretnice i roadmap | Backlog |
| PLAN-02 | Kalendar i Gantt s datumima i ovisnostima | Backlog |
| TIME-01 | Unos sati po zadatku i pregled zapisa | Gotovo |
| TIME-02 | Izvještaji prema osobi, projektu, aktivnosti i razdoblju | Backlog |
| FIELD-01 | Korisnička polja za zadatke, korisnike, projekte i vrijeme | Backlog |
| DOC-01 | Dokumenti, datoteke i kontrolirani prilozi | Backlog |
| WIKI-01 | Wiki po projektu, verzije, usporedba i prava uređivanja | Backlog |
| NEWS-01 | Novosti i komentari | Backlog |
| FORUM-01 | Forumi, teme, odgovori i moderiranje | Backlog |
| SCM-01 | Veze s Git repozitorijima, pregled commitova i razlika | Backlog |
| NOTIFY-01 | SMTP obavijesti, podsjetnici, red slanja i ponavljanje | Backlog |
| MAIL-01 | Zaprimanje poruka e-pošte u zadatke | Backlog |
| FEED-01 | Feed aktivnosti i Atom/RSS | Backlog |
| SEARCH-01 | Pretraživanje kroz module i osobna nadzorna ploča | Backlog |
| API-01 | Dokumentirani REST API i pristupni tokeni | Backlog |
| EXT-01 | Proširenja, teme i lokalizacija | Backlog |
| AUDIT-01 | Revizijski zapisi administracije i promjena ovlasti | Backlog |
| OPS-01 | Verzijske SQL skripte, ponovljiva instalacija i odvojeni demo podaci | Gotovo, provjereno na zasebnim bazama |
| OPS-02 | Konfiguracija JSON/env, Windows publish paket i upute za IIS | Paket provjeren; ciljni IIS nije testiran |
| OPS-03 | Backup/restore, rollback, nadzor, zdravstvene provjere i logovi | Backlog |
| OPS-04 | OCP manifesti i testiranje iza proxyja | Backlog |
| DB-01 | SQL Server | Gotovo |
| DB-02 | Oracle i Db2 LUW/zOS provider, migracije i integracijski testovi | Backlog |
| QA-01 | Integracijske provjere ovlasti, bootstrapa i novih modula | Kontinuirano |
| QA-02 | Browser E2E, pristupačnost, opterećenje i produkcijski pregled | Backlog |

## Pravilo isporuke

Svaka cjelina: migracija bez gubitka podataka, serverska autorizacija, validacija, demo scenarij, integracijski test, dokumentiranje ograničenja. Produkcijskim spremnim smatramo tek izdanje koje prođe i QA-02, AUTH-04 i OPS-03. Preostale stavke nisu automatski završene samim objavljivanjem izvornog koda na GitHubu.
