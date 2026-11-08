# Lokalno pokretanje

Okolina: .NET 10 SDK u `.tools/dotnet`, SQL Server 2025 Express instanca `localhost\SQLEXPRESS`, Windows prijava.

## Baza

Iz korijena projekta pokrenite:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Initialize-Local.ps1 -Demo
```

Skripta kreira bazu ako ne postoji i primjenjuje verzioniranu strukturu. Ponovljeno pokretanje ne briše podatke. `-Demo` dodaje neobavezne probne podatke; izostavite ga za praznu bazu.

## HTTPS i aplikacija

```powershell
.\.tools\dotnet\dotnet.exe dev-certs https --trust
```

Potvrdite Windowsov zahtjev za povjerenje lokalnom razvojnom certifikatu. Zatim dvokliknite `Start-TeamManager.cmd` i otvorite https://localhost:7043. HTTP je dostupan na http://localhost:5043. Zatvaranje prozora zaustavlja aplikaciju, a baza ostaje spremljena u SQL Serveru.

Početna stranica vodi na prijavu i imenik korisnika. Implementirani su korisnički profili, organizacijske jedinice, članstva u timovima/projektima te prilagodljive role. Plan ostalih modula nalazi se u PRODUCT-PLAN.md. Ovo je lokalna razvojna verzija.

Nakon inicijalizacije baze aplikacija pri pokretanju automatski kreira `admin` / `admin` samo ako tablica korisnika nema nijedan redak. Račun dobiva punu administratorsku rolu, a lozinka se sprema kao hash. Ako korisnici već postoje, ništa se ne dodaje niti resetira.

Pri svakoj prijavi računom `admin` s lozinkom `admin` nudi se promjena lozinke ili nastavak bez promjene. Nakon promjene podsjetnik prestaje. Postojeći lokalni računi ostaju sačuvani.

Za eksplicitnu pripremu početnog računa bez pokretanja web servera u razvojnoj okolini može se koristiti i:

```powershell
cd src\TeamManager.Web
$env:ASPNETCORE_ENVIRONMENT='Development'
..\..\.tools\dotnet\dotnet.exe run -- --bootstrap-local-user
```

Naredba koristi isto pravilo prazne tablice korisnika. Role uređujete kroz poveznicu Role i ovlasti, a globalne role i članstva dodjeljujete na profilu korisnika. Funkcija je opis radnog mjesta, odvojena od sigurnosnih ovlasti.

## Kasnija instalacija u banci

Predviđen je Windows/IIS, zasebna SQL baza i certifikat banke. Skripte u `database` služe eksplicitnom postavljanju baze; normalno pokretanje aplikacije ih ne izvršava. Produkcijska veza postavlja se kroz `ConnectionStrings__TeamManager`; povjerenje samopotpisanom SQL certifikatu iz lokalnih postavki ne prenosi se u produkciju.

Redmine služi kao funkcionalna referenca za uloge, tijek zadataka i povijest promjena. Njegov kod nije preuzet.
