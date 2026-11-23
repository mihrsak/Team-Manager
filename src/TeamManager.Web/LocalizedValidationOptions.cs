using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace TeamManager.Web;

// Metadata is cached globally. Resolve display names/messages when used, rather
// than capturing the language of the first request that builds the metadata.
public sealed class LocalizedValidationOptions(IStringLocalizer<UiText> text) : IConfigureOptions<MvcOptions>
{
    private static readonly Dictionary<string,string> Labels=new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hours"]="Sati",["Activity"]="Aktivnost",["Note"]="Napomena",["Date"]="Datum",
        ["Title"]="Naslov",["Description"]="Opis",["Name"]="Naziv",["Type"]="Tip",
        ["Priority"]="Prioritet",["Status"]="Status",["EmployeeNumber"]="Broj radnika",
        ["Password"]="Lozinka",["Query"]="Pretraga",["From"]="Od datuma",["To"]="Do datuma",
        ["Person"]="Osoba",["ProjectId"]="Projekt",["IssueId"]="Zadatak",["Id"]="Identifikator",
        ["Month"]="Mjesec",["PageNumber"]="Stranica",["Revision"]="Verzija",
        ["Body"]="Sadržaj",["Text"]="Tekst",["Kind"]="Vrsta"
    };
    private static string Label(string name)=>Labels.GetValueOrDefault(name,name);

    public void Configure(MvcOptions options)
    {
        options.ModelMetadataDetailsProviders.Add(new Metadata(text));
        var messages=options.ModelBindingMessageProvider;
        messages.SetAttemptedValueIsInvalidAccessor((value,field)=>text["Vrijednost '{0}' nije valjana za polje {1}.",value,text[Label(field)].Value]);
        messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value=>text["Vrijednost '{0}' nije valjana.",value]);
        messages.SetUnknownValueIsInvalidAccessor(field=>text["Unesi valjanu vrijednost za polje {0}.",text[Label(field)].Value]);
        messages.SetNonPropertyUnknownValueIsInvalidAccessor(()=>text["Unesi valjanu vrijednost."]);
        messages.SetValueIsInvalidAccessor(value=>text["Vrijednost '{0}' nije valjana.",value]);
        messages.SetValueMustNotBeNullAccessor(_=>text["Obavezno polje nije popunjeno."]);
        messages.SetValueMustBeANumberAccessor(field=>text["Polje {0} mora biti broj.",text[Label(field)].Value]);
        messages.SetNonPropertyValueMustBeANumberAccessor(()=>text["Unesi valjan broj."]);
        messages.SetMissingBindRequiredValueAccessor(field=>text["Polje {0} je obavezno.",text[Label(field)].Value]);
        messages.SetMissingKeyOrValueAccessor(()=>text["Nedostaje obavezna vrijednost."]);
        messages.SetMissingRequestBodyRequiredValueAccessor(()=>text["Nedostaje sadržaj zahtjeva."]);
    }

    private sealed class Metadata(IStringLocalizer<UiText> text) : IDisplayMetadataProvider,IValidationMetadataProvider
    {
        public void CreateDisplayMetadata(DisplayMetadataProviderContext context)
        {
            if(context.DisplayMetadata.DisplayName is null&&context.Key.Name is string name&&Labels.TryGetValue(name,out var label))
                context.DisplayMetadata.DisplayName=()=>text[label].Value;
        }
        public void CreateValidationMetadata(ValidationMetadataProviderContext context)
        {
            foreach(var attribute in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
            {
                if(attribute.ErrorMessage is not null||attribute.ErrorMessageResourceName is not null)continue;
                attribute.ErrorMessage=attribute switch
                {
                    RequiredAttribute=>"Polje {0} je obavezno.",
                    StringLengthAttribute length when length.MinimumLength>0=>"Polje {0} mora imati od {2} do {1} znakova.",
                    StringLengthAttribute=>"Polje {0} može imati najviše {1} znakova.",
                    _=>null
                };
            }
        }
    }
}
