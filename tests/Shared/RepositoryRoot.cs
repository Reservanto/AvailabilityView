namespace AvailabilityView.Tests.Shared;

/// <summary>Kořen repozitáře, který testy najdou z výstupní složky podle souboru řešení.</summary>
internal static class RepositoryRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "AvailabilityView.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Kořen repozitáře se nenašel.");
    }
}
