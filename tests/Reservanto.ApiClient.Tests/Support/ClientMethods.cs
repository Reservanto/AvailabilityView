using System.Reflection;

namespace Reservanto.ApiClient.Tests.Support;

/// <summary>Metody klienta pojmenované podle cesty: <c>/v1/Segment/GetList</c> = <c>client.Segment.GetListAsync</c>.</summary>
internal static class ClientMethods
{
    public static IEnumerable<(PropertyInfo Group, MethodInfo Method)> All() =>
        typeof(ReservantoApiClient).GetProperties()
            .SelectMany(group => group.PropertyType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => (group, method)));

    public static string PathOf(PropertyInfo group, MethodInfo method) =>
        $"/v1/{group.Name}/{method.Name.Replace("Async", string.Empty, StringComparison.Ordinal)}";

    public static (object Group, MethodInfo Method) For(ReservantoApiClient client, string path)
    {
        (PropertyInfo group, MethodInfo method) = All().Single(pair => PathOf(pair.Group, pair.Method) == path);
        return (group.GetValue(client)!, method);
    }
}
