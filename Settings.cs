using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace YuzuPresence
{
    public class YuzuPresenceSettings : ObservableObject
    {
        // --- Discord ---
        private bool enabled = true;
        private string clientId = "";

        // --- What is shown ---
        private string activityName = "{name}";
        private string details = "{developer}";
        private string state = "Rating {rating}/10 | Played {total_time}";
        private string largeImageText = "{vndb_title}";
        private bool showElapsed = true;
        private bool showTotalPlaytime = true;
        private bool showRating = true;
        private bool showDeveloper = true;
        private bool showCover = true;
        private int coverFilter = 0;          // 0 = show all, 1 = hide explicit, 2 = hide suggestive + explicit
        private bool showButton = true;
        private string buttonText = "View on VNDB";
        private int titleLanguage = 0;        // 0 = romanized, 1 = original

        // --- When it is shown ---
        private bool onlyVndbGames = false;
        private bool skipHiddenGames = true;
        private string ignoredTag = "NoPresence";

        // --- VNDB ---
        private bool useVndb = true;
        private bool autoLinkOnLaunch = true;
        private bool autoLinkOnLibraryUpdate = true;
        private bool strictMatching = true;

        public bool Enabled { get { return enabled; } set { SetValue(ref enabled, value); } }
        public string ClientId { get { return clientId; } set { SetValue(ref clientId, value); } }

        public string ActivityName { get { return activityName; } set { SetValue(ref activityName, value); } }
        public string Details { get { return details; } set { SetValue(ref details, value); } }
        public string State { get { return state; } set { SetValue(ref state, value); } }
        public string LargeImageText { get { return largeImageText; } set { SetValue(ref largeImageText, value); } }
        public bool ShowElapsed { get { return showElapsed; } set { SetValue(ref showElapsed, value); } }
        public bool ShowTotalPlaytime { get { return showTotalPlaytime; } set { SetValue(ref showTotalPlaytime, value); } }
        public bool ShowRating { get { return showRating; } set { SetValue(ref showRating, value); } }
        public bool ShowDeveloper { get { return showDeveloper; } set { SetValue(ref showDeveloper, value); } }
        public bool ShowCover { get { return showCover; } set { SetValue(ref showCover, value); } }
        public int CoverFilter { get { return coverFilter; } set { SetValue(ref coverFilter, value); } }
        public bool ShowButton { get { return showButton; } set { SetValue(ref showButton, value); } }
        public string ButtonText { get { return buttonText; } set { SetValue(ref buttonText, value); } }
        public int TitleLanguage { get { return titleLanguage; } set { SetValue(ref titleLanguage, value); } }

        public bool OnlyVndbGames { get { return onlyVndbGames; } set { SetValue(ref onlyVndbGames, value); } }
        public bool SkipHiddenGames { get { return skipHiddenGames; } set { SetValue(ref skipHiddenGames, value); } }
        public string IgnoredTag { get { return ignoredTag; } set { SetValue(ref ignoredTag, value); } }

        public bool UseVndb { get { return useVndb; } set { SetValue(ref useVndb, value); } }
        public bool AutoLinkOnLaunch { get { return autoLinkOnLaunch; } set { SetValue(ref autoLinkOnLaunch, value); } }
        public bool AutoLinkOnLibraryUpdate { get { return autoLinkOnLibraryUpdate; } set { SetValue(ref autoLinkOnLibraryUpdate, value); } }
        public bool StrictMatching { get { return strictMatching; } set { SetValue(ref strictMatching, value); } }
    }

    public class SimpleCommand : ICommand
    {
        private readonly Action action;
        public SimpleCommand(Action action) { this.action = action; }
        public bool CanExecute(object parameter) { return true; }
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public void Execute(object parameter) { action(); }
    }

    public class YuzuPresenceSettingsViewModel : ObservableObject, ISettings
    {
        private readonly YuzuPresencePlugin plugin;
        private YuzuPresenceSettings editingClone;
        private YuzuPresenceSettings settings;

        public YuzuPresenceSettings Settings
        {
            get { return settings; }
            set { settings = value; OnPropertyChanged(); }
        }

        public ICommand TestConnectionCommand { get; private set; }
        public ICommand LinkAllCommand { get; private set; }

        public YuzuPresenceSettingsViewModel(YuzuPresencePlugin plugin)
        {
            this.plugin = plugin;
            var saved = plugin.LoadPluginSettings<YuzuPresenceSettings>();
            if (saved != null)
            {
                Settings = saved;
            }
            else
            {
                Settings = new YuzuPresenceSettings();
                TryImportOldScriptConfig();
            }
            TestConnectionCommand = new SimpleCommand(() => plugin.TestConnection(Settings.ClientId));
            LinkAllCommand = new SimpleCommand(() => plugin.LinkAllGames());
        }

        // Carries the Application ID over from the earlier PowerShell version of this extension.
        private void TryImportOldScriptConfig()
        {
            try
            {
                var path = Path.Combine(plugin.PlayniteApi.Paths.ExtensionsDataPath, "HyggerPresence_Playnite", "config.json");
                if (!File.Exists(path)) return;
                var old = Serialization.FromJson<Dictionary<string, object>>(File.ReadAllText(path));
                object v;
                if (old != null && old.TryGetValue("ClientId", out v) && v != null) Settings.ClientId = v.ToString();
            }
            catch { }
        }

        public void BeginEdit() { editingClone = Serialization.GetClone(Settings); }
        public void CancelEdit() { Settings = editingClone; }
        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
            plugin.OnSettingsSaved();
        }
        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}
