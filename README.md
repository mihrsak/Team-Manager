# Team Manager

Team Manager je web aplikacija na ASP.NET Core 10 i SQL Serveru za korisnike, timove, projekte i zadatke. Funkcionalni uzor je Redmine; implementacija je vlastita i njegov izvorni kod nije preuzet.

## Trenutačno izdanje

Razvojno izdanje 0.1. Ovo još nije potpuna zamjena za Redmine niti potvrđeno produkcijsko izdanje za banku. [Backlog](docs/BACKLOG.md) sadrži implementirane cjeline, ograničenja i preostali opseg.

## Implementirano

- Prijava brojem radnika, profil, funkcija i organizacijska jedinica.
- Prilagodljive role i ovlasti, globalne role i članstva u timovima/projektima.
- Prvi admin/admin samo u praznoj bazi i neobvezni podsjetnik za promjenu lozinke.
- Projektni zadaci s opisom, tipom, statusom, prioritetom, nositeljem i rokom.
- Šifrarnici i prijelazi statusa prema roli i tipu zadatka.
- Filtriranje, komentari, povijest izmjena i evidencija sati.
- Verzijske SQL migracije, eksplicitni demo podaci i Windows paket.

## Preuzimanje i instalacija

[Upute za instalaciju](docs/INSTALL.md) opisuju build, konfiguraciju, instalaciju baze i Windows/IIS. Postavke su u appsettings.Local.json ili varijablama okoline; lokalne postavke i tajne nisu dio repozitorija.

```powershell
dotnet build src/TeamManager.Web
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Windows.ps1
```

ZIP je u artifacts. GitHub Actions workflow također gradi Windows paket koji se preuzima iz artefakta uspješno završenog workflowa. Paket uključuje runtime, SQL skripte i Install-Database.ps1.

```powershell
# Iz mape raspakiranog paketa, nakon konfiguriranja veze:
powershell -NoProfile -ExecutionPolicy Bypass -File Install-Database.ps1 -CreateDatabase -Demo
```

Izostavite -CreateDatabase ako DBA već kreira bazu i -Demo ako ne želite probne podatke. Trenutačno je podržan SQL Server; Oracle, Db2 i OCP nalaze se u backlogu.

## Razvoj i provjere

- [Lokalna okolina](docs/LOCAL-SETUP.md)
- [Testovi](docs/TESTING.md)
- [Status provjera i ograničenja](docs/RELEASE-STATUS.md)
- [Backlog](docs/BACKLOG.md)

Baza je odvojena od aplikacije. Nadogradnja aplikacije ne resetira podatke ili lozinke; demo se učitava samo izričitom naredbom.
