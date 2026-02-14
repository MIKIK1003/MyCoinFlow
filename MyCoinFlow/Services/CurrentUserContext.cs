namespace MyCoinFlow.Services
{
    /// <summary>
    /// Hält den aktuell angemeldeten Benutzer im Speicher (pro App-Session).
    /// Single Source of Truth für Admin-Rechte in der UI.
    /// </summary>
    public static class CurrentUserContext
    {
        public static bool IsAuthenticated { get; private set; }
        public static string Username { get; private set; } = "";
        public static bool IsAdmin { get; private set; }

        public static void SignIn(string username, bool isAdmin)
        {
            IsAuthenticated = true;
            Username = username ?? "";
            IsAdmin = isAdmin;
        }

        public static void SignOut()
        {
            IsAuthenticated = false;
            Username = "";
            IsAdmin = false;
        }
    }
}
