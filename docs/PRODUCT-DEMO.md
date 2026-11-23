# Demo za reviziju proizvoda

Završni pregled od 22. 11. 2026.: [https://localhost:7610/Projects](https://localhost:7610/Projects). Lokalni demonstracijski administrator: `admin` / `admin`. Ovaj račun pripada isključivo lokalnoj demo bazi. Demo korisnici `demo.lead`, `demo.dev` i `demo.viewer` imaju generiranu lozinku u ignoriranoj datoteci `LocalDemoAccess.private.json`; ne spremati je u Git niti koristiti za stvarne račune.

Postojeći radni podaci ostaju dostupni odvojeno na [https://localhost:7589/Projects](https://localhost:7589/Projects). Oba pregleda koriste konačni paket `artifacts/rev2-polish-final` i lokalni dotnet HTTPS proces. Završni paket nije objavljen na IIS u ovoj sesiji jer proces nema Windows administratorski token.

## Sadržaj i tokovi za pregled

- **Portal za zaposlenike / Employee portal**: četiri zadatka u različitim statusima i prioritetima, dodijeljena razvojnom korisniku, verzija 1.0, UAT prekretnica i napredak prema statusima.
- **Interni procesi / Internal processes**: privatni projekt s dva zadatka; voditelj i razvojni korisnik imaju članstvo, promatrač nema pristup. Privatno članstvo nije vidljivo ni kroz tuđi profil.
- **Podrška i održavanje / Support and maintenance**: dva zadatka za primjer kontinuiranog rada.
- Portal uključuje wiki s dvije revizije, dokument za preuzimanje, novost, temu i odgovor foruma, tri unosa vremena te obavijest praćenog zadatka za razvojni račun. Izvještaj vremena vodi na detalje/ispravak unosa.
- Promjena jezika u profilu prebacuje sistemsko sučelje HR/EN. Primjeri korisničkog sadržaja imaju usporedni hrvatski i engleski tekst. Četiri početna prioriteta predstavljaju hitnost; blokiranje/otkazivanje pripada statusu, a rang i aktivnost kataloga uređuju se u administraciji tijeka rada.

## Ponovljivo stvaranje bez resetiranja podataka

Preduvjeti: .NET 10, lokalni SQL Server Express, pokrenut lokalni Db2 LUW, postojeća ignorirana konfiguracija s Db2 pristupom, Playwright/Edge i axe u `artifacts/qa-browser`, pouzdan lokalni HTTPS certifikat te njegova javna PEM kopija `dev-ca.pem` uz objavljeni DLL. Privatni certifikacijski ključ nije potreban Node testu. Portovi moraju biti slobodni.

Iz korijena repozitorija:

```powershell
./scripts/New-ProductDemo.ps1 `
  -ApplicationDll artifacts/rev2-polish-final/TeamManager.Web.dll `
  -Db2SettingsPath artifacts/db2-complete-final/appsettings.Local.json `
  -Port 7610 -SeedPort 7614
```

Skripta svaki put generira **novu** SQL bazu `TeamManager_ProductDemo_<GUID>` i Db2 shemu `TMR_DEMO_<12 hex>`. SQL izvor služi za pripremu reprezentativnih podataka; serijalizabilna snimka zatim se uvozi u novu relacijsku Db2 shemu i neovisno uspoređuje. Postojeće baze/sheme se ne brišu i ne prepisuju. Privremeni SQL web proces zaustavlja se nakon pripreme, a izvorna baza ostaje sačuvana. Stvaranje sheme dopušteno je samo prema loopback Db2 adresi i generiranom demo nazivu. Stari demo proces treba zaustaviti prije ponovne uporabe istog porta ili odabrati drugi port.

Svaki pokušaj ostavlja zasebnu ignoriranu mapu `artifacts/product-demo-<GUID>`: konfiguraciju, manifest ID-ova, privatnu snimku, generirane pristupne podatke, PID procesa i dnevnike. Ne objavljivati cijelu mapu niti snimku jer sadrži privatne podatke i hashove lozinki. Statički sadržaj čita se iz `wwwroot` objavljenog paketa, a konfiguracija iz demo mape.

Provjera postojećeg demoa:

```powershell
$env:NODE_EXTRA_CA_CERTS=(Resolve-Path artifacts/rev2-polish-final/dev-ca.pem).Path
node scripts/Test-ProductDemo.cjs artifacts/product-demo-<GUID>
```

## Dokazi i ograničenja

Dva neovisna stvaranja i uvoza prošla su usporedbu po **233 zapisa u 51 tablici**. Konačni drugi demo prošao je **28 HR/EN WCAG A/AA axe provjera**, sadržaj glavnih modula, poveznice detalja vremena, odvajanje projektnih ovlasti, privatnost profila i obavijesti. To nije nova ručna NVDA potvrda.

Novi proces provjeren je prvo na hrvatskom jeziku. Izvještaj vremena eksplicitno vraća `DECIMAL(20,2)` kako inicijalizacija Db2 DECFLOAT tipa ne bi ovisila o regionalnom formatu upravljačkog programa. Postojeći Db2 pregled također je nadograđen aditivnom migracijom na relacijsku verziju 11 i prošao je `Test-ReviewSmoke.cjs`, uz očuvanje postojeće konfiguracije i poslovnih podataka. Raniji Db2 Linux web testni kontejner zaustavljen je nakon promjene sheme; njegova slika i podaci nisu uklonjeni. Ranije container/proxy potvrde ne predstavljaju novu provjeru ovog Windows paketa.

Za produkciju ostaju zasebne provjere ciljnog IIS-a/OpenShifta, bankarskog SSO/AD-a, stvarnog sandučića, kapaciteta te Db2 z/OS-a i licence. Lokalno zatvaranje REV-01–REV-14 ne zatvara te vanjske uvjete prihvata.
