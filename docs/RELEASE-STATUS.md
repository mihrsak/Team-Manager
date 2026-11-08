# Razvojno izdanje 0.1 — 2026-11-08

Implementirani su korisnici, organizacija, role, projektni zadaci, komentari, povijest, unos sati i prilagodljivi šifrarnici/tijek rada. Demo projekt s tri zadatka dostupan je nakon eksplicitnog seedanja. Cijeli Redmine funkcionalni opseg nije dovršen; mjerodavan je BACKLOG.md.

## Dokazi provjera

- Build: 0 grešaka, 0 upozorenja.
- Test-Bootstrap.ps1: 7 prolaznih provjera.
- Test-Users.ps1: 16 prolaznih provjera.
- Test-Issues.ps1: 35 prolaznih provjera, uključujući workflow, projektne postavke, hijerarhiju, vidljivost, životni ciklus i ponavljanje instalacije nakon uređivanja.
- Test-Package.ps1: 3 prolazne provjere stvarnog self-contained Windows paketa u Production konfiguraciji protiv privremene SQL baze.
- Lokalni server pokrenut na https://localhost:7043.
- Automatizirana vizualna provjera nije dovršena: ugrađeni preglednik pri dohvaćanju postojeće kartice prikazao je ERR_CONNECTION_REFUSED. Lokalni HTTPS zahtjevi i HTTP integracijski testovi su prošli.

GitHub workflow radi build i publish paket; ne izvršava lokalne SQL integracijske skripte. Cijela aplikacija još nije potvrđena za produkcijsko bankarsko okruženje.

## Nastavak

PROJ-02 dovršen. Sljedeće PROJ-03 i ROLE-03; zatim planiranje, izvještaji, dokumenti/wiki, obavijesti i produkcijske provjere. Ta područja nisu zamijenjena praznim gumbima ili označena gotovima.
