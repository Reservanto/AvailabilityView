using AvailabilityView.Application.Availability;

namespace AvailabilityView.Web.Availability;

/// <summary>
/// Formulář filtrů z <c>pickers</c>. Metoda GET bez adresy cíle odešle volby na stejnou stránku,
/// funguje tak i bez JavaScriptu a pod podadresářem IIS. Možnosti jsou jen Id a název veřejných objektů.
/// </summary>
/// <param name="Fields">Filtry v pořadí závislosti.</param>
/// <param name="Kept">Ostatní parametry adresy, i neznámé. Formulář je pošle skryté, aby zůstal stejný pohled.</param>
public sealed record FilterForm(IReadOnlyList<FilterField> Fields, IReadOnlyList<KeptParameter> Kept)
{
    /// <summary>Formulář z možností filtrů ve výsledku čtení, <c>null</c> = žádný filtr.</summary>
    /// <param name="request">Co návštěvník chce vidět.</param>
    /// <param name="filters">Možnosti filtrů z <see cref="AvailabilityResult.Filters"/>.</param>
    internal static FilterForm? Of(VisitorRequest request, IReadOnlyList<FilterChoices> filters)
    {
        if (filters.Count == 0)
        {
            return null;
        }

        // Hodnoty všech filtrů posílá formulář sám, i hodnotu nižšího filtru, která po změně vyššího k němu nepatří.
        // Čtení ji zahodí a doplní znovu (SPEC sekce 5), takže změna zaměření nevede na „pohled není dostupný“.
        string[] names = [.. filters.Select(filter => VisitorParameters.NameOf(filter.Filter))];
        return new FilterForm(
            [.. filters.Select(FilterField.Of)],
            [.. request.ParametersExcept(names).Select(pair => new KeptParameter(pair.Key, pair.Value))]);
    }
}

/// <summary>Jeden filtr formuláře.</summary>
/// <param name="Filter">Filtr, jeho popisek je v textech pod <c>Visitor.Filter.{hodnota}</c>.</param>
/// <param name="Name">Název parametru adresy.</param>
/// <param name="AllowsAll">Nabízí se i volba „všechny“ s prázdnou hodnotou. Zvolená je, když žádná možnost zvolená není.</param>
/// <param name="Options">Možnosti v pořadí z API.</param>
public sealed record FilterField(AvailabilityFilter Filter, string Name, bool AllowsAll, IReadOnlyList<FilterChoice> Options)
{
    /// <summary>Pole z možností filtru. Stejně ho staví formulář návštěvníka i generátor.</summary>
    /// <param name="filter">Možnosti filtru z výsledku čtení nebo z nabídky.</param>
    internal static FilterField Of(FilterChoices filter) => new(
        filter.Filter,
        VisitorParameters.NameOf(filter.Filter),
        filter.AllowsAll,
        [.. filter.Options.Select(option => new FilterChoice(option.Id, option.Name, option.Id == filter.SelectedId))]);
}

/// <summary>Možnost filtru.</summary>
/// <param name="Id">Id veřejného objektu, hodnota parametru adresy.</param>
/// <param name="Name">Název veřejného objektu.</param>
/// <param name="IsSelected">Možnost je právě zvolená.</param>
public sealed record FilterChoice(int Id, string Name, bool IsSelected);

/// <summary>Parametr adresy, který formulář pošle beze změny.</summary>
/// <param name="Name">Název, jak přišel.</param>
/// <param name="Value">Hodnota, jak přišla.</param>
public sealed record KeptParameter(string Name, string Value);
