# Završetak lokalne faze — 26. 11. 2026.

**Status: lokalni prihvat zatvoren za dokumentirani implementirani opseg.** U aktualnom backlogu nema evidentiranih neriješenih obveznih lokalnih nalaza. Ne otvara se novi krug testiranja samo zato što se mogu zamisliti dodatni testovi. To nije tvrdnja da aplikacija nema grešaka, da je potpuno jednaka Redmineu ili da je bankarski produkcijski prihvat završen.

Ovaj zapis objedinjuje postojeće rezultate; cijeli skup testova nije ponovno izvođen pri njegovu pisanju. Posljednja funkcionalna promjena aplikacije je `f875ecc` (REV13). Do `a101388` nakon nje mijenjani su paketne/testne skripte, CI i dokumentacija; nema promjena u `src` ili `database` u tom rasponu. Ranije dokumentirana ograničenja funkcionalnosti ostaju važeća.

## Dokazi zatvaranja

| Područje | Dokaz i granica provjere |
|---|---|
| Funkcionalnosti i ovlasti | [REV13](PRODUCT-REVIEW-2026-11-26-R13.md): zatvoreni nalazi foruma/grupa/definicijskih nacrta, HR/EN provjere na izoliranim SQL Server, Oracle i Db2 bazama te IIS-u. Kontrolirani race slučajevi imaju zasebno navedeni uži SQL opseg. |
| Lokalni IIS i preglednik | REV13 završni logovi potvrđuju glavne HR/EN tokove i 82 prikaza bez novih axe prijava, dokumentnog prelijevanja i JavaScript grešaka. To nije pokrivenost svakog mogućeg korisničkog puta. |
| Pristupačnost i opterećenje | [IIS/NVDA prihvat 25. 11.](LOCAL-IIS-NVDA-2026-11-25.md) sadrži ručne/govorne NVDA provjere i lokalna mjerenja; nisu ponovno izvođeni za paketne dorade. Lokalno mjerenje ne predstavlja produkcijski kapacitet. |
| Distribucija i migracije | DELIV-01 i QA-LOCAL-04: instalacija bez demoa, izričito demo učitavanje i ponavljanje bez duplikata, provjeren ZIP, 54 migracijska asseta i pet negativnih ZIP scenarija. [GitHub build i provjere arhive](https://github.com/mihrsak/Team-Manager/actions/runs/45856903086) za `a101388` uspješno su završili. |
| Obnova i rollback | OPS-LOCAL-04: stvarna obnova u odvojenu SQL bazu/datoteke, SHA-256 usporedba šest tablica uključujući binarni sadržaj, prijava/readiness, nadogradnja i povratak. Ne tvrdi se provjera svake tablice ni produkcijskog RTO-a. |
| Privremena nedostupnost baze | OPS-LOCAL-05: zasebna SQL testna baza OFFLINE/ONLINE, readiness 503 uz liveness 200 te povratak na 200 u istom procesu. Nije test ispada mreže ili cijelog klastera. |

Lokalni operativni dokazi su `artifacts/local-recovery-20261126.log` i `artifacts/local-health-recovery-20261126.log`; preglednički `artifacts/review13-iis-acceptance.log` i `artifacts/review13-overview-final.log`. Ti lokalni artefakti nisu sadržaj Git repozitorija; reproducibilne skripte i sažeci rezultata jesu. Lokalni pregledi i poslovni podaci nisu promijenjeni konsolidacijom.

## Koliko još testiranja?

- **Lokalno:** nema evidentiranih otvorenih obveznih stavki za sadašnji implementirani opseg. Kontinuirane provjere uz buduće promjene nisu novi beskonačni backlog.
- **Ciljno okruženje:** ostaje **sedam područja** u [planu ciljnog prihvata](TARGET-ACCEPTANCE.md), ne sedam pojedinačnih testova. To su sandučić, bankarski SSO/AD, ciljni IIS, ciljni OpenShift, obnova/nadzor, Db2 z/OS i produkcijski funkcionalni/kapacitetni prihvat. Primjenjuju se samo odabrana odredišta i integracije; OpenShift ili z/OS nisu uvjet ako ih se ne koristi.
- **Rok ciljnog prihvata:** nije procijenjen bez odabira odredišta, pristupa, testnih računa i dogovorenih kriterija. Broj lokalno odrađenih testova ne može zamijeniti te ulaze.

## Kada ponovno otvaramo lokalnu provjeru?

Nova funkcionalnost, promjena koda/migracije/ovisnosti/konfiguracije ili reproducirana prijavljena greška otvara ciljane testove i povezanu regresiju. Kad relevantne provjere prođu i potvrđeni nalazi budu popravljeni, stavka se zatvara i šalje na GitHub. Širi regresijski krug pokreće se ako promjena ili nalaz to opravdava. Ne ponavlja se cijeli prihvat radi obične dokumentacijske promjene.

Sljedeća poslovna faza je korisnički pilot nad probnim podacima i priprema odabranog ciljnog okruženja, uz evidentiranje konkretnih novih nalaza ako se pojave.
