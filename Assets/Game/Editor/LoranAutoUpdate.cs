#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Loran.EditorTools
{
    /// <summary>
    /// Keeps the game code up to date from GitHub, so nobody has to download anything.
    /// Whenever you click back into Unity (and not more than once every 20 seconds), it checks
    /// github.com/davidmoranofficial-boop/corruption-of-gaia for new changes. If there are any, it copies
    /// Assets/Game and Tools from the repository into this project and Unity reloads them.
    ///
    /// It never runs during Play mode; it waits until you stop. Turn it off or check by hand from the
    /// "Corruption of Gaia" menu. Anything you change by hand inside Assets/Game will be replaced by the next update.
    /// Needs git installed (it is on most Linux systems; on Windows install Git for Windows).
    /// </summary>
    [InitializeOnLoad]
    public static class LoranAutoUpdate
    {
        const string RepoUrl = "https://github.com/davidmoranofficial-boop/corruption-of-gaia.git";
        const string Branch = "main";
        const string EnabledKey = "CorruptionOfGaia.AutoUpdate";
        const double MinSecondsBetweenChecks = 20;
        static readonly string[] Synced = { "Assets/Game", "Tools" };

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        static string Clone => Path.Combine(ProjectRoot, "Library", "CorruptionOfGaiaSync");
        static string AppliedFile => Path.Combine(ProjectRoot, "Library", "CorruptionOfGaiaSync.applied");

        static bool running;
        static double lastCheck = -1000;
        static volatile string finished;   // set by the background job, picked up on the main thread
        static volatile bool needsRefresh;
        static bool warnedNoGit;

        static LoranAutoUpdate()
        {
            EditorApplication.focusChanged += focused => { if (focused) Check(false); };
            EditorApplication.update += MainThread;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) Check(false); };
            EditorApplication.delayCall += () => Check(false);
        }

        static bool Enabled => EditorPrefs.GetBool(EnabledKey, true);

        [MenuItem("Corruption of Gaia/Check for updates now")]
        static void CheckNow() => Check(true);

        [MenuItem("Corruption of Gaia/Auto-update: turn on or off")]
        static void Toggle()
        {
            EditorPrefs.SetBool(EnabledKey, !Enabled);
            Debug.Log("[Corruption of Gaia] Auto-update is now " + (Enabled ? "ON" : "OFF") + ".");
        }

        static void Check(bool byHand)
        {
            if (running || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (!byHand && (!Enabled || EditorApplication.timeSinceStartup - lastCheck < MinSecondsBetweenChecks)) return;
            lastCheck = EditorApplication.timeSinceStartup;
            running = true;
            string root = ProjectRoot, clone = Clone, applied = AppliedFile;
            Task.Run(() =>
            {
                try { finished = Update(root, clone, applied, byHand); }
                catch (Exception e) { finished = "!Update failed: " + e.Message; }
            });
        }

        static void MainThread()
        {
            string msg = finished;
            if (msg == null) return;
            finished = null;
            running = false;
            if (needsRefresh)
            {
                needsRefresh = false;
                AssetDatabase.Refresh();
                ShowNotification("Game updated");
            }
            if (msg.StartsWith("!")) Debug.LogWarning("[Corruption of Gaia] " + msg.Substring(1));
            else if (msg != "") Debug.Log("[Corruption of Gaia] " + msg);
        }

        static void ShowNotification(string text)
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (w.GetType().Name == "GameView" || w.GetType().Name == "SceneView") w.ShowNotification(new GUIContent(text));
        }

        // ------------------------------------------------------------------ background work (no Unity API in here)

        static string Update(string root, string clone, string appliedFile, bool byHand)
        {
            if (!Directory.Exists(Path.Combine(clone, ".git")))
            {
                if (Directory.Exists(clone)) Directory.Delete(clone, true);
                var (ok, output) = Git(root, "clone --depth 1 --branch " + Branch + " " + RepoUrl + " \"" + clone + "\"");
                if (!ok) return NoGit(output);
            }
            else
            {
                var (ok, output) = Git(clone, "fetch --depth 1 origin " + Branch);
                if (!ok) return NoGit(output);
                Git(clone, "reset --hard FETCH_HEAD");
            }

            string head = Git(clone, "rev-parse HEAD").output.Trim();
            string applied = File.Exists(appliedFile) ? File.ReadAllText(appliedFile).Trim() : "";
            if (head == applied) return byHand ? "Already up to date." : "";

            int changed = 0, removed = 0;
            foreach (var folder in Synced)
            {
                string from = Path.Combine(clone, folder), to = Path.Combine(root, folder);
                if (!Directory.Exists(from)) continue;
                var (c, r) = Mirror(from, to);
                changed += c;
                removed += r;
            }
            File.WriteAllText(appliedFile, head);
            needsRefresh = changed + removed > 0;
            string subject = Git(clone, "log -1 --format=%s").output.Trim();
            return "Updated: " + subject + " (" + changed + " file(s) changed, " + removed + " removed).";
        }

        static string NoGit(string output)
        {
            if (output.Contains("not found") || output.Contains("No such file") || output.Contains("cannot find"))
            {
                if (warnedNoGit) return "";
                warnedNoGit = true;
                return Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? "!Git isn't installed, so the game can't update itself. Install Git for Windows from https://git-scm.com/download/win (keep the default options), then restart Unity."
                    : "!git isn't installed, so the game can't update itself. Install it with: sudo apt install git";
            }
            return "!Couldn't reach GitHub to check for updates. " + output.Trim();
        }

        /// <summary>Copies every file across that differs, and removes files (and their .meta) that the repository no longer has.</summary>
        static (int changed, int removed) Mirror(string from, string to)
        {
            int changed = 0, removed = 0;
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var src in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(from, src);
                if (rel.EndsWith(".meta")) continue;   // Unity makes its own .meta files
                wanted.Add(rel);
                string dst = Path.Combine(to, rel);
                byte[] data = File.ReadAllBytes(src);
                if (File.Exists(dst) && File.ReadAllBytes(dst).SequenceEqual(data)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.WriteAllBytes(dst, data);
                changed++;
            }
            if (Directory.Exists(to))
                foreach (var dst in Directory.GetFiles(to, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(to, dst);
                    if (rel.EndsWith(".meta")) continue;
                    if (rel.Split(Path.DirectorySeparatorChar).Any(p => p == "bin" || p == "obj")) continue;   // test build output
                    if (wanted.Contains(rel)) continue;
                    File.Delete(dst);
                    if (File.Exists(dst + ".meta")) File.Delete(dst + ".meta");
                    removed++;
                }
            return (changed, removed);
        }

        /// <summary>"git" if it's on the PATH; on Windows, falls back to where Git for Windows installs itself.</summary>
        static string GitExe()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return "git";
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                try { if (dir != "" && File.Exists(Path.Combine(dir.Trim('"'), "git.exe"))) return "git"; } catch { }
            foreach (var env in new[] { "ProgramFiles", "ProgramW6432", "ProgramFiles(x86)", "LOCALAPPDATA" })
            {
                string baseDir = Environment.GetEnvironmentVariable(env);
                if (string.IsNullOrEmpty(baseDir)) continue;
                foreach (var sub in new[] { @"Git\cmd\git.exe", @"Programs\Git\cmd\git.exe" })
                {
                    string candidate = Path.Combine(baseDir, sub);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return "git";
        }

        static (bool ok, string output) Git(string dir, string args)
        {
            try
            {
                var p = new Process
                {
                    StartInfo = new ProcessStartInfo(GitExe(), "-c core.autocrlf=false " + args)
                    {
                        WorkingDirectory = dir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };
                p.StartInfo.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                p.Start();
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } return (false, "git took too long"); }
                return (p.ExitCode == 0, outTask.Result + errTask.Result);
            }
            catch (Exception e)
            {
                return (false, "git not found: " + e.Message);
            }
        }
    }
}
#endif
