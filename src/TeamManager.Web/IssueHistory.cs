using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace TeamManager.Web;

public sealed record HistoryChange(string Field, string Before, string After);

public static class IssueHistory
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["Title"]="Naslov", ["Description"]="Opis", ["Type"]="Tip", ["Priority"]="Prioritet",
        ["Status"]="Status", ["AssigneeId"]="Nositelj", ["IsPrivate"]="Privatni zadatak",
        ["RoadmapItemId"]="Verzija ili prekretnica", ["StartDate"]="Početak", ["DueDate"]="Rok",
        ["Date"]="Datum", ["Hours"]="Sati", ["Activity"]="Aktivnost", ["Note"]="Napomena"
    };

    // Presentation only: the stored audit body and concurrency revision are never rewritten.
    public static IReadOnlyList<HistoryChange>? Read(string body, IReadOnlyDictionary<int,string> people,
        IReadOnlyDictionary<int,string> roadmap, IStringLocalizer<UiText> text, IReadOnlyDictionary<int,string>? projects=null)
    {
        try
        {
            using var document=JsonDocument.Parse(body);
            var root=document.RootElement;
            if(root.ValueKind==JsonValueKind.Object && root.TryGetProperty("FromProjectId",out var source) && root.TryGetProperty("ToProjectId",out var destination))
                return [new(text["Projekt"],ProjectName(source),ProjectName(destination))];
            if(root.ValueKind!=JsonValueKind.Object || !root.TryGetProperty("After",out var after) || after.ValueKind!=JsonValueKind.Object)
                return null;
            root.TryGetProperty("Before",out var before);
            // Existing issue events store the previous snapshot as an embedded JSON string;
            // time corrections store it directly as an object. Support both without changing either.
            if(before.ValueKind==JsonValueKind.String)
            {
                using var snapshot=JsonDocument.Parse(before.GetString()!);
                before=snapshot.RootElement.Clone();
            }
            if(before.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null or JsonValueKind.Undefined)) return null;
            var changes=new List<HistoryChange>();
            foreach(var (field,label) in Labels)
            {
                var old=before.ValueKind==JsonValueKind.Object && before.TryGetProperty(field,out var oldValue)?oldValue:default;
                var current=after.TryGetProperty(field,out var newValue)?newValue:default;
                if(Empty(old)&&Empty(current)) continue;
                if(old.ValueKind!=JsonValueKind.Undefined && current.ValueKind!=JsonValueKind.Undefined && JsonElement.DeepEquals(old,current)) continue;
                var oldText=Value(field,old); var newText=Value(field,current);
                if(field is "Date" or "StartDate" or "DueDate" or "Hours" && oldText==newText) continue;
                changes.Add(new(text[label],oldText,newText));
            }
            return changes;

            string ProjectName(JsonElement value)=>value.ValueKind==JsonValueKind.Number && value.TryGetInt32(out var id)
                ? projects is not null && projects.TryGetValue(id,out var name)?name:text["Nedostupno (#{0})",id]
                : text["Nije postavljeno"];

            string Value(string field,JsonElement value)
            {
                if(Empty(value)) return text["Nije postavljeno"];
                if(field is "AssigneeId" or "RoadmapItemId" && value.ValueKind==JsonValueKind.Number && value.TryGetInt32(out var id))
                {
                    var names=field=="AssigneeId"?people:roadmap;
                    return names.TryGetValue(id,out var name)?name:text["Nedostupno (#{0})",id];
                }
                if(value.ValueKind is JsonValueKind.True or JsonValueKind.False) return text[value.GetBoolean()?"Da":"Ne"];
                if(field is "Date" or "StartDate" or "DueDate" && value.ValueKind==JsonValueKind.String &&
                    DateTime.TryParse(value.GetString(),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var date))
                    return date.ToString("d",CultureInfo.CurrentCulture);
                if(field=="Hours" && value.ValueKind==JsonValueKind.Number && value.TryGetDecimal(out var hours)) return hours.ToString("0.##",CultureInfo.CurrentCulture);
                return value.ValueKind==JsonValueKind.String?value.GetString()!:value.GetRawText();
            }
        }
        catch(JsonException) { return null; }
    }

    private static bool Empty(JsonElement value)=>value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
}
