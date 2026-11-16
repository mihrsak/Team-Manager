# Team Manager REST API

API v1 nudi čitanje projekata i zadataka. Lokalna osnovna adresa je `https://localhost:7043/api/v1`. Odgovori s podacima su JSON.

## Pristup

Na **Moj profil → API tokeni** izradite imenovani token. Puna vrijednost prikazuje se pri izradi. Pohranite je na mjesto namijenjeno tajnama. Šaljite token u zaglavlju `Authorization: Bearer …` preko HTTPS-a.

Token vrijedi 30 dana. Opoziv tokena, deaktivacija računa, promjena ili reset lozinke te opoziv sesija poništavaju njegov pristup. Za svaki zahtjev primjenjuju se aktualne ovlasti korisnika, uključujući vidljivost projekata i privatnih zadataka.

## Rute

| Metoda i putanja | Rezultat |
|---|---|
| `GET /api/v1/projects` | Dostupni projekti s identifikatorom, nazivom, opisom i stanjem. |
| `GET /api/v1/projects/{projectId}/issues` | Do 100 dostupnih zadataka, od najnovijeg ID-ja prema najstarijem. Svaki red sadrži identifikator, naslov, status, prioritet i rok. |
| `GET /api/v1/projects/{projectId}/issues?beforeId={id}` | Sljedeća stranica zadataka sa strogo manjim ID-jem od dostavljene vrijednosti. Upotrijebite ID zadnjeg retka prethodne stranice. |
| `GET /api/v1/issues/{id}` | Podaci dostupnog zadatka, uključujući projekt, opis, tip, nositelja i datume. |

Nedostajući, nevažeći i opozvani tokeni vraćaju `401`. Nedostupni projekti i zadaci vraćaju `404`. Uspješni odgovori imaju `200`, a prazan popis vraća `[]`.

```powershell
Invoke-RestMethod 'https://localhost:7043/api/v1/projects' -Headers @{
    Authorization = "Bearer $env:TEAMMANAGER_API_TOKEN"
}
```
