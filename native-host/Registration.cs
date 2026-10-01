using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace AwsLoginHelperHost
{
    /// <summary>
    /// Handles everything needed to make Edge aware of this native host, and to make sure
    /// the companion Edge extension is actually installed - both without ever needing admin
    /// rights, since everything here is written under HKEY_CURRENT_USER and %LOCALAPPDATA%.
    ///
    /// This is the Edge counterpart of the Chrome build's Registration.cs. The two use
    /// DIFFERENT registry hives (Edge only ever reads its own
    /// Software\Microsoft\Edge\NativeMessagingHosts, never Chrome's
    /// Software\Google\Chrome\NativeMessagingHosts, even though both browsers are Chromium
    /// and speak the identical native messaging wire protocol) and DIFFERENT host names, so
    /// installing this one never interferes with a Chrome install of the same overall tool on
    /// the same machine.
    /// </summary>
    internal static class Registration
    {
        public const string HostName = "com.penchala_services_inc.awsloginhelper_edge";

        // Filled in once the extension is published to the Microsoft Edge Add-ons store. Until
        // then this stays a placeholder and --check-extension / --register will say so instead
        // of silently no-op'ing.
        private const string ExtensionIdPlaceholder = "REPLACE_WITH_EDGE_ADDONS_ID";

        // IMPORTANT: this is published with PublishSingleFile=true, which means
        // Assembly.GetExecutingAssembly().Location returns "" at runtime (there is no
        // separate physical DLL to point at - everything is bundled into one .exe). The
        // supported way to get the real, on-disk .exe path in that mode is
        // Environment.ProcessPath (.NET 6+).
        private static string ExePath => Environment.ProcessPath;
        private static string InstallDir => Path.GetDirectoryName(ExePath);
        private static string ExtensionIdFile => Path.Combine(InstallDir, "extension-id.txt");
        private static string ManifestFile => Path.Combine(InstallDir, "native-messaging-host.json");

        /// <summary>
        /// Returns every extension id currently allowed to talk to this native host - ALL of
        /// them get written into allowed_origins, so a published Edge Add-ons install and any
        /// number of local unpacked/dev-loaded installs (each of which gets its own, different,
        /// machine/path-specific id) can all work side by side on the same machine.
        ///
        /// extension-id.txt holds one id per line and is additive, never overwritten wholesale:
        /// an explicit <paramref name="overrideExtensionId"/> (the MSI's EXTENSIONID property,
        /// passed through from --register's own argument) is ADDED to whatever ids are already
        /// on file rather than replacing them, and persisted back (once, the first time it's
        /// seen) so it also sticks for CheckExtensionInstalled and for any later bare
        /// --register run. This means overriding to point at a local unpacked extension never
        /// breaks the published one on that same machine.
        /// </summary>
        public static List<string> GetConfiguredExtensionIds(string overrideExtensionId = null)
        {
            var ids = new List<string>();
            if (File.Exists(ExtensionIdFile))
            {
                ids.AddRange(File.ReadAllLines(ExtensionIdFile)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrEmpty(line)));
            }

            string overrideTrimmed = overrideExtensionId?.Trim();
            if (!string.IsNullOrEmpty(overrideTrimmed) && !ids.Contains(overrideTrimmed, StringComparer.OrdinalIgnoreCase))
            {
                ids.Add(overrideTrimmed);
                try
                {
                    File.AppendAllLines(ExtensionIdFile, new[] { overrideTrimmed });
                    Logger.Log($"GetConfiguredExtensionIds: added new override id to {ExtensionIdFile}: {overrideTrimmed}");
                }
                catch (Exception ex)
                {
                    // Still usable for THIS run even if persisting it failed - just won't
                    // stick for a later bare --register.
                    Logger.LogError($"GetConfiguredExtensionIds: failed to persist override id (non-fatal): {ex}");
                }
            }

            if (ids.Count == 0) ids.Add(ExtensionIdPlaceholder);
            return ids;
        }

        /// <summary>
        /// Writes native-messaging-host.json next to this executable (with this executable's
        /// own absolute path baked in) and points Edge at it via the per-user registry key
        /// Chromium browsers document for native messaging hosts (Edge reads its OWN registry
        /// hive - see the class-level note above). Safe to call repeatedly (e.g. every
        /// install/upgrade, or by hand after publishing the extension and editing
        /// extension-id.txt).
        ///
        /// <paramref name="extensionIdOverride"/> is how the MSI's EXTENSIONID property (see
        /// Package.wxs) reaches this - defaults to the Edge Add-ons id there, but anyone
        /// installing locally can pass their own unpacked extension's id instead via
        /// `msiexec /i AWS-Login-Helper-Host-Edge.msi EXTENSIONID=<their id>`. When given, it's
        /// ADDED to extension-id.txt (see GetConfiguredExtensionIds) rather than replacing it,
        /// so it also sticks for CheckExtensionInstalled below and for any later bare
        /// `--register` run (by the installer on upgrade, or by hand) - without dropping any
        /// other id that was already allowed.
        /// </summary>
        public static void RegisterHost(string extensionIdOverride = null)
        {
            // Console.WriteLine below is effectively invisible when this runs as an MSI custom
            // action (Return="ignore" on that action also means MSI swallows any exception here
            // completely, with no error dialog and no failed-install signal at all) - so this is
            // logged to host.log too, the only place a silent failure during an automated install
            // leaves any trace. This exact gap is what let a broken registration go unnoticed
            // through several rounds of testing on the Chrome build: it always happened to get
            // fixed by someone separately running --register by hand afterward, which masked the
            // custom action itself never having been verified working on its own.
            Logger.Log("RegisterHost: starting");
            try
            {
                string exePath = ExePath;
                List<string> extensionIds = GetConfiguredExtensionIds(extensionIdOverride);
                Logger.Log($"RegisterHost: exePath={exePath}, extensionIds=[{string.Join(", ", extensionIds)}], manifestFile={ManifestFile}");

                var manifest = new Newtonsoft.Json.Linq.JObject
                {
                    ["name"] = HostName,
                    ["description"] = "AWS Login Helper Native (Edge) - native messaging host",
                    ["path"] = exePath,
                    ["type"] = "stdio",
                    // "chrome-extension://" is the correct scheme name for BOTH browsers here -
                    // Edge extensions are still addressed with this scheme for Chromium
                    // compatibility, there is no separate "edge-extension://" scheme.
                    ["allowed_origins"] = new Newtonsoft.Json.Linq.JArray(
                        extensionIds.Select(id => (object)$"chrome-extension://{id}/")),
                };

                File.WriteAllText(ManifestFile, manifest.ToString());
                Logger.Log($"RegisterHost: wrote manifest file ({new FileInfo(ManifestFile).Length} bytes)");

                using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Edge\NativeMessagingHosts\{HostName}"))
                {
                    key.SetValue(null, ManifestFile);
                }
                Logger.Log("RegisterHost: wrote registry value");

                Console.WriteLine($"Registered native host manifest at {ManifestFile}");
                if (extensionIds.Count == 1 && extensionIds[0] == ExtensionIdPlaceholder)
                {
                    Console.WriteLine(
                        "WARNING: extension-id.txt is not set yet. Edge will reject messages from the " +
                        "real extension until you put the published Microsoft Edge Add-ons extension ID " +
                        $"into {ExtensionIdFile} and re-run --register.");
                    Logger.Log("RegisterHost: WARNING extension-id.txt still has the placeholder value");
                }

                CheckExtensionInstalled(openStoreIfMissing: true);
                Logger.Log("RegisterHost: completed successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError($"RegisterHost: FAILED - {ex}");
                throw; // preserve the real exit code / error for anyone running --register by hand
            }
        }

        public static void UnregisterHost()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Edge\NativeMessagingHosts\{HostName}", throwOnMissingSubKey: false);
                Console.WriteLine("Removed native messaging host registry entry.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not remove registry entry: " + ex.Message);
            }
        }

        /// <summary>
        /// Looks for ANY of the configured extension ids under every Edge profile this
        /// Windows user has - both CRX/Add-ons-store installs (which get their own folder
        /// under "Extensions") AND unpacked Developer-Mode installs (which Edge never copies
        /// anywhere; it only records their id inside the profile's own "Preferences" file).
        /// Missing that second case was a real bug on the Chrome build this was ported from:
        /// it meant this always reported "not found" - and kept reopening the store page -
        /// for anyone testing with an unpacked/local load rather than a published extension.
        /// Checking every configured id (not just one) means this correctly reports
        /// "installed" as soon as EITHER the published extension OR a locally-registered
        /// unpacked one is found - a machine set up for local dev doesn't need the published
        /// one too just to pass this check.
        /// </summary>
        public static bool CheckExtensionInstalled(bool openStoreIfMissing, string extensionIdOverride = null)
        {
            List<string> extensionIds = GetConfiguredExtensionIds(extensionIdOverride);
            if (extensionIds.Count == 1 && extensionIds[0] == ExtensionIdPlaceholder)
            {
                Console.WriteLine("Extension ID not configured yet (see extension-id.txt) - skipping install check.");
                return false;
            }

            string edgeUserData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Edge", "User Data");

            bool found = false;
            if (Directory.Exists(edgeUserData))
            {
                foreach (var profileDir in Directory.GetDirectories(edgeUserData))
                {
                    if (extensionIds.Any(id =>
                        Directory.Exists(Path.Combine(profileDir, "Extensions", id)) ||
                        IsExtensionInPreferences(profileDir, id)))
                    {
                        found = true;
                        break;
                    }
                }
            }

            if (found)
            {
                Console.WriteLine("Extension is already installed.");
                return true;
            }

            Console.WriteLine("Extension not found in any Edge profile.");
            if (openStoreIfMissing)
            {
                // The first configured id is the one that's meant to be the published Edge
                // Add-ons listing (extension-id.txt's original entry, before any local
                // override got appended after it) - that's the one worth sending someone to
                // install from the store. Confirmed working with a bare id, no slug required:
                // https://microsoftedge.microsoft.com/addons/detail/<id>
                string storeUrl = $"https://microsoftedge.microsoft.com/addons/detail/{extensionIds[0]}";
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = EdgeManager.FindEdgeExe(), Arguments = storeUrl, UseShellExecute = false });
                }
                catch
                {
                    // Fall back to whatever the OS considers the default handler for the URL.
                    Process.Start(new ProcessStartInfo { FileName = storeUrl, UseShellExecute = true });
                }
            }

            return false;
        }

        /// <summary>
        /// Edge records every extension it has ever loaded for a profile - unpacked or not -
        /// under extensions.settings.&lt;id&gt; in that profile's "Preferences" (sometimes
        /// "Secure Preferences") JSON file. Reading it is the only way to detect an unpacked/
        /// Developer-Mode load, since those are never copied into an "Extensions" folder.
        /// </summary>
        private static bool IsExtensionInPreferences(string profileDir, string extensionId)
        {
            foreach (var fileName in new[] { "Preferences", "Secure Preferences" })
            {
                string path = Path.Combine(profileDir, fileName);
                if (!File.Exists(path)) continue;

                try
                {
                    var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
                    var settings = json["extensions"]?["settings"] as Newtonsoft.Json.Linq.JObject;
                    if (settings != null && settings[extensionId] != null) return true;
                }
                catch
                {
                    // Preferences can be mid-write while Edge is running, or not valid JSON
                    // for some other reason - skip rather than fail the whole check over it.
                }
            }
            return false;
        }
    }
}
