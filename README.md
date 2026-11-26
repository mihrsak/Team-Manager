# Team Manager

Team Manager je web aplikacija na ASP.NET Core 10 za korisnike, timove, projekte i zadatke. Funkcionalni uzor je Redmine; implementacija je vlastita i njegov izvorni kod nije preuzet. Sučelje je na hrvatskom i engleskom jeziku.

## Gdje smo stali — 26. 11. 2026.

Posljednja dovršena funkcionalna revizija je [REV13](docs/PRODUCT-REVIEW-2026-11-26-R13.md): provjere ovlasti nakon zaključavanja za forum i grupe, očuvanje nacrta grupa/definicija i ograničene vidljivosti polja. [Backlog](docs/BACKLOG.md) i [status izdanja](docs/RELEASE-STATUS.md) navode točan implementirani opseg i dokaze. Lokalni IIS pregled je na https://localhost:7443; adresa vrijedi samo na računalu na kojem je instaliran.

Glavni moduli su implementirani: računi i prijave, prilagodljive role, višestruka članstva i grupe, projektna hijerarhija i moduli, zadaci i tijek rada, pretraga/upiti, planiranje, vrijeme, prilagodljiva polja, dokumenti, wiki, novosti, forum, Git pregled, obavijesti, e-pošta, API tokeni i revizijski zapis. Detaljna ograničenja pojedinih modula ostaju u backlogu; ovo nije tvrdnja o potpunoj jednakosti Redmineu.

## Baze i instalacija

| Okruženje | Trenutačni status |
|---|---|
| SQL Server | Implementiran; lokalni Windows/IIS, migracije i regresije provjereni. Zadani provider. |
| Oracle | Aktivni relacijski moduli i migracije lokalno provjereni; zasebna konfiguracija i postupak prijenosa. |
| Db2 LUW | Lokalno provjeren 12.1.5 s Oracle kompatibilnošću; [upute](docs/DB2-LOCAL.md). |
| Db2 z/OS | Nije potvrđen; potrebni su odgovarajuća licenca i ciljna provjera. |
| OpenShift | Pripremljeni predlošci i lokalno provjerena Linux slika/proxy; ciljni klaster ostaje za provjeru. |

[Instalacijske upute](docs/INSTALL.md) razlikuju SQL Server instalaciju od Oracle/Db2 postupaka. Postavke su u `appsettings.Local.json` ili varijablama okoline; tajne i lokalne postavke nisu dio repozitorija. Baza i trajni podaci odvojeni su od aplikacijskog paketa.

```powershell
dotnet build src/TeamManager.Web
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Windows.ps1
```

Windows ZIP je u `artifacts`. [GitHub Actions](https://github.com/mihrsak/Team-Manager/actions/workflows/build.yml) također izrađuje paket `TeamManager-Windows`. Paket uključuje runtime, migracije, instalacijsku skriptu, primjer konfiguracije i dokumentaciju. SQL Server instalacija iz raspakiranog paketa, nakon konfiguriranja veze:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Install-Database.ps1 -CreateDatabase
```

Izostavite `-CreateDatabase` ako DBA već kreira bazu. `-Demo` dodajte samo za izričito učitavanje probnih podataka. Oracle/Db2 koriste svoje opisane postupke, ne ovu SQL Server naredbu. Prvi račun `admin/admin` nastaje samo u praznoj instalaciji; prijava nudi promjenu lozinke. Nadogradnja ne resetira postojeće korisnike ili podatke.

## Sljedeći koraci

Lokalno dovršene funkcionalnosti ne znače dovršen prihvat u banci. Otvoreni su povezivanje i provjera namjenskog sandučića, bankarski SSO/AD, ciljni IIS ili OpenShift, obnova/rollback i nadzor u ciljnom okruženju, produkcijski kapacitet te Db2 z/OS ako se odabere. [Plan ciljnog prihvata](docs/TARGET-ACCEPTANCE.md) navodi potrebne ulaze i kriterije bez označavanja tih provjera dovršenima.

- [Lokalna okolina](docs/LOCAL-SETUP.md)
- [Testovi](docs/TESTING.md)
- [Instalacija i održavanje](docs/INSTALL.md)
- [Backlog](docs/BACKLOG.md)