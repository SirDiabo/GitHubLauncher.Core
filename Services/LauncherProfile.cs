namespace GitHubLauncher.Core.Services
{
    public class LauncherProfile
    {
        public static LauncherProfile Default { get; } = new();

        public virtual string DisplayName => "GitHub Launcher";
        public virtual string ApplicationId => "GitHubLauncher";
        public virtual string Repository => "SirDiabo/GitHubLauncher";
        public virtual string ExecutableName => "GitHubLauncher";
        public virtual string DefaultInstallFolderName => "Games";
        public virtual string UserAgent => "GitHubLauncher/1.0";
        public virtual string CliUserAgent => "GitHubLauncher-CLI";
        public virtual string UpdaterUserAgent => "GitHubLauncher-Updater";
        public virtual string SteamTag => DisplayName;

        public virtual (List<object> standard, List<object> experimental, List<object> custom) GetDefaultGamesData()
        {
            return (new List<object>(), new List<object>(), new List<object>());
        }

        public virtual void ConfigureInstalledGame(string gamePath, bool isPortable)
        {
        }
    }
}
