using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Playnite.SDK.Events;

namespace YuzuPresence
{
    public class YuzuPresencePlugin : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private static readonly Regex VndbUrl = new Regex(@"vndb\.org/v(\d+)", RegexOptions.IgnoreCase);
        private static readonly Regex Token = new Regex(@"\{(\w+)\}");

        private readonly DiscordIpc discord = new DiscordIpc();
        private readonly VndbClient vndb = new VndbClient();
        private readonly object stateLock = new object();
        private Guid? currentGameId;

        public YuzuPresenceSettingsViewModel SettingsVm { get; private set; }
        public YuzuPresenceSettings Settings { get { return SettingsVm.Settings; } }

        public override Guid Id { get; } = Guid.Parse("d2b1fefa-d459-4c2e-b0f0-474805a030da");

        public YuzuPresencePlugin(IPlayniteAPI api) : base(api)
        {
            SettingsVm = new YuzuPresenceSettingsViewModel(this);
            Properties = new GenericPluginProperties { HasSettings = true };
        }

        public override ISettings GetSettings(bool firstRunSettings) { return SettingsVm; }
        public override UserControl GetSettingsView(bool firstRunSettings) { return new YuzuPresenceSettingsView(); }

        // ===================== Playnite events =====================

        public override void OnGameStarted(OnGameStartedEventArgs args)
        {
            var game = args.Game;
            lock (stateLock) { currentGameId = game.Id; }
            if (!Settings.Enabled) return;
            Task.Run(() =>
            {
                try { ShowPresence(game); }
                catch (Exception ex) { logger.Error(ex, "Yuzu Presence: updating presence failed"); }
            });
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            bool mine;
            lock (stateLock)
            {
                // Clear unless a *different* game has since taken over the status.
                mine = currentGameId == null || currentGameId == args.Game.Id;
                if (mine) currentGameId = null;
            }
            if (mine) Task.Run(() => ClearPresence());
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            lock (stateLock) { currentGameId = null; }
            ClearPresence();
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            if (!Settings.UseVndb || !Settings.AutoLinkOnLibraryUpdate) return;
            Task.Run(() =>
            {
                try { AutoLinkNewGames(); }
                catch (Exception ex) { logger.Error(ex, "Yuzu Presence: auto-link failed"); }
            });
        }

        public void OnSettingsSaved()
        {
            discord.Close();
            vndb.ClearCache();
            Guid? cur;
            lock (stateLock) { cur = currentGameId; }
            if (cur != null && Settings.Enabled)
            {
                var game = PlayniteApi.Database.Games.Get(cur.Value);
                if (game != null) Task.Run(() => ShowPresence(game));
            }
        }

        // ===================== presence =====================

        private void ClearPresence()
        {
            try { discord.Clear(Settings.ClientId); }
            catch (Exception ex) { logger.Warn(ex, "Yuzu Presence: clear failed"); }
        }

        private bool IsExcluded(Game game)
        {
            var s = Settings;
            if (s.SkipHiddenGames && game.Hidden) return true;
            if (!string.IsNullOrWhiteSpace(s.IgnoredTag) && game.Tags != null &&
                game.Tags.Any(t => string.Equals(t.Name, s.IgnoredTag.Trim(), StringComparison.OrdinalIgnoreCase))) return true;
            return false;
        }

        private void ShowPresence(Game game)
        {
            var s = Settings;
            if (string.IsNullOrWhiteSpace(s.ClientId))
            {
                logger.Warn("Yuzu Presence: no Discord Application ID set (Add-ons > Extensions settings > Yuzu Presence).");
                return;
            }
            if (IsExcluded(game)) return;

            long startTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Show something straight away, then upgrade it once VNDB has answered.
            if (s.UseVndb && !s.OnlyVndbGames) Push(game, null, startTs);

            VndbInfo info = null;
            if (s.UseVndb) info = ResolveVndb(game);
            if (s.OnlyVndbGames && info == null) return;
            if (info != null || !s.UseVndb) Push(game, info, startTs);
        }

        private void Push(Game game, VndbInfo info, long startTs)
        {
            var activity = BuildActivity(game, info, startTs);
            lock (stateLock)
            {
                // The game may have exited while VNDB was being queried; don't resurrect the status.
                if (currentGameId != game.Id) return;
                if (!discord.SetActivity(Settings.ClientId, activity))
                    logger.Warn("Yuzu Presence: " + discord.LastError);
            }
        }

        private Dictionary<string, object> BuildActivity(Game game, VndbInfo info, long startTs)
        {
            var s = Settings;
            var vars = new Dictionary<string, string>();
            vars["name"] = game.Name ?? "";
            vars["platform"] = game.Platforms != null && game.Platforms.Count > 0 ? game.Platforms[0].Name : "";
            vars["source"] = game.Source != null ? game.Source.Name : "";
            vars["total_time"] = s.ShowTotalPlaytime && game.Playtime > 0 ? FormatPlaytime(game.Playtime) : "";
            vars["vndb_title"] = game.Name ?? "";
            vars["original_title"] = game.Name ?? "";
            vars["developer"] = "";
            vars["rating"] = "";
            vars["released"] = "";
            vars["length"] = "";
            vars["vndb"] = "";
            if (info != null)
            {
                vars["vndb"] = info.Id;
                vars["vndb_title"] = s.TitleLanguage == 1 ? info.Original : info.Title;
                vars["original_title"] = info.Original;
                vars["released"] = info.Year;
                vars["length"] = info.Length;
                if (s.ShowDeveloper) vars["developer"] = info.Developer;
                if (s.ShowRating) vars["rating"] = info.Rating;
            }

            var act = new Dictionary<string, object>();

            // "name" overrides the Discord application's name, so "Playing <game>" shows the game.
            var name = Expand(s.ActivityName, vars);
            if (name.Length >= 2) act["name"] = Trim(name, 128);
            var details = Expand(s.Details, vars);
            if (details.Length >= 2) act["details"] = Trim(details, 128);
            var state = Expand(s.State, vars);
            if (state.Length >= 2) act["state"] = Trim(state, 128);

            if (s.ShowElapsed)
            {
                var ts = new Dictionary<string, object>();
                ts["start"] = startTs;
                act["timestamps"] = ts;
            }

            if (info != null && s.ShowCover && !string.IsNullOrEmpty(info.ImageUrl) && CoverAllowed(info))
            {
                var assets = new Dictionary<string, object>();
                assets["large_image"] = info.ImageUrl;
                var lt = Expand(s.LargeImageText, vars);
                if (lt.Length >= 2) assets["large_text"] = Trim(lt, 128);
                act["assets"] = assets;
            }

            if (info != null && s.ShowButton && !string.IsNullOrEmpty(info.Id))
            {
                var btn = new Dictionary<string, object>();
                btn["label"] = Trim(string.IsNullOrWhiteSpace(s.ButtonText) ? "View on VNDB" : s.ButtonText, 32);
                btn["url"] = "https://vndb.org/" + info.Id;
                act["buttons"] = new List<object> { btn };
            }
            return act;
        }

        private bool CoverAllowed(VndbInfo info)
        {
            double limit;
            switch (Settings.CoverFilter)
            {
                case 1: limit = 1.5; break;   // hide explicit
                case 2: limit = 0.5; break;   // hide suggestive and explicit
                default: return true;
            }
            return info.Sexual < limit && info.Violence < limit;
        }

        // Template: segments separated by '|'. A segment whose placeholders are all empty is dropped,
        // so "Rating {rating}/10 | Played {total_time}" simply collapses when there is no data.
        private static string Expand(string template, Dictionary<string, string> vars)
        {
            if (string.IsNullOrWhiteSpace(template)) return "";
            var parts = new List<string>();
            foreach (var segment in template.Split('|'))
            {
                bool hasToken = false, anyValue = false;
                var text = Token.Replace(segment, m =>
                {
                    hasToken = true;
                    string v;
                    if (vars.TryGetValue(m.Groups[1].Value, out v) && !string.IsNullOrWhiteSpace(v))
                    {
                        anyValue = true;
                        return v;
                    }
                    return "";
                });
                if (hasToken && !anyValue) continue;
                text = text.Trim();
                if (text.Length > 0) parts.Add(text);
            }
            return string.Join(" | ", parts);
        }

        private static string Trim(string text, int max)
        {
            return text.Length > max ? text.Substring(0, max - 1) + "\u2026" : text;
        }

        private static string FormatPlaytime(ulong seconds)
        {
            var h = seconds / 3600;
            var m = (seconds % 3600) / 60;
            return h > 0 ? h + "h " + m + "m" : m + "m";
        }

        // ===================== VNDB linking =====================

        public static string GetVndbId(Game game)
        {
            if (game.Links == null) return null;
            foreach (var l in game.Links)
            {
                if (l.Url == null) continue;
                var m = VndbUrl.Match(l.Url);
                if (m.Success) return "v" + m.Groups[1].Value;
            }
            return null;
        }

        private VndbInfo ResolveVndb(Game game)
        {
            var id = GetVndbId(game);
            if (id != null) return vndb.GetById(id);
            if (!Settings.AutoLinkOnLaunch) return null;
            var m = vndb.FindMatch(game.Name, !Settings.StrictMatching);
            if (m != null)
            {
                LinkGame(game, m.Id);
                logger.Info("Yuzu Presence: linked '" + game.Name + "' to " + m.Id + " (" + m.Title + ")");
            }
            return m;
        }

        // Adds (or updates) the VNDB link in the game's Links tab.
        private void LinkGame(Game game, string vndbId)
        {
            var url = "https://vndb.org/" + vndbId;
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (game.Links == null) game.Links = new ObservableCollection<Link>();
                var existing = game.Links.FirstOrDefault(l => l.Url != null && VndbUrl.IsMatch(l.Url));
                if (existing != null) existing.Url = url;
                else game.Links.Add(new Link("VNDB", url));
                PlayniteApi.Database.Games.Update(game);
            });
        }

        private string TriedPath { get { return Path.Combine(GetPluginUserDataPath(), "tried.json"); } }

        private HashSet<string> LoadTried()
        {
            try
            {
                if (File.Exists(TriedPath))
                {
                    var list = Serialization.FromJson<List<string>>(File.ReadAllText(TriedPath));
                    if (list != null) return new HashSet<string>(list);
                }
            }
            catch { }
            return new HashSet<string>();
        }

        private void SaveTried(HashSet<string> tried)
        {
            try { File.WriteAllText(TriedPath, Serialization.ToJson(tried.ToList())); } catch { }
        }

        // New games from a library update: link up to 30 per run, skipping ones that already failed to match.
        private void AutoLinkNewGames()
        {
            var tried = LoadTried();
            var targets = PlayniteApi.Database.Games
                .Where(g => GetVndbId(g) == null && !tried.Contains(g.Id.ToString()))
                .Take(30).ToList();
            foreach (var g in targets)
            {
                var m = vndb.FindMatch(g.Name, !Settings.StrictMatching);
                if (m != null) LinkGame(g, m.Id);
                else if (vndb.LastRequestFailed) break;     // network trouble: try again next time
                else tried.Add(g.Id.ToString());
                Thread.Sleep(400);                          // stay inside VNDB's rate limit
            }
            SaveTried(tried);
        }

        private void LinkMany(List<Game> games, bool loose, string title)
        {
            int linked = 0, missed = 0;
            PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
            {
                progress.ProgressMaxValue = games.Count;
                foreach (var g in games)
                {
                    if (progress.CancelToken.IsCancellationRequested) break;
                    progress.Text = title + "\n" + g.Name;
                    var m = vndb.FindMatch(g.Name, loose);
                    if (m != null) { LinkGame(g, m.Id); linked++; }
                    else { missed++; if (vndb.LastRequestFailed) break; }
                    progress.CurrentProgressValue = progress.CurrentProgressValue + 1;
                    Thread.Sleep(400);
                }
            }, new GlobalProgressOptions(title, true) { IsIndeterminate = false });
            PlayniteApi.Dialogs.ShowMessage("Linked " + linked + " game(s). " + missed + " had no match on VNDB.", "Yuzu Presence");
        }

        // ===================== menus / commands =====================

        public void LinkAllGames()
        {
            var targets = PlayniteApi.Database.Games.Where(g => GetVndbId(g) == null).ToList();
            if (targets.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("Every game already has a VNDB link.", "Yuzu Presence");
                return;
            }
            var q = targets.Count + " game(s) have no VNDB link. Search VNDB for each one now?\n\n" +
                    "Only exact title matches are linked. This takes roughly " + (int)Math.Ceiling(targets.Count * 0.6) + " seconds.";
            if (PlayniteApi.Dialogs.ShowMessage(q, "Yuzu Presence", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            LinkMany(targets, false, "Linking games to VNDB...");
        }

        public void TestConnection(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
            {
                PlayniteApi.Dialogs.ShowErrorMessage("Enter your Discord Application ID first.", "Yuzu Presence");
                return;
            }
            bool ok = discord.Connect(clientId);
            var error = discord.LastError;
            discord.Close();
            if (ok) PlayniteApi.Dialogs.ShowMessage("Connected to Discord.", "Yuzu Presence");
            else PlayniteApi.Dialogs.ShowErrorMessage(error ?? "Could not connect to Discord.", "Yuzu Presence");
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Link all games to VNDB (exact title matches)",
                MenuSection = "@Yuzu Presence",
                Action = a => LinkAllGames()
            };
            yield return new MainMenuItem
            {
                Description = "Clear presence now",
                MenuSection = "@Yuzu Presence",
                Action = a =>
                {
                    lock (stateLock) { currentGameId = null; }
                    ClearPresence();
                }
            };
            yield return new MainMenuItem
            {
                Description = "Test Discord connection",
                MenuSection = "@Yuzu Presence",
                Action = a => TestConnection(Settings.ClientId)
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = "Find on VNDB and link (best match)",
                MenuSection = "Yuzu Presence",
                Action = a => LinkSelected(a.Games.ToList())
            };
            yield return new GameMenuItem
            {
                Description = "Preview VNDB info",
                MenuSection = "Yuzu Presence",
                Action = a => Preview(a.Games.FirstOrDefault())
            };
            yield return new GameMenuItem
            {
                Description = "Set VNDB ID / link manually...",
                MenuSection = "Yuzu Presence",
                Action = a => SetManually(a.Games.ToList())
            };
        }

        private void LinkSelected(List<Game> games)
        {
            if (games.Count == 0) return;
            if (games.Count > 1)
            {
                LinkMany(games.Where(g => GetVndbId(g) == null).ToList(), true, "Linking selected games to VNDB...");
                return;
            }
            var game = games[0];
            VndbInfo m = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(p => { m = vndb.FindMatch(game.Name, true); },
                new GlobalProgressOptions("Searching VNDB...", false));
            if (m == null)
            {
                PlayniteApi.Dialogs.ShowErrorMessage("No match found.\n\n" + vndb.LastMessage, "Yuzu Presence");
                return;
            }
            var q = "Link '" + game.Name + "' to '" + m.Title + "'?\nhttps://vndb.org/" + m.Id;
            if (PlayniteApi.Dialogs.ShowMessage(q, "Yuzu Presence", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                LinkGame(game, m.Id);
        }

        private void Preview(Game game)
        {
            if (game == null) return;
            var id = GetVndbId(game);
            if (id == null)
            {
                PlayniteApi.Dialogs.ShowErrorMessage("This game has no VNDB link yet. Use 'Find on VNDB and link'.", "Yuzu Presence");
                return;
            }
            VndbInfo info = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(p => { info = vndb.GetById(id); },
                new GlobalProgressOptions("Asking VNDB...", false));
            if (info == null)
            {
                PlayniteApi.Dialogs.ShowErrorMessage("Lookup failed.\n\n" + vndb.LastMessage, "Yuzu Presence");
                return;
            }
            PlayniteApi.Dialogs.ShowMessage(
                "https://vndb.org/" + info.Id + "\nTitle: " + info.Title + "\nOriginal: " + info.Original +
                "\nDeveloper: " + info.Developer + "\nRating: " + info.Rating + "\nReleased: " + info.Year +
                "\nLength: " + info.Length + "\nCover: " + info.ImageUrl, "Yuzu Presence");
        }

        private void SetManually(List<Game> games)
        {
            var r = PlayniteApi.Dialogs.SelectString("VNDB link or ID (e.g. https://vndb.org/v17 or v17):", "Yuzu Presence", "");
            if (!r.Result) return;
            var m = Regex.Match(r.SelectedString ?? "", @"v(\d+)", RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                PlayniteApi.Dialogs.ShowErrorMessage("That doesn't look like a VNDB ID.", "Yuzu Presence");
                return;
            }
            foreach (var g in games) LinkGame(g, "v" + m.Groups[1].Value);
            vndb.ClearCache();
            PlayniteApi.Dialogs.ShowMessage("VNDB link saved.", "Yuzu Presence");
        }
    }
}
