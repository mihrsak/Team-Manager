using Microsoft.Extensions.Localization;
using TeamManager.Web.Pages;

namespace TeamManager.Web;

public static class PageLabels
{
    public static string For(string? page)=>page switch
    {
        "/Login"=>"Prijava", "/LdapLogin"=>"LDAP prijava", "/Dashboard"=>"Moji zadaci",
        "/NotFound"=>"Stranica nije dostupna.",
        "/Denied"=>"Pristup odbijen", "/Error"=>"Zahtjev nije uspio.",
        "/CustomFields"=>"Prilagodljiva polja", "/IssueFields"=>"Polja zadataka",
        "/RegistrationRequests"=>"Zahtjevi za registraciju",
        "/Search"=>"Pretraga", "/Projects"=>"Projekti", "/Project"=>"Projekt",
        "/Issues"=>"Zadaci", "/Issue"=>"Zadatak", "/Calendar"=>"Kalendar", "/Gantt"=>"Gantt",
        "/Activity"=>"Aktivnosti", "/Notifications"=>"Obavijesti", "/TimeReport"=>"Izvještaj o vremenu", "/TimeEntry"=>"Detalji unosa vremena",
        "/Profile"=>"Moj profil", "/Users"=>"Korisnici", "/Roles"=>"Role i ovlasti", "/Workflow"=>"Tijek rada",
        "/Roadmap"=>"Karta planova", "/ProjectNews"=>"Novosti", "/Wiki"=>"Wiki", "/Forum"=>"Forum",
        "/ProjectDocuments"=>"Dokumenti", "/Repositories"=>"Git repozitoriji", "/RepositoryHistory"=>"Git repozitoriji",
        "/Groups"=>"Grupe korisnika", "/AdministrationAudit"=>"Revizijski zapis", "/ApiTokens"=>"API tokeni",
        "/EmailNotifications"=>"Obavijesti e-poštom", "/MailIntake"=>"Ulazna e-pošta", _=>"Team Manager"
    };

    public static string Title(string? page,object? model,IStringLocalizer<UiText> text)
    {
        if(page=="/Profile" && model is ProfileModel {Person: {} person} profile)
            return text[profile.IsSelf?"Moj profil":"Profil korisnika"].Value+" · "+person.DisplayName;
        if(page=="/CustomFields" && model is CustomFieldsModel fields)
        {
            var scope=fields.Scope switch {"project"=>"Polja projekta","time"=>"Polja unosa vremena",_=>"Polja korisnika"};
            return fields.EntityId is null
                ? text["Definicije prilagodljivih polja"].Value+" · "+text[scope].Value
                : text[scope].Value+(fields.Target is {} target?" · "+target.Title:"");
        }
        if(page=="/IssueFields" && model is IssueFieldsModel {Project: {} project})
            return text["Polja zadataka"].Value+" · "+project.Input.Name;
        return text[For(page)].Value;
    }
}
