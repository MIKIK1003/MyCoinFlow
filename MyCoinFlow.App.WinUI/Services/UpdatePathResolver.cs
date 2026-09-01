namespace MyCoinFlow.WinUI.Services;

public static class UpdatePathResolver
{
    public static string? GetOneDriveRoot()
    {
        foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var path = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return path;
        }
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
        return Directory.Exists(fallback) ? fallback : null;
    }

    public static string? GetUpdateFolder()
    {
        var root = GetOneDriveRoot();
        if (string.IsNullOrWhiteSpace(root)) return null;
        return new[] { Path.Combine(root, "Documents", "MyCoinFlowUpdate"), Path.Combine(root, "Dokumente", "MyCoinFlowUpdate") }.FirstOrDefault(Directory.Exists);
    }

    public static string? GetSetupPath(string fileName)
    {
        var folder = GetUpdateFolder();
        if (folder is null) return null;
        var path = Path.Combine(folder, fileName);
        return File.Exists(path) ? path : null;
    }
}
