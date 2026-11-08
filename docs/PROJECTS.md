# Postavke projekata (PROJ-02)

U navigaciji odaberite **Projekti**, zatim naziv projekta ili **Novi projekt**. Dostupni su naziv, opis, nadređeni projekt, vidljivost i stanje. Migracija `008-projects.sql` čuva postojeće projekte kao aktivne i privatne.

- Javni projekt znači čitanje svim prijavljenim korisnicima. Anoniman pristup nije omogućen. Pravo čitanja ne daje pravo komentiranja, unosa sati ili uređivanja.
- Privatni projekt vide korisnici s projektnom ovlasti pregleda zadataka ili upravljanja projektom i administratori.
- Stvaranje zahtijeva globalnu ovlast `organization.manage`. Kreator novog projekta dobiva projektnu rolu Voditelj. Uređivanje zahtijeva `projects.manage` na projektu ili punu administraciju. Migracija dodaje tu ovlast zadanoj roli Voditelj.
- Promjena nadređenog projekta zahtijeva upravljanje odabranim novim nadređenim projektom. Postojećeg nedostupnog roditelja moguće je zadržati bez otkrivanja njegova naziva. Kružne veze se odbijaju; istodobne promjene hijerarhije su serijalizirane.
- Hijerarhija ne nasljeđuje članstva, ovlasti, vidljivost ni stanje. Svaki potprojekt ima vlastite postavke.
- Zatvoren i arhiviran projekt zadržavaju pregled zadataka i povijesti. Kreiranje/uređivanje zadataka, komentari i unos vremena su blokirani, uključujući administratora. Ponovno aktiviranje vraća rad. Arhivirani projekti ostaju navedeni sa svojim stanjem, bez brisanja podataka.
- Korisnik s pravom upravljanja projektom može mijenjati njegove postavke i ponovno ga otvoriti. Upravljanje članovima zadržava zasebnu ovlast `members.manage`.
- Provjera revizije štiti od prepisivanja novije izmjene projekta zastarjelim obrascem.

Instalacija: pokrenuti uobičajenu `Install-Database.ps1` iz novog paketa (ili aplikaciju s `--apply-schema`) prije pokretanja novog izdanja. Ponovno pokretanje migracije ne resetira postavke.

## Moduli projekta (PROJ-03)

Na postojećem projektu odjeljak **Moduli projekta** zasebno sprema postavke zadataka/komentara i evidencije vremena. Potrebna je ista ovlast `projects.manage` kao za druge postavke projekta. Migracija `009-project-modules.sql` uključuje oba modula postojećim projektima; novi projekti također započinju s oba uključena.

Isključen modul zadataka uklanja zadatke iz popisa/pretrage i blokira izravne URL-ove i POST zahtjeve, čak i administratoru. Projektne postavke ostaju dostupne za ponovno uključivanje. Isključen modul vremena skriva tablicu sati, obrazac i vremenske zapise u povijesti te blokira upis sati. Komentari i zadaci nastavljaju raditi.

Vrijeme zahtijeva i uključen modul zadataka. Odabir vremena ostaje sačuvan kad se zadaci isključe. Isključivanje ne briše podatke; ponovno uključivanje vraća prethodne podatke. Ponovljena migracija ne mijenja odabire. Revizija projekta štiti i ove postavke od zastarjelog obrasca. Ne postoji automatsko nasljeđivanje postavki potprojektima.

Moduli koji još nisu implementirani (wiki, dokumenti i ostalo) ostaju u svojim backlog stavkama. Nasljeđivanje članstava i više rola zasebna su stavka ROLE-03.