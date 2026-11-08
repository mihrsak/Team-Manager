# Team Manager: vlastita .NET implementacija

Funkcionalne reference pregledane 8.11.2026.:
- https://www.redmine.org/projects/redmine/wiki/Features
- https://www.redmine.org/projects/redmine/wiki/RedmineRoles

Redmineov kod nije preuzet. Koristimo ASP.NET Core 10 i SQL Server.

## Korisnici i ovlasti

Prijava brojem radnika, hashirane lozinke, profil s funkcijom i organizacijskom jedinicom. Prilagodljive role daju ovlasti za korisnike, organizaciju, role ili članstva. Više globalnih rola zbraja ovlasti. U ovoj verziji članstvo nosi jednu rolu po timu/projektu. Kontekstna ovlast za upravljanje članovima vrijedi samo u dodijeljenom timu/projektu. Posljednji puni administrator je zaštićen od uklanjanja ovlasti. Provjere su serverske i promjene prava vrijede pri sljedećem zahtjevu.

## Planirane cjeline

1. Zadaci: tipovi, statusi, prioriteti, rokovi, komentari i povijest.
2. Tijek rada prema tipu zadatka i roli.
3. Projektna hijerarhija, privatnost, više rola po članstvu i prekretnice.
4. Filteri, spremljeni upiti, kalendar i Gantt.
5. Evidencija vremena i izvještaji.
6. Dokumenti, wiki, prilozi i obavijesti.
7. Korisnička polja, AD/LDAP, revizijski zapisi administracije i produkcijsko pakiranje.

Te cjeline još nisu implementirane. Ako nema korisnika, pokretanje aplikacije kreira početni račun admin/admin s punom administracijom. Promjena početne lozinke nudi se pri svakoj prijavi dok je admin/admin u uporabi, uz mogućnost preskakanja. Postojeće lozinke i podaci ne resetiraju se.
