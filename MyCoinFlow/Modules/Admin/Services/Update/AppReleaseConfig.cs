namespace MyCoinFlow.Services.Update
{
    public static class AppReleaseConfig
    {
        // HINWEIS: Aktuell Platzhalter. In einem der nächsten Minischritte ...
        public static string VersionFeedUrl { get; set; } =
        "https://1drv.ms/u/c/74e7b5071216d03a/EXRgGBcOz1lHiZjyQStA0wcBO3_sT0HnB7MLq8n8_wfjSQ?e=NonAAZ";

        public static string? LocalVersionJsonPath { get; set; } =
            OneDriveLocalResolver.TryGetReleaseFeedLocalPath();

        public static string LocalDownloadFolder =>
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MyCoinFlow", "Update");

        public static string DefaultSetupFileName => "MyCoinFlow-Setup.exe";
        public static string ProductName => "MyCoinFlow";
        public static bool IsSelfContained => true;
    }
}
