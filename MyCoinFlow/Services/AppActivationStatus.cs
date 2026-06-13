namespace MyCoinFlow.Services
{
    /// <summary>
    /// Beschreibt den Aktivierungszustand von MyCoinFlow.
    /// Wird später im LoginWindow und im Lizenzbereich verwendet.
    /// </summary>
    public enum AppActivationStatus
    {
        None = 0,
        Trial = 1,
        Licensed = 2,
        Expired = 3,
        Invalid = 4
    }
}