internal static class UserConfig
{
    private const string ResourceName = "RefineryPlugin.config.json";

    public static string Ensure()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameCapture", "RefineryPlugin", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            using var stream = typeof(UserConfig).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded config '{ResourceName}' was not found.");
            using var reader = new StreamReader(stream);
            File.WriteAllText(path, reader.ReadToEnd());
        }

        return path;
    }
}
