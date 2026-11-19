# Status izdanja Team Manager

Provjereno lokalno 19. 11. 2026. SQL Server i Oracle podržavaju aktivne module: račune i ovlasti, projekte i zadatke, planove, vrijeme, prilagodljiva polja, dokumente, wiki, novosti, forum, Git poveznice/povijest, ulaznu e-poštu, obavijesti, SMTP/podsjetnike, osobne API tokene, registraciju i grupe. HR/EN sučelje je zadržano. Početni legacy pregled timskih stavki u Oracle načinu otvara aktivni pregled projekata.

## Lokalna provjera

- Aktivni paket: `artifacts/oracle-complete-final`, Oracle shema 10 / protokol zaključavanja 5.
- URL: https://localhost:7572/Projects; readiness 200.
- Oracle integracije potvrđuju migracijski integritet 51 tablice, očuvanje podataka/povijesti, ovlasti, zaključavanja, paralelne upise, rollback, opozive i zaštitu zadnjeg administratora.
- Stvarni loopback TLS IMAP i SMTP testovi potvrđuju stvaranje privatnih zadataka, deduplikaciju, commit/Seen, HR/EN poruke, ponovne pokušaje, lease zaštitu i podsjetnike.
- Cjeloviti Oracle browser tokovi i 52 automatizirana WCAG A/AA skeniranja su prošli; odvojeni API, registracijski, grupni i e-mail testovi provjeravaju dodatne autentikacijske i UI tokove.
- SQL integracijske regresije i zasebni privremeni SQL testovi registracije/grupa su prošli.
- Zaštitna lista Oracle ruta ostaje uključena. Svi aktivni moduli omogućeni su pojedinačno; neprovjereni/razvojni endpointi nisu globalno otvoreni.

## Preostale provjere i rad

| Stavka | Stvarni status |
|---|---|
| DB-02 / Db2 LUW | U pripremi lokalnog okruženja; nije označeno dovršenim. Driver i službena Community slika dostupni su; rootless pokretanje nije podržalo potrebne mountove, pa se provjerava odvojeno rootful okruženje. |
| DB-02 / Db2 z/OS | Nije implementirano/provjereno na ciljnoj bazi; potreban je ciljni pristup i odgovarajuća Db2 Connect licenca. |
| AUTH-06 | Lokalni Keycloak/OpenLDAP scenariji postoje; bankarski SSO/AD ostaje za ciljno okruženje. |
| MAIL-01 | Lokalni gateway i TLS IMAP su provjereni; namjenski stvarni sandučić nije povezan/provjeren. |
| OPS-02 / OPS-03 | Lokalni IIS/HTTPS, backup/restore i rollback ranije provjereni; ciljni IIS i operativni nadzor nisu potvrđeni. |
| OPS-04 | Lokalna slika/proxy ranije provjereni; ciljni OpenShift nije dostupan u ovom razgovoru. |
| QA-02 | Automatizirane lokalne provjere prošle; kapacitet i završna prihvatna provjera na ciljnom produkcijskom okruženju ostaju otvoreni. |

Ovaj status ne predstavlja potvrdu spremnosti za bankarsku produkciju. DB-02 i vanjske provjere nisu zatvoreni samim pushom na GitHub. Potvrda registracije e-poštom ostaje izvan dogovorenog opsega AUTH-05.
