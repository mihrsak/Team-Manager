namespace TeamManager.Web;

public static class PageLabels
{
    public static string For(string? page)=>page switch
    {
        "/Login"=>"Prijava", "/LdapLogin"=>"LDAP prijava", "/Dashboard"=>"Moji zadaci",
        "/Search"=>"Pretraga", "/Projects"=>"Projekti", "/Project"=>"Projekt",
        "/Issues"=>"Zadaci", "/Issue"=>"Zadatak", "/Calendar"=>"Kalendar", "/Gantt"=>"Gantt",
        "/Activity"=>"Aktivnosti", "/Notifications"=>"Obavijesti", "/TimeReport"=>"Izvještaj o vremenu",
        "/Profile"=>"Moj profil", "/Users"=>"Korisnici", "/Roles"=>"Role i ovlasti", "/Workflow"=>"Tijek rada",
        "/Roadmap"=>"Karta planova", "/ProjectNews"=>"Novosti", "/Wiki"=>"Wiki", "/Forum"=>"Forum",
        "/ProjectDocuments"=>"Dokumenti", "/Repositories"=>"Git repozitoriji", "/RepositoryHistory"=>"Git repozitoriji",
        "/Groups"=>"Grupe korisnika", "/AdministrationAudit"=>"Revizijski zapis", "/ApiTokens"=>"API tokeni",
        "/EmailNotifications"=>"Obavijesti e-poštom", "/MailIntake"=>"Ulazna e-pošta", _=>"Team Manager"
    };
}
