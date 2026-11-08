# Team Manager: vlastita .NET implementacija

Funkcionalne reference pregledane 8.11.2026.:
- https://www.redmine.org/projects/redmine/wiki/Features
- https://www.redmine.org/projects/redmine/wiki/RedmineRoles

Redmineov kod nije preuzet. Koristimo ASP.NET Core 10 i SQL Server.

## Korisnici i ovlasti

Prijava brojem radnika, hashirane lozinke, profil s funkcijom i organizacijskom jedinicom. Prilagodljive role daju ovlasti za korisnike, organizaciju, role ili članstva. Više globalnih rola zbraja ovlasti. U ovoj verziji članstvo nosi jednu rolu po timu/projektu. Kontekstna ovlast za upravljanje članovima vrijedi samo u dodijeljenom timu/projektu. Posljednji puni administrator je zaštićen od uklanjanja ovlasti. Provjere su serverske i promjene prava vrijede pri sljedećem zahtjevu.

## Status razvoja

Zadaci, osnovni filtri, komentari, povijest, unos sati, šifrarnici i tijek rada sada su implementirani. Postoje Windows paket i verzijske SQL skripte.

Mjerodavan ažurirani popis prihvatnih kriterija i preostalih funkcionalnosti je [BACKLOG.md](BACKLOG.md). Dokazi provjera su u [RELEASE-STATUS.md](RELEASE-STATUS.md).

Ako nema korisnika, pokretanje aplikacije kreira početni račun admin/admin s punom administracijom. Promjena početne lozinke nudi se pri svakoj prijavi dok je admin/admin u uporabi, uz mogućnost preskakanja. Postojeće lozinke i podaci ne resetiraju se.
