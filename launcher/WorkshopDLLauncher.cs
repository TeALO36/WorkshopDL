// WorkshopDL Launcher — companion GUI for WorkshopDL
// - Auto-detects installed Steam games (libraryfolders.vdf + appmanifest_*.acf)
// - Manually add a game folder (for non-Steam or undetected installs)
// - Paste a Steam Workshop URL -> analyze (game, appid, item) -> download via steamcmd
// - Fallback: hand the URL over to WorkshopDL.exe (clipboard auto-detection)
// Targets .NET Framework 4.8 (preinstalled on Windows 10/11).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WorkshopDLLauncher
{
    public class DetectedGame
    {
        public string Name;
        public int AppId;
        public string InstallPath;   // empty for manual entries without a known exe dir
        public bool Manual;          // added by the user
    }

    public class AnalysisResult
    {
        public bool Ok;
        public string Error;
        public int AppId;
        public string GameName;
        public string ItemTitle;
        public long ItemId;
        public bool Removed;         // item removed from Workshop
        public bool Installed;       // game detected on this PC
        public string InstallPath;
    }

    public static class SteamLocator
    {
        // Returns the Steam install path (e.g. C:\Program Files (x86)\Steam) or null.
        public static string FindSteamPath()
        {
            var candidates = new List<string>();
            try
            {
                string p = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (!string.IsNullOrEmpty(p)) candidates.Add(p);
            }
            catch { }
            try
            {
                string p = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
                if (!string.IsNullOrEmpty(p)) candidates.Add(p);
            }
            catch { }
            candidates.Add(@"C:\Program Files (x86)\Steam");
            candidates.Add(@"C:\Program Files\Steam");
            foreach (var c in candidates)
                if (!string.IsNullOrEmpty(c) && Directory.Exists(Path.Combine(c, "steamapps")))
                    return c;
            return null;
        }

        // All steam library folders that contain a steamapps dir.
        // Paths are normalized (slashes + trailing separator + case) so the same
        // library is never scanned twice — the registry stores forward slashes.
        private static string NormLib(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            p = p.Replace('/', '\\').TrimEnd('\\');
            return p;
        }

        public static List<string> FindLibraryFolders(string steamPath)
        {
            var libs = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> add = p =>
            {
                p = NormLib(p);
                if (string.IsNullOrEmpty(p)) return;
                if (!Directory.Exists(Path.Combine(p, "steamapps"))) return;
                if (seen.Add(p)) libs.Add(p);
            };
            add(steamPath);
            string vdf = string.IsNullOrEmpty(steamPath) ? null : Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (vdf != null && File.Exists(vdf))
            {
                try
                {
                    string txt = File.ReadAllText(vdf);
                    foreach (Match m in Regex.Matches(txt, "\"path\"\\s+\"([^\"]+)\""))
                        add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
                catch { }
            }
            return libs;
        }

        // Parses appmanifest_*.acf files -> detected games. Folders that exist in
        // steamapps/common but have no manifest (partial/orphaned installs) are added
        // too, so the list doesn't silently miss games the user can see on disk.
        public static List<DetectedGame> Scan(List<string> libraries)
        {
            var found = new List<DetectedGame>();
            var seen = new HashSet<int>();
            var coveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<DetectedGame> push = g =>
            {
                if (!string.IsNullOrEmpty(g.InstallPath))
                {
                    g.InstallPath = NormLib(g.InstallPath);
                    if (!seenPaths.Add(g.InstallPath)) return;
                }
                if (g.AppId != 0 && !seen.Add(g.AppId)) return;
                found.Add(g);
            };
            foreach (var lib in libraries)
            {
                string dir = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(dir)) continue;
                foreach (var acf in Directory.GetFiles(dir, "appmanifest_*.acf"))
                {
                    try
                    {
                        string txt = File.ReadAllText(acf);
                        string appIdStr = Regex.Match(txt, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value;
                        string name = Regex.Match(txt, "\"name\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value;
                        string instDir = Regex.Match(txt, "\"installdir\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value;
                        int appId;
                        if (!int.TryParse(appIdStr, out appId) || string.IsNullOrEmpty(name)) continue;
                        string full = Path.Combine(dir, "common", instDir.Replace("\\\\", "\\"));
                        if (Directory.Exists(full)) coveredPaths.Add(NormLib(full));
                        push(new DetectedGame
                        {
                            Name = name.Replace("\\\\", "\\"),
                            AppId = appId,
                            InstallPath = Directory.Exists(full) ? full : "",
                            Manual = false
                        });
                    }
                    catch { }
                }
            }
            // Orphaned folders: present on disk, no appmanifest.
            foreach (var lib in libraries)
            {
                string common = Path.Combine(lib, "steamapps", "common");
                if (!Directory.Exists(common)) continue;
                foreach (var sub in Directory.GetDirectories(common))
                {
                    if (coveredPaths.Contains(NormLib(sub))) continue;
                    string leaf = Path.GetFileName(sub);
                    if (string.IsNullOrEmpty(leaf)) continue;
                    // Known non-game folders Steam keeps inside common/
                    if (leaf == "Steam Controller Configs" || leaf == "_CommonRedist" ||
                        leaf.StartsWith("_")) continue;
                    push(new DetectedGame
                    {
                        Name = leaf,
                        AppId = 0,
                        InstallPath = sub,
                        Manual = false
                    });
                }
            }
            found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return found;
        }
    }

    public static class WorkshopAnalyzer
    {
        // Supported games (Modules/games.txt + Modules/appids.txt, parallel lines).
        // Used to cross-check/correct the AppID scraped from the Workshop page and
        // to offer every supported game in the target selector.
        private static Dictionary<string, int> supportedByName;
        private static List<KeyValuePair<string, int>> supportedList;

        public static void LoadSupported(string modulesDir)
        {
            if (supportedList != null) return;
            try
            {
                string g = Path.Combine(modulesDir, "games.txt");
                string a = Path.Combine(modulesDir, "appids.txt");
                if (!File.Exists(g) || !File.Exists(a)) return;
                string[] names = File.ReadAllLines(g);
                string[] ids = File.ReadAllLines(a);
                int n = Math.Min(names.Length, ids.Length);
                var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var list = new List<KeyValuePair<string, int>>();
                for (int i = 0; i < n; i++)
                {
                    string nm = names[i].Trim();
                    int id;
                    if (!int.TryParse(ids[i].Trim(), out id)) id = 0;
                    if (nm.Length == 0 || id == 0) continue;
                    list.Add(new KeyValuePair<string, int>(nm, id));
                    if (!byName.ContainsKey(nm)) byName[nm] = id;
                }
                if (list.Count > 0) { supportedList = list; supportedByName = byName; }
            }
            catch { }
        }

        public static List<KeyValuePair<string, int>> SupportedGames()
        {
            return supportedList;
        }

        // Extracts the published file id from any steamcommunity sharedfiles URL.
        public static long? ParseItemId(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            Match m = Regex.Match(url, @"[?&]id=(\d+)");
            if (!m.Success) m = Regex.Match(url, @"/filedetails/?id=(\d+)");
            if (!m.Success) m = Regex.Match(url, @"(\d{6,})\s*$");
            if (m.Success) { long id; if (long.TryParse(m.Groups[1].Value, out id)) return id; }
            return null;
        }

        // Downloads the page with up to 3 attempts: Steam answers 429 "too many
        // requests" when analyzed in a burst, which used to fail the analysis.
        private static string FetchHtml(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            Exception last = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";
                    req.Accept = "text/html,application/xhtml+xml";
                    req.Timeout = 20000;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        return sr.ReadToEnd();
                }
                catch (WebException wex)
                {
                    last = wex;
                    int code = 0;
                    var hr = wex.Response as HttpWebResponse;
                    if (hr != null) { code = (int)hr.StatusCode; hr.Close(); }
                    bool transient = code == 429 || code == 500 || code == 502 || code == 503 || code == 504 ||
                        wex.Status == WebExceptionStatus.Timeout ||
                        wex.Status == WebExceptionStatus.ConnectFailure ||
                        wex.Status == WebExceptionStatus.NameResolutionFailure ||
                        wex.Status == WebExceptionStatus.ReceiveFailure ||
                        wex.Status == WebExceptionStatus.ConnectionClosed;
                    if (!transient || attempt == 3) throw;
                    System.Threading.Thread.Sleep(attempt * 2000); // 2 s, puis 4 s
                }
            }
            throw last;
        }

        public static AnalysisResult Analyze(string url)
        {
            var r = new AnalysisResult();
            long? itemId = ParseItemId(url);
            if (itemId == null) { r.Error = "URL invalide : impossible de trouver l'identifiant de l'objet du Workshop."; return r; }
            r.ItemId = itemId.Value;

            string html;
            try
            {
                html = FetchHtml("https://steamcommunity.com/sharedfiles/filedetails/?id=" + r.ItemId);
            }
            catch (WebException wex)
            {
                var hr = wex.Response as HttpWebResponse;
                if (hr != null && (int)hr.StatusCode == 429)
                    r.Error = "Échec de l'analyse : Steam limite les requêtes (trop de demandes). " +
                        "Patientez quelques secondes puis recliquez sur « Analyser le lien ».";
                else if (hr != null)
                    r.Error = "Échec de l'analyse : Steam a répondu " + (int)hr.StatusCode + ". " +
                        "Patientez quelques secondes puis réessayez.";
                else
                    r.Error = "Échec de l'analyse (réseau) : " + wex.Message +
                        " Vérifiez votre connexion puis réessayez.";
                return r;
            }
            catch (Exception ex)
            {
                r.Error = "Échec de l'analyse : " + ex.Message;
                return r;
            }

            try
            {
                if (html.IndexOf("There was a problem accessing the item", StringComparison.OrdinalIgnoreCase) >= 0)
                { r.Error = "Objet introuvable sur le Steam Workshop (id " + r.ItemId + ")."; return r; }

                // The "banned" banner is always present in Steam's HTML template but
                // hidden with display:none unless the item really was removed.
                Match rm = Regex.Match(html, "id=\"bannedNotification\"[^>]*>");
                r.Removed = rm.Success && rm.Value.IndexOf("display: none", StringComparison.OrdinalIgnoreCase) < 0;

                // Game name from the app hub header (the item's own game).
                Match nm = Regex.Match(html, "apphub_AppName[^>]*>([^<]+)<");
                if (nm.Success) r.GameName = WebUtility.HtmlDecode(nm.Groups[1].Value.Trim());

                // AppID: prefer signals NEAR that header — a global first-match can
                // land on an unrelated game advertised in the page (e.g. a Garry's Mod
                // banner on a Golf map page).
                int appId = 0;
                if (nm.Success)
                {
                    int idx = nm.Index;
                    int from = Math.Max(0, idx - 3000);
                    int len = Math.Min(html.Length - from, 6000);
                    string win = html.Substring(from, len);
                    Match wm = Regex.Match(win, @"data-appid=""(\d+)""");
                    if (wm.Success) appId = int.Parse(wm.Groups[1].Value);
                    if (appId == 0)
                    {
                        int rel = idx - from;
                        int bestDist = int.MaxValue;
                        foreach (Match am in Regex.Matches(win, @"steamcommunity\.com/app/(\d+)"))
                        {
                            int d = Math.Abs(am.Index - rel);
                            if (d < bestDist) { bestDist = d; appId = int.Parse(am.Groups[1].Value); }
                        }
                    }
                }
                if (appId == 0)
                {
                    Match m = Regex.Match(html, @"data-appid=""(\d+)""");
                    if (m.Success) appId = int.Parse(m.Groups[1].Value);
                }
                if (appId == 0)
                {
                    Match m = Regex.Match(html, @"steamcommunity\.com/app/(\d+)");
                    if (m.Success) appId = int.Parse(m.Groups[1].Value);
                }

                // Cross-check with the supported list: if the page's own game name is
                // in the list, its AppID is authoritative for WorkshopDL.
                if (r.GameName != null && supportedByName != null)
                {
                    int mapped;
                    if (supportedByName.TryGetValue(r.GameName, out mapped) && mapped != 0) appId = mapped;
                }

                r.AppId = appId;
                if (r.AppId == 0) { r.Error = "Impossible de déterminer le jeu (AppID) depuis la page du Workshop."; return r; }

                Match t = Regex.Match(html, "class=\"workshopItemTitle\">([^<]+)<");
                if (t.Success) r.ItemTitle = WebUtility.HtmlDecode(t.Groups[1].Value.Trim());

                r.Ok = true;
                return r;
            }
            catch (Exception ex)
            {
                r.Error = "Erreur réseau : " + ex.Message;
                return r;
            }
        }
    }

    public static class SteamCmdRunner
    {
        // Locates (or bootstraps) steamcmd next to WorkshopDL / in the user profile.
        public static string FindSteamCmd(string workDir)
        {
            var candidates = new List<string>
            {
                Path.Combine(workDir, "steamcmd", "steamcmd.exe"),
                Path.Combine(workDir, "steamcmd.exe"),
            };
            foreach (var c in candidates) if (File.Exists(c)) return c;
            string local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkshopDL", "steamcmd", "steamcmd.exe");
            if (File.Exists(local)) return local;
            return null; // caller must bootstrap
        }

        public static string Bootstrap(string workDir)
        {
            string target = Path.Combine(workDir, "steamcmd");
            Directory.CreateDirectory(target);
            string exe = Path.Combine(target, "steamcmd.exe");
            if (File.Exists(exe)) return exe;
            string zip = Path.Combine(Path.GetTempPath(), "steamcmd.zip");
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var wc = new WebClient())
                wc.DownloadFile("https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip", zip);
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, target);
            return File.Exists(exe) ? exe : null;
        }
    }

    public class MainForm : Form
    {
        // ---------- UI ----------
        private TextBox urlBox;
        private Button analyzeBtn;
        private Button downloadBtn;
        private Button openWdlBtn;
        private Button cancelBtn;
        private Label statusLabel;
        private Label itemLabel;
        private Label targetLbl;
        private ComboBox targetCombo;
        private Label installedLabel;
        private Label gamesTitle;
        private Button locateBtn;
        private ListView gamesList;
        private readonly List<DetectedGame> rendered = new List<DetectedGame>();
        private Button addFolderBtn;
        private Button refreshBtn;
        private TextBox searchBox;
        private CheckBox anonCheck;
        private ProgressBar progress;
        private Panel resultPanel;
        private ToolStripMenuItem hideMenuItem;
        private ToolStripMenuItem unhideAllMenuItem;

        // ---------- state ----------
        private List<DetectedGame> games = new List<DetectedGame>();
        private AnalysisResult current;
        private DetectedGame autoGame;         // original auto-detection (combo row 0)
        private readonly string workDir;
        private readonly string manualListPath;
        private readonly string hiddenListPath;
        private Process steamCmdProc;
        private readonly HashSet<string> hiddenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DetectedGame> comboGames = new List<DetectedGame>();
        private volatile bool downloadCancelled;

        public MainForm()
        {
            workDir = AppDomain.CurrentDomain.BaseDirectory;
            manualListPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WorkshopDL", "manual_games.txt");
            hiddenListPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WorkshopDL", "hidden_games.txt");

            WorkshopAnalyzer.LoadSupported(Path.Combine(workDir, "Modules"));
            LoadHidden();
            BuildUi();
            LoadGames();
            TryPrefillFromClipboard();
        }

        // ---------- UI construction ----------
        private void BuildUi()
        {
            Text = "WorkshopDL Launcher";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1040, 640);
            Size = new Size(1080, 690);
            BackColor = Color.FromArgb(245, 246, 250);
            Font = new Font("Segoe UI", 9.5F);

            // Header
            var header = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(27, 40, 56) };
            var title = new Label
            {
                Text = "WorkshopDL Launcher",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(18, 16)
            };
            var sub = new Label
            {
                Text = "Collez un lien du Steam Workshop — le jeu est détecté automatiquement",
                ForeColor = Color.FromArgb(160, 190, 220),
                Font = new Font("Segoe UI", 9F),
                AutoSize = true,
                Location = new Point(20, 44)
            };
            header.Controls.Add(title);
            header.Controls.Add(sub);

            // Left: URL + analysis + download (added to the form at the end, see dock note below)
            var left = new Panel { Dock = DockStyle.Left, Width = 500, BackColor = Color.White };

            var urlLabel = new Label { Text = "Lien de l'objet du Workshop :", AutoSize = true, Location = new Point(16, 16) };
            urlBox = new TextBox { Location = new Point(16, 38), Width = 470, Height = 26 };
            analyzeBtn = MakeButton("Analyser le lien", Color.FromArgb(52, 120, 246), new Point(16, 72), 150);
            analyzeBtn.Click += (s, e) => AnalyzeAsync();

            resultPanel = new Panel
            {
                Location = new Point(16, 116),
                Size = new Size(470, 166),
                BackColor = Color.FromArgb(248, 249, 252),
                Visible = false
            };
            itemLabel = new Label { Location = new Point(12, 6), Size = new Size(446, 32), ForeColor = Color.FromArgb(40, 40, 40) };
            targetLbl = new Label
            {
                Text = "Envoyer vers (jeu cible —modifiable) :",
                AutoSize = true,
                Location = new Point(12, 40),
                ForeColor = Color.FromArgb(95, 100, 110),
                Font = new Font("Segoe UI", 8.5F)
            };
            targetCombo = new ComboBox
            {
                Location = new Point(12, 56),
                Size = new Size(446, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5F)
            };
            targetCombo.SelectedIndexChanged += (s, e) => ApplyTargetSelection();
            installedLabel = new Label { Location = new Point(12, 86), Size = new Size(446, 36) };
            locateBtn = MakeButton("Localiser le dossier du jeu…", Color.FromArgb(120, 90, 220), new Point(12, 124), 260);
            locateBtn.Visible = false;
            locateBtn.Click += (s, e) => AddManualFolder();
            resultPanel.Controls.Add(itemLabel);
            resultPanel.Controls.Add(targetLbl);
            resultPanel.Controls.Add(targetCombo);
            resultPanel.Controls.Add(installedLabel);
            resultPanel.Controls.Add(locateBtn);
            left.Controls.Add(resultPanel);
            left.Controls.Add(urlLabel);
            left.Controls.Add(urlBox);
            left.Controls.Add(analyzeBtn);

            downloadBtn = MakeButton("Télécharger", Color.FromArgb(35, 165, 90), new Point(16, 296), 170);
            downloadBtn.Enabled = false;
            downloadBtn.Click += (s, e) => DownloadAsync();

            openWdlBtn = MakeButton("Ouvrir dans WorkshopDL", Color.FromArgb(90, 100, 115), new Point(196, 296), 210);
            openWdlBtn.Enabled = false;
            openWdlBtn.Click += (s, e) => HandOverToWorkshopDL();

            cancelBtn = MakeButton("Annuler", Color.FromArgb(200, 60, 60), new Point(416, 296), 70);
            cancelBtn.Enabled = false;
            cancelBtn.Visible = false;
            cancelBtn.Click += (s, e) => CancelDownload();

            anonCheck = new CheckBox
            {
                Text = "Mode anonyme (décochez si le jeu refuse le téléchargement anonyme)",
                AutoSize = true,
                Location = new Point(16, 340),
                Checked = true
            };
            left.Controls.Add(downloadBtn);
            left.Controls.Add(openWdlBtn);
            left.Controls.Add(cancelBtn);
            left.Controls.Add(anonCheck);

            progress = new ProgressBar { Location = new Point(16, 374), Width = 470, Height = 22, Visible = false, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30 };
            left.Controls.Add(progress);

            statusLabel = new Label
            {
                Text = "En attente d'un lien…",
                AutoSize = false,
                Location = new Point(16, 408),
                Size = new Size(470, 120),
                ForeColor = Color.FromArgb(90, 95, 105)
            };
            left.Controls.Add(statusLabel);

            // Right: detected games.
            // Everything here is docked (never anchored): anchoring captured the
            // panel's transient width while the form's dock layout was still settling,
            // which collapsed the list to ~20px wide in the first build.
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 12, 16, 12), BackColor = Color.FromArgb(245, 246, 250) };

            // Children of a docked panel must be added Fill-LAST-in-processing, i.e.
            // the Fill control has to be FIRST in the Controls collection: WinForms
            // docks from the end of the collection backwards.
            gamesList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                // GridLines off on purpose: WinForms draws phantom grid rows across
                // the empty area below the last item (known ListView rendering bug).
                HideSelection = false,
                ShowItemToolTips = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9.5F)
            };
            gamesList.Columns.Add("Jeu", 300);
            gamesList.Columns.Add("AppID", 90);
            gamesList.Columns.Add("Dossier", 260);
            gamesList.Columns.Add("Source", 90);
            gamesList.DoubleClick += (s, e) => OpenSelectedGameFolder();
            // Fixed pixel columns (740px) overflow the 536px client area and push the
            // last column off-screen behind a horizontal scrollbar: re-fit on every resize.
            gamesList.Resize += (s, e) => FitColumns();

            // Right-click: hide a game from the list / restore hidden games.
            var gamesMenu = new ContextMenuStrip();
            hideMenuItem = new ToolStripMenuItem("Masquer ce jeu de la liste");
            hideMenuItem.Click += (s, e) => HideSelectedGame();
            unhideAllMenuItem = new ToolStripMenuItem("Aucun jeu masqué");
            unhideAllMenuItem.Click += (s, e) => UnhideAllGames();
            gamesMenu.Items.Add(hideMenuItem);
            gamesMenu.Items.Add(new ToolStripSeparator());
            gamesMenu.Items.Add(unhideAllMenuItem);
            gamesMenu.Opening += (s, e) =>
            {
                int n = hiddenKeys.Count;
                unhideAllMenuItem.Enabled = n > 0;
                unhideAllMenuItem.Text = n > 0
                    ? "Réafficher tous les jeux masqués (" + n + ")"
                    : "Aucun jeu masqué";
                hideMenuItem.Enabled = gamesList.SelectedIndices.Count > 0;
            };
            gamesList.ContextMenuStrip = gamesMenu;
            gamesList.MouseClick += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                var hit = gamesList.HitTest(e.Location);
                if (hit.Item != null) { hit.Item.Selected = true; hit.Item.Focused = true; }
            };
            right.Controls.Add(gamesList);

            var hint = new Label
            {
                Text = "Astuce : un jeu absent de la liste ? Cliquez sur « + Dossier manuel » pour pointer son dossier d'installation.\nDouble-clic : ouvre le dossier du jeu — Clic droit : masquer / réafficher un jeu de la liste.",
                Dock = DockStyle.Bottom,
                Height = 74,
                AutoSize = false,
                ForeColor = Color.FromArgb(110, 115, 125)
            };
            right.Controls.Add(hint);

            var filterRow = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.FromArgb(245, 246, 250) };
            searchBox = new TextBox { Location = new Point(0, 10), Width = 260 };
            searchBox.TextChanged += (s, e) => RenderGames();
            refreshBtn = MakeButton("Actualiser", Color.FromArgb(52, 120, 246), new Point(272, 8), 100);
            refreshBtn.Click += (s, e) => LoadGames();
            addFolderBtn = MakeButton("+ Dossier manuel", Color.FromArgb(120, 90, 220), new Point(382, 8), 150);
            addFolderBtn.Click += (s, e) => AddManualFolder();
            filterRow.Controls.Add(searchBox);
            filterRow.Controls.Add(refreshBtn);
            filterRow.Controls.Add(addFolderBtn);
            right.Controls.Add(filterRow);

            var topRow = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(245, 246, 250) };
            gamesTitle = new Label
            {
                Text = "Jeux détectés sur ce PC",
                Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(0, 8)
            };
            topRow.Controls.Add(gamesTitle);
            right.Controls.Add(topRow);

            // Docking layout is applied in reverse collection order (last entry laid out
            // first): header docks Top, then the left column docks Left, and the Fill panel
            // — which must stay FIRST in the collection — is laid out last.
            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(header);
        }

        private Button MakeButton(string text, Color bg, Point loc, int width)
        {
            return new Button
            {
                Text = text,
                Location = loc,
                Size = new Size(width, 34),
                BackColor = bg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Bold)
            };
        }

        // ---------- games ----------
        // Distribute the column widths over the real client width so every column
        // stays visible whatever the window size (no horizontal scrollbar).
        private void FitColumns()
        {
            if (gamesList == null || gamesList.Columns.Count != 4) return;
            int w = gamesList.ClientSize.Width - 4; // account for the border/scrollbar gutter
            if (w < 320) w = 320;
            int appid = 80;
            int source = 74;
            int jeu = Math.Max(180, (int)(w * 0.42));
            int folder = w - appid - source - jeu;
            if (folder < 140)
            {
                folder = 140;
                jeu = Math.Max(140, w - appid - source - folder);
            }
            gamesList.Columns[0].Width = jeu;
            gamesList.Columns[1].Width = appid;
            gamesList.Columns[2].Width = folder;
            gamesList.Columns[3].Width = source;
        }

        private void LoadGames()
        {
            statusLabel.Text = "Détection des jeux Steam…";
            try
            {
                string steam = SteamLocator.FindSteamPath();
                var libs = SteamLocator.FindLibraryFolders(steam);
                games = SteamLocator.Scan(libs);
                foreach (var g in LoadManualGames())
                    if (!games.Exists(x => x.AppId == g.AppId && g.AppId != 0) &&
                        !games.Exists(x => string.Equals(x.InstallPath, g.InstallPath, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(g.InstallPath)))
                        games.Add(g);
                RenderGames();
                statusLabel.Text = (steam == null)
                    ? "Steam introuvable. Ajoutez un dossier manuellement avec le bouton « + Dossier manuel »."
                    : string.Format("{0} jeu(x) détecté(s) dans {1} bibliothèque(s).", games.Count, libs.Count);
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Erreur de détection : " + ex.Message;
            }
        }

        private void RenderGames()
        {
            string filter = searchBox.Text.Trim();
            gamesList.BeginUpdate();
            gamesList.Items.Clear();
            rendered.Clear();
            foreach (var g in games)
            {
                if (hiddenKeys.Contains(KeyFor(g))) continue;
                if (filter.Length > 0 && g.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var item = new ListViewItem(g.Name);
                item.SubItems.Add(g.AppId != 0 ? g.AppId.ToString() : "—");
                // Keep the cell short (folder name) and put the full path in the tooltip
                // so the column stays readable at any window width.
                string folderName;
                if (string.IsNullOrEmpty(g.InstallPath))
                    folderName = "(chemin inconnu)";
                else
                {
                    string trimmed = g.InstallPath.TrimEnd('\\', '/');
                    folderName = Path.GetFileName(trimmed);
                    if (string.IsNullOrEmpty(folderName)) folderName = trimmed;
                }
                item.SubItems.Add(folderName);
                item.SubItems.Add(g.Manual ? "Manuel" : "Steam");
                if (g.Manual) item.ForeColor = Color.FromArgb(110, 70, 190);
                item.ToolTipText = string.IsNullOrEmpty(g.InstallPath)
                    ? "Chemin inconnu — reprenez le dossier avec « + Dossier manuel »."
                    : g.InstallPath;
                if (g.AppId == 0)
                    item.ToolTipText += "\nAppID inconnu — réessayez après « Actualiser » ou « + Dossier manuel ».";
                gamesList.Items.Add(item);
                rendered.Add(g);
            }
            gamesList.EndUpdate();
            FitColumns();
            gamesTitle.Text = "Jeux détectés sur ce PC (" + rendered.Count + ")";
            if (current != null && current.Ok) BuildTargetCombo();
        }

        private void OpenSelectedGameFolder()
        {
            try
            {
                if (gamesList.SelectedIndices.Count == 0) return;
                var g = rendered[gamesList.SelectedIndices[0]];
                if (string.IsNullOrEmpty(g.InstallPath) || !Directory.Exists(g.InstallPath))
                {
                    statusLabel.Text = "Chemin inconnu pour « " + g.Name + " ».";
                    return;
                }
                Process.Start(new ProcessStartInfo("explorer.exe", g.InstallPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Erreur : " + ex.Message;
            }
        }

        private List<DetectedGame> LoadManualGames()
        {
            var list = new List<DetectedGame>();
            try
            {
                if (!File.Exists(manualListPath)) return list;
                foreach (var line in File.ReadAllLines(manualListPath))
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    int appId = 0;
                    int.TryParse(parts[0], out appId);
                    list.Add(new DetectedGame
                    {
                        AppId = appId,
                        Name = parts[1],
                        InstallPath = parts.Length > 2 ? parts[2] : "",
                        Manual = true
                    });
                }
            }
            catch { }
            return list;
        }

        private void SaveManualGames()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(manualListPath));
                var sb = new StringBuilder();
                foreach (var g in games)
                    if (g.Manual)
                        sb.AppendFormat("{0}\t{1}\t{2}\n", g.AppId, g.Name.Replace('\t', ' '), g.InstallPath);
                File.WriteAllText(manualListPath, sb.ToString());
            }
            catch { }
        }

        // ---------- hidden games (ignore) ----------
        private static string KeyFor(DetectedGame g)
        {
            if (g.AppId != 0) return "A" + g.AppId;
            if (!string.IsNullOrEmpty(g.InstallPath)) return "P" + g.InstallPath.TrimEnd('\\', '/').Replace('/', '\\');
            return "N" + g.Name.ToLowerInvariant();
        }

        private void LoadHidden()
        {
            try
            {
                if (!File.Exists(hiddenListPath)) return;
                foreach (var line in File.ReadAllLines(hiddenListPath))
                    if (line.Trim().Length > 0) hiddenKeys.Add(line.Trim());
            }
            catch { }
        }

        private void SaveHidden()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(hiddenListPath));
                File.WriteAllLines(hiddenListPath, new List<string>(hiddenKeys).ToArray());
            }
            catch { }
        }

        private void HideSelectedGame()
        {
            if (gamesList.SelectedIndices.Count == 0) return;
            var g = rendered[gamesList.SelectedIndices[0]];
            hiddenKeys.Add(KeyFor(g));
            SaveHidden();
            RenderGames();
            statusLabel.Text = "« " + g.Name + " » masqué (" + hiddenKeys.Count + " masqué(s)).\n" +
                "Clic droit dans la liste → « Réafficher tous les jeux masqués » pour tout remettre.";
        }

        private void UnhideAllGames()
        {
            if (hiddenKeys.Count == 0) return;
            int n = hiddenKeys.Count;
            hiddenKeys.Clear();
            SaveHidden();
            RenderGames();
            statusLabel.Text = n + " jeu(s) réaffiché(s).";
        }

        // ---------- target game selector ----------
        private static string TargetKey(DetectedGame g)
        {
            return g.AppId + "|" + g.Name;
        }

        // Rebuilds the "Envoyer vers" combo: auto-detection first, then the games
        // detected on this PC, then the full supported list (non installed).
        private void BuildTargetCombo()
        {
            if (current == null || !current.Ok || autoGame == null || targetCombo == null) return;

            string prevKey = null;
            if (targetCombo.SelectedIndex >= 0 && targetCombo.SelectedIndex < comboGames.Count)
                prevKey = TargetKey(comboGames[targetCombo.SelectedIndex]);

            comboGames.Clear();
            targetCombo.Items.Clear();

            comboGames.Add(autoGame);
            targetCombo.Items.Add("Auto : " + autoGame.Name + "  (AppID " + autoGame.AppId + ")");

            var seen = new HashSet<int>();
            if (autoGame.AppId != 0) seen.Add(autoGame.AppId);

            foreach (var g in games)
            {
                if (hiddenKeys.Contains(KeyFor(g))) continue;
                if (g.AppId != 0 && !seen.Add(g.AppId)) continue;
                comboGames.Add(g);
                targetCombo.Items.Add(g.Name + "  (AppID " +
                    (g.AppId != 0 ? g.AppId.ToString() : "?") + ")" + (g.Manual ? " — manuel" : ""));
            }

            var supported = WorkshopAnalyzer.SupportedGames();
            if (supported != null)
            {
                foreach (var kv in supported)
                {
                    if (!seen.Add(kv.Value)) continue;
                    comboGames.Add(new DetectedGame { Name = kv.Key, AppId = kv.Value, InstallPath = "", Manual = false });
                    targetCombo.Items.Add(kv.Key + "  (AppID " + kv.Value + ") — non installé");
                }
            }

            int sel = 0;
            if (prevKey != null)
                for (int i = 0; i < comboGames.Count; i++)
                    if (TargetKey(comboGames[i]) == prevKey) { sel = i; break; }
            targetCombo.SelectedIndex = sel; // fires ApplyTargetSelection
        }

        // Applies the selected target: forces AppID/game for the download and
        // refreshes the installed status + button states.
        private void ApplyTargetSelection()
        {
            if (current == null || !current.Ok || targetCombo == null) return;
            int i = targetCombo.SelectedIndex;
            if (i < 0 || i >= comboGames.Count) return;

            var g = comboGames[i];
            current.AppId = g.AppId;
            current.GameName = g.Name;
            current.InstallPath = g.InstallPath;
            bool installed = !string.IsNullOrEmpty(g.InstallPath) && Directory.Exists(g.InstallPath);
            current.Installed = installed;

            installedLabel.Text = installed
                ? "✔ Installé sur ce PC — " + g.InstallPath
                : (g.AppId == 0
                    ? "✘ AppID inconnu pour ce jeu — utilisez « + Dossier manuel » ou laissez la détection auto."
                    : "✘ Jeu non détecté sur ce PC — vérifiez qu'il est installé, ou ajoutez son dossier.");
            installedLabel.ForeColor = installed
                ? Color.FromArgb(25, 130, 70)
                : (g.AppId == 0 ? Color.FromArgb(190, 40, 40) : Color.FromArgb(190, 110, 20));
            locateBtn.Visible = !installed;

            downloadBtn.Enabled = !current.Removed && g.AppId != 0;
            openWdlBtn.Enabled = true;
            if (current.Removed)
                statusLabel.Text = "Objet retiré du Workshop : le téléchargement échouera probablement.";
            else if (g.AppId == 0)
                statusLabel.Text = "La cible n'a pas d'AppID : laissez « Auto » ou choisissez un autre jeu pour télécharger.";
            else
                statusLabel.Text = "Cible : " + g.Name + (i == 0 ? " (détection automatique)" : " (votre choix)") +
                    " — « Télécharger » enverra l'objet vers ce jeu.";
        }

        // ---------- download cancellation ----------
        private void KillSteamCmd()
        {
            try
            {
                var p = steamCmdProc;
                if (p != null && !p.HasExited) p.Kill();
            }
            catch { }
        }

        private void CancelDownload()
        {
            downloadCancelled = true;
            KillSteamCmd();
            cancelBtn.Enabled = false;
            statusLabel.Text = "Annulation en cours…";
        }

        private void AddManualFolder()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Sélectionnez le dossier d'installation du jeu (celui qui contient le .exe du jeu).";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string path = dlg.SelectedPath;
                string name = new DirectoryInfo(path).Name;

                // Try to infer an appid if the folder sits inside a Steam library.
                int appId = 0;
                var m = Regex.Match(path, @"\\steamapps\\common\\", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    // find matching appmanifest by installdir
                    foreach (var lib in SteamLocator.FindLibraryFolders(SteamLocator.FindSteamPath()))
                    {
                        string dir = Path.Combine(lib, "steamapps");
                        if (!Directory.Exists(dir)) continue;
                        foreach (var acf in Directory.GetFiles(dir, "appmanifest_*.acf"))
                        {
                            try
                            {
                                string txt = File.ReadAllText(acf);
                                string instDir = Regex.Match(txt, "\"installdir\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value.Replace("\\\\", "\\");
                                if (string.Equals(Path.Combine(dir, "common", instDir), path, StringComparison.OrdinalIgnoreCase))
                                {
                                    int.TryParse(Regex.Match(txt, "\"appid\"\\s+\"(\\d+)\"").Groups[1].Value, out appId);
                                    string nm = Regex.Match(txt, "\"name\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"").Groups[1].Value.Replace("\\\\", "\\");
                                    if (!string.IsNullOrEmpty(nm)) name = nm;
                                    goto found;
                                }
                            }
                            catch { }
                        }
                    }
                }
            found:
                if (games.Exists(g => string.Equals(g.InstallPath, path, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(path)))
                {
                    MessageBox.Show(this, "Ce dossier est déjà dans la liste.", "WorkshopDL Launcher",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                games.Add(new DetectedGame { Name = name, AppId = appId, InstallPath = path, Manual = true });
                games.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                SaveManualGames();
                RenderGames();
                statusLabel.Text = "Dossier ajouté : " + name;
            }
        }

        // ---------- analysis ----------
        private void TryPrefillFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsText() &&
                    Clipboard.GetText().IndexOf("steamcommunity.com/sharedfiles", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    urlBox.Text = Clipboard.GetText().Trim();
                }
            }
            catch { }
        }

        private async void AnalyzeAsync()
        {
            string url = urlBox.Text.Trim();
            if (url.Length == 0) { statusLabel.Text = "Collez d'abord un lien du Steam Workshop."; return; }
            if (!url.StartsWith("http")) url = "https://" + url;

            analyzeBtn.Enabled = false;
            resultPanel.Visible = false;
            locateBtn.Visible = false;
            downloadBtn.Enabled = false;
            openWdlBtn.Enabled = false;
            statusLabel.Text = "Analyse de la page du Workshop…";
            try
            {
                var r = await Task.Run(() => WorkshopAnalyzer.Analyze(url));
                current = r;
                if (!r.Ok)
                {
                    statusLabel.Text = r.Error ?? "Échec de l'analyse.";
                    return;
                }
                itemLabel.Text = "Objet : " + (r.ItemTitle ?? ("id " + r.ItemId)) + (r.Removed ? "\n⚠ Retiré du Workshop" : "");

                var match = games.Find(g => g.AppId == r.AppId && r.AppId != 0);
                r.Installed = match != null;
                r.InstallPath = match != null ? match.InstallPath : "";
                autoGame = new DetectedGame
                {
                    AppId = r.AppId,
                    Name = r.GameName ?? "Jeu inconnu",
                    InstallPath = r.InstallPath,
                    Manual = false
                };

                resultPanel.Visible = true;
                // Fills "Envoyer vers" (auto + detected + supported) and applies the
                // default (auto) target: installed status, locate + download buttons.
                BuildTargetCombo();
                ApplyTargetSelection();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Erreur : " + ex.Message;
            }
            finally
            {
                analyzeBtn.Enabled = true;
            }
        }

        // ---------- download ----------
        private async void DownloadAsync()
        {
            if (current == null || !current.Ok) return;
            if (current.Removed)
            {
                statusLabel.Text = "Cet objet a été retiré du Workshop : il n'est plus téléchargeable.";
                return;
            }
            if (current.AppId == 0)
            {
                statusLabel.Text = "Pas d'AppID pour la cible : choisissez un jeu dans « Envoyer vers » (ou laissez « Auto »).";
                return;
            }

            downloadCancelled = false;
            downloadBtn.Enabled = false;
            openWdlBtn.Enabled = false;
            cancelBtn.Visible = true;
            cancelBtn.Enabled = true;
            progress.Visible = true;
            statusLabel.Text = "Préparation de steamcmd…";
            try
            {
                string exe = await Task.Run(() =>
                {
                    string found = SteamCmdRunner.FindSteamCmd(workDir);
                    if (found != null) return found;
                    return SteamCmdRunner.Bootstrap(workDir);
                });
                if (exe == null) { statusLabel.Text = "Impossible de trouver/télécharger steamcmd."; return; }

                bool anon = anonCheck.Checked;
                string output = "";
                int exit = 0;

                if (anon)
                {
                    // Hidden, log parsed for success/failure.
                    string args = string.Format(
                        "+login anonymous +workshop_download_item {0} {1} +quit",
                        current.AppId, current.ItemId);
                    var psi = new ProcessStartInfo(exe, args)
                    {
                        WorkingDirectory = Path.GetDirectoryName(exe),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    var sb = new StringBuilder();
                    var tcs = new TaskCompletionSource<int>();
                    var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    proc.OutputDataReceived += (s, e) => { if (e.Data != null) { lock (sb) sb.AppendLine(e.Data); } };
                    proc.ErrorDataReceived += (s, e) => { if (e.Data != null) { lock (sb) sb.AppendLine(e.Data); } };
                    proc.Exited += (s, e) => tcs.TrySetResult(proc.ExitCode);
                    steamCmdProc = proc;
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    statusLabel.Text = "Téléchargement en cours vers « " + current.GameName + " »…";
                    if (downloadCancelled) KillSteamCmd();
                    exit = await tcs.Task;
                    lock (sb) output = sb.ToString();
                    steamCmdProc = null;
                    if (downloadCancelled)
                    {
                        statusLabel.Text = "✖ Téléchargement annulé.";
                        return;
                    }
                }
                else
                {
                    // Account mode: show the steamcmd window so the user can type
                    // their Steam login + Steam Guard code interactively.
                    var psi = new ProcessStartInfo(exe,
                        string.Format("+login +workshop_download_item {0} {1} +quit", current.AppId, current.ItemId))
                    {
                        WorkingDirectory = Path.GetDirectoryName(exe),
                        UseShellExecute = true
                    };
                    var proc = Process.Start(psi);
                    steamCmdProc = proc;
                    statusLabel.Text = "Connectez-vous à Steam dans la fenêtre steamcmd (compte qui possède le jeu), puis patientez…";
                    if (downloadCancelled) KillSteamCmd();
                    await Task.Run(() => proc.WaitForExit());
                    exit = proc.ExitCode;
                    steamCmdProc = null;
                    if (downloadCancelled)
                    {
                        statusLabel.Text = "✖ Téléchargement annulé.";
                        return;
                    }
                }

                bool success = output.IndexOf("Success. Downloaded item", StringComparison.OrdinalIgnoreCase) >= 0
                    || FindDownloadedItem(current.AppId, current.ItemId) != null;
                if (success)
                {
                    string dest = FindDownloadedItem(current.AppId, current.ItemId);
                    statusLabel.Text = "✔ Téléchargement terminé." + (dest != null ? "\n" + dest : "");
                    if (dest != null)
                    {
                        var open = MessageBox.Show(this,
                            "Objet téléchargé :\n" + dest + "\n\nOuvrir le dossier ?", "WorkshopDL Launcher",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                        if (open == DialogResult.Yes)
                            Process.Start(new ProcessStartInfo("explorer.exe", dest) { UseShellExecute = true });
                    }
                }
                else if (output.IndexOf("Missing decryption key", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    statusLabel.Text = "Steam refuse le téléchargement anonyme pour ce jeu.\nDécochez « Mode anonyme » et connectez-vous avec votre compte Steam (qui possède le jeu).";
                }
                else if (exit != 0)
                {
                    statusLabel.Text = "Échec (code " + exit + ").\nEssayez : décochez le mode anonyme, ou utilisez « Ouvrir dans WorkshopDL ».\n\n" + Tail(output, 400);
                }
                else
                {
                    statusLabel.Text = "Réponse inattendue de steamcmd.\n" + Tail(output, 400);
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Erreur : " + ex.Message;
            }
            finally
            {
                progress.Visible = false;
                cancelBtn.Visible = false;
                cancelBtn.Enabled = false;
                downloadBtn.Enabled = current != null && current.Ok && !current.Removed && current.AppId != 0;
                openWdlBtn.Enabled = current != null && current.Ok;
            }
        }

        private string FindDownloadedItem(int appId, long itemId)
        {
            try
            {
                string exe = SteamCmdRunner.FindSteamCmd(workDir);
                string root = exe != null ? Path.GetDirectoryName(exe) : null;
                var candidates = new List<string>();
                if (root != null)
                {
                    candidates.Add(Path.Combine(root, "steamapps", "workshop", "content", appId.ToString(), itemId.ToString()));
                    candidates.Add(Path.Combine(root, "steamapps", "workshop", "content", appId.ToString()));
                }
                string steam = SteamLocator.FindSteamPath();
                if (steam != null)
                    candidates.Add(Path.Combine(steam, "steamapps", "workshop", "content", appId.ToString(), itemId.ToString()));
                foreach (var c in candidates)
                    if (Directory.Exists(c)) return c;
            }
            catch { }
            return null;
        }

        private void HandOverToWorkshopDL()
        {
            if (current == null || !current.Ok) { statusLabel.Text = "Analysez d'abord un lien."; return; }
            try
            {
                Clipboard.SetText("https://steamcommunity.com/sharedfiles/filedetails/?id=" + current.ItemId);
                string wdl = Path.Combine(workDir, "WorkshopDL.exe");
                if (!File.Exists(wdl))
                {
                    statusLabel.Text = "WorkshopDL.exe introuvable à côté du launcher (" + workDir + ").";
                    return;
                }
                Process.Start(new ProcessStartInfo(wdl) { WorkingDirectory = workDir, UseShellExecute = true });
                statusLabel.Text = "Lien copié dans le presse-papiers — WorkshopDL a été lancé.\nIl détecte l'URL automatiquement (Auto-URL detection).";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Erreur : " + ex.Message;
            }
        }

        private static string Tail(string s, int n)
        {
            if (s == null) return "";
            s = s.Trim();
            return s.Length <= n ? s : "…" + s.Substring(s.Length - n);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                if (steamCmdProc != null && !steamCmdProc.HasExited) steamCmdProc.Kill();
            }
            catch { }
            base.OnFormClosed(e);
        }
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Headless self-test used by CI / verification: prints detection results.
            if (args.Length > 0 && args[0] == "--selftest")
            {
                int fail = 0;
                string steam = SteamLocator.FindSteamPath();
                Console.WriteLine("steam=" + (steam ?? "(not found)"));
                var libs = SteamLocator.FindLibraryFolders(steam);
                Console.WriteLine("libraries=" + libs.Count);
                var games = SteamLocator.Scan(libs);
                Console.WriteLine("games=" + games.Count);
                foreach (var g in games)
                    Console.WriteLine("  game: " + g.Name + " | appid=" + g.AppId + " | " + g.InstallPath);
                if (games.Count == 0) { Console.WriteLine("FAIL: no games detected"); fail = 1; }

                string url = args.Length > 1 ? args[1] : "https://steamcommunity.com/sharedfiles/filedetails/?id=3764231585";
                WorkshopAnalyzer.LoadSupported(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules"));
                WorkshopAnalyzer.LoadSupported("Modules");
                var sup = WorkshopAnalyzer.SupportedGames();
                Console.WriteLine("supported=" + (sup == null ? -1 : sup.Count));
                Console.WriteLine("analyzing: " + url);
                var r = WorkshopAnalyzer.Analyze(url);
                Console.WriteLine("ok=" + r.Ok + " appid=" + r.AppId + " game=" + r.GameName +
                                  " item=" + r.ItemTitle + " itemid=" + r.ItemId + " removed=" + r.Removed);
                if (!r.Ok || r.AppId == 0) { Console.WriteLine("FAIL: analysis failed: " + r.Error); fail = 1; }
                if (r.ItemTitle == null) { Console.WriteLine("FAIL: item title missing"); fail = 1; }

                long? parsed = WorkshopAnalyzer.ParseItemId("https://steamcommunity.com/sharedfiles/filedetails/?id=3764231585");
                Console.WriteLine("parseid=" + parsed);
                if (parsed != 3764231585) { Console.WriteLine("FAIL: ParseItemId"); fail = 1; }

                // Exercise the real form: detection must land in the list control.
                try
                {
                    var form = new MainForm();
                    var fld = typeof(MainForm).GetField("gamesList",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var lb = (ListView)fld.GetValue(form);
                    Console.WriteLine("list_items=" + lb.Items.Count);
                    for (int i = 0; i < lb.Items.Count; i++) Console.WriteLine("  item: " + lb.Items[i].Text);
                    if (lb.Items.Count == 0) { Console.WriteLine("FAIL: games list empty"); fail = 1; }

                    // Exercise the target selector (auto + detected + supported list).
                    var flags2 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var res = new AnalysisResult
                    {
                        Ok = true,
                        AppId = 431240,
                        GameName = "Golf With Your Friends",
                        ItemId = 3764231585,
                        ItemTitle = "Pyramid Par"
                    };
                    typeof(MainForm).GetField("current", flags2).SetValue(form, res);
                    typeof(MainForm).GetField("autoGame", flags2).SetValue(form, new DetectedGame
                    { AppId = 431240, Name = "Golf With Your Friends", InstallPath = "", Manual = false });
                    typeof(MainForm).GetMethod("BuildTargetCombo", flags2).Invoke(form, null);
                    var tc = (ComboBox)typeof(MainForm).GetField("targetCombo", flags2).GetValue(form);
                    Console.WriteLine("target_combo=" + tc.Items.Count + " selected=" + tc.SelectedIndex);
                    for (int i = 0; i < Math.Min(3, tc.Items.Count); i++) Console.WriteLine("  combo: " + tc.Items[i]);
                    if (tc.Items.Count < 8) { Console.WriteLine("FAIL: target combo too small"); fail = 1; }
                    if (tc.SelectedIndex != 0) { Console.WriteLine("FAIL: combo default is not Auto"); fail = 1; }

                    // Force another game (Spacewar) -> the download target must switch.
                    int swIdx = -1;
                    for (int i = 0; i < tc.Items.Count; i++)
                        if (tc.Items[i].ToString().StartsWith("Spacewar ")) { swIdx = i; break; }
                    if (swIdx > 0) tc.SelectedIndex = swIdx;
                    Console.WriteLine("forced_appid=" + res.AppId + " forced_game=" + res.GameName);
                    if (swIdx > 0 && res.AppId != 480) { Console.WriteLine("FAIL: target override did not switch AppID"); fail = 1; }

                    // Hide / unhide round-trip (selection needs a real window handle).
                    int baseCount = lb.Items.Count;
                    if (!lb.IsHandleCreated) { var forcedHandle = lb.Handle; }
                    lb.Items[0].Selected = true;
                    lb.Select();
                    Console.WriteLine("selected_for_hide=" + lb.SelectedIndices.Count);
                    typeof(MainForm).GetMethod("HideSelectedGame", flags2).Invoke(form, null);
                    Console.WriteLine("after_hide=" + lb.Items.Count);
                    if (lb.Items.Count != baseCount - 1) { Console.WriteLine("FAIL: hide did not remove row"); fail = 1; }
                    typeof(MainForm).GetMethod("UnhideAllGames", flags2).Invoke(form, null);
                    Console.WriteLine("after_unhide=" + lb.Items.Count);
                    if (lb.Items.Count != baseCount) { Console.WriteLine("FAIL: unhide did not restore rows"); fail = 1; }

                    form.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FAIL: form check " + ex.Message);
                    fail = 1;
                }

                Console.WriteLine(fail == 0 ? "SELFTEST OK" : "SELFTEST FAILED");
                Environment.Exit(fail);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
