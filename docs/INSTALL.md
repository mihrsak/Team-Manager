# Instalacija Team Managera

Status: razvojno izdanje. Aktualni implementirani opseg i preostali moduli zapisani su u docs/BACKLOG.md. Podržana baza ovog izdanja je SQL Server. Oracle/Db2/OCP nisu još implementirani.

## Iz izvornog koda

1. Instalirajte Git, .NET 10 SDK i SQL Server (lokalno može Express).
2. Klonirajte repozitorij i pokrenite `dotnet build src/TeamManager.Web`.
3. Iz korijena pokrenite `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Windows.ps1`.
4. ZIP u `artifacts` sadrži aplikaciju, SQL skripte, instalacijsku skriptu i primjer konfiguracije. Paket win-x64 uključuje .NET runtime.

## Konfiguracija

Raspakirajte paket u trajnu mapu aplikacije. Kopirajte `appsettings.Local.example.json` u `appsettings.Local.json`. Unesite SQL server, naziv baze i DNS naziv aplikacije u AllowedHosts.

Alternativa je varijabla okoline `ConnectionStrings__TeamManager`. Varijable imaju prednost nad JSON postavkama. Lozinke se ne spremaju u GitHub. Windows Integrated Security koristi identitet procesa aplikacije; taj identitet mora imati pristup bazi. SQL korisnik aplikacije treba SELECT/INSERT/UPDATE/DELETE nad aplikacijskim tablicama; instalaciju/nadogradnju izvršite zasebnim identitetom s DDL pravima.

Produkcijska veza provjerava SQL certifikat (`Encrypt=True;TrustServerCertificate=False`). Za lokalni SQL Express razvoj može se koristiti TrustServerCertificate=True. Certifikat banke i servername moraju odgovarati.

## Instalacija i nadogradnja baze

Iz mape paketa:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Install-Database.ps1 -CreateDatabase
```

Ako DBA unaprijed kreira praznu bazu, izostavite `-CreateDatabase`. Nadogradnja koristi istu naredbu bez tog prekidača. Migracije vode verziju u SchemaVersions; ponovljeno izvršavanje ne prazni tablice. Instalacijske skripte nisu zamjena za backup prije nadogradnje.

Za neobavezne probne podatke dodajte `-Demo`. Demo se ne učitava pri običnom pokretanju aplikacije. Prva prazna instalacija dobiva admin/admin; prijava nudi neobveznu promjenu lozinke. Postojeći korisnici nisu izmijenjeni. Ne koristite demo podatke kao stvarne bankarske podatke.

## Windows / IIS

Službena referenca: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0

1. Instalirajte IIS i službeni ASP.NET Core 10 Hosting Bundle. Self-contained paket i dalje zahtijeva ASP.NET Core Module za IIS.
2. Kreirajte application pool, No Managed Code, s namjenskim Windows identitetom.
3. Dodijelite čitanje mape aplikacije tom identitetu i potrebne SQL ovlasti.
4. U IIS-u kreirajte site s fizičkom putanjom paketa i HTTPS bindingom na certifikat za DNS naziv iz AllowedHosts. Publish generira web.config.
5. Postavite ASPNETCORE_ENVIRONMENT=Production. Provjerite HTTPS, prijavu i prikaz podataka.

Lokalno pokretanje iz izvornog koda: `dotnet dev-certs https --trust`, ASPNETCORE_ENVIRONMENT=Development, konfigurirana veza na bazu, pa `dotnet run --project src/TeamManager.Web --urls https://localhost:7043`.

## Podaci i nadogradnja aplikacije

Baza je izvan mape aplikacije. Sačuvajte appsettings.Local.json i ASP.NET Data Protection ključeve identiteta procesa pri nadogradnji. Zaustavite application pool, napravite backup baze i postavki, primijenite migracije, zamijenite aplikacijske datoteke, vratite lokalnu konfiguraciju i pokrenite pool. Povratak na staru verziju baze radi se iz provjerenog backupa, ne brisanjem tablica.

SQL backup/restore, IIS instalacija u banci, vanjski certifikati, AD/LDAP i opterećenje zahtijevaju provjeru u ciljnom okruženju; nisu potvrđeni lokalnim testovima.
