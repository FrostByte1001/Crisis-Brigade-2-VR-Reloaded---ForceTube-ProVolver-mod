using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ForceTubePatch
{
    [BepInPlugin(GUID, PluginName, Version)]
    public class ForceTubePlugin : BasePlugin
    {
        public const string GUID = "com.forcetube.crisisvrigade2";
        public const string PluginName = "Crisis VRigade 2 ForceTube";
        public const string Version = "1.0.0";

        private static ForceTubePlugin Instance;
        private static bool forceTubeInitialized = false;
        private static bool initializationComplete = false;  // Prevents hot-plug from running during init
        private static float lastDeviceCheckTime = 0f;
        private static HashSet<string> configuredDevices = new HashSet<string>();

        // Configuration
        private static ConfigEntry<bool> configVerboseLogging;
        private static ConfigEntry<bool> configTestMode;
        private static ConfigEntry<float> configTestDuration;
        private static ConfigEntry<bool> configEnableAutoConfig;
        private static ConfigEntry<float> configDeviceCheckInterval;

        // Device configurations (up to 4 devices)
        private static Dictionary<string, DeviceConfig> deviceConfigs = new Dictionary<string, DeviceConfig>();

        private class DeviceConfig
        {
            public ConfigEntry<string> DeviceID;
            public ConfigEntry<string> Channels;
            public ConfigEntry<string> FriendlyName;
            public List<int> ParsedChannels;
        }

        // ===== FORCETUBE DLL IMPORTS =====
        [DllImport("ForceTubeVR_API_x64.dll")]
        private static extern void InitAsync(bool pistolsFirst);

        [DllImport("ForceTubeVR_API_x64.dll")]
        private static extern void ShotChannel(byte kickPower, byte rumblePower, float rumbleDuration, int channel);

        [DllImport("ForceTubeVR_API_x64.dll")]
        private static extern void KickChannel(byte power, int channel);

        [DllImport("ForceTubeVR_API_x64.dll")]
        private static extern void RumbleChannel(byte power, float timeInSeconds, int channel);

        [DllImport("ForceTubeVR_API_x64.dll")]
        private static extern byte TempoToKickPower(float tempo);

        [DllImport("ForceTubeVR_API_x64.dll")]
        private static extern string ListConnectedForceTube();

        [DllImport("ForceTubeVR_API_x64.dll")]
        private static extern bool AddToChannel(int nChannel, string sName);

        [DllImport("ForceTubeVR_API_x64.dll")]
        private static extern void ClearChannel(int nChannel);

        [DllImport("ForceTubeVR_API_x64.dll")]
        private static extern bool InitChannels(string sJsonChannelList);

        [DllImport("ForceTubeVR_API_x64.dll")]
        private static extern void SetActive(bool active);

        // ===== PLUGIN INITIALIZATION =====
        public override void Load()
        {
            Instance = this;
            Instance.Log.LogInfo($"========================================");
            Instance.Log.LogInfo($"Plugin {PluginName} v{Version} is loading...");
            Instance.Log.LogInfo($"========================================");

            // Setup configuration
            SetupConfiguration();

            // Initialize ForceTube
            try
            {
                InitAsync(false);  // false = rifle priority
                Instance.Log.LogInfo("ForceTube InitAsync called, waiting for devices...");

                // Activate ForceTube API
                SetActive(true);
                Instance.Log.LogInfo("ForceTube SetActive(true) called");

                // RETRY LOOP: Poll for devices for up to 15 seconds (game takes ~20s to start)
                bool devicesFound = false;
                int maxAttempts = 30;  // 30 attempts × 500ms = 15 seconds

                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    System.Threading.Thread.Sleep(500);

                    try
                    {
                        string jsonDevices = ListConnectedForceTube();

                        if (!string.IsNullOrEmpty(jsonDevices) && jsonDevices.Contains("\"Connected\""))
                        {
                            using (JsonDocument doc = JsonDocument.Parse(jsonDevices))
                            {
                                if (doc.RootElement.TryGetProperty("Connected", out JsonElement devices))
                                {
                                    int count = devices.GetArrayLength();
                                    if (count > 0)
                                    {
                                        float elapsedTime = (attempt + 1) * 0.5f;
                                        Instance.Log.LogInfo($"SUCCESS: {count} device(s) found after {elapsedTime:F1} seconds!");
                                        devicesFound = true;
                                        break;
                                    }
                                }
                            }
                        }

                        // Log progress every 2 seconds
                        if (attempt > 0 && attempt % 4 == 0)
                        {
                            float elapsedTime = attempt * 0.5f;
                            Instance.Log.LogInfo($"  Still waiting for devices... ({elapsedTime:F1}s elapsed)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Instance.Log.LogError($"  Error checking devices on attempt {attempt + 1}: {ex.Message}");
                    }
                }

                if (devicesFound)
                {
                    // Devices found - configure them
                    DetectAndConfigureDevices();
                    forceTubeInitialized = true;
                    Instance.Log.LogInfo("ForceTube initialization successful!");

                    // TEST: Try rumbling all channels to verify API works
                    Instance.Log.LogInfo("TEST: Sending test rumble to all channels...");
                    for (int testChannel = 2; testChannel <= 5; testChannel++)
                    {
                        try
                        {
                            RumbleChannel(150, 0.5f, testChannel);
                            Instance.Log.LogInfo($"  Test rumble sent to channel {testChannel}");
                        }
                        catch (Exception ex)
                        {
                            Instance.Log.LogError($"  Test rumble failed on channel {testChannel}: {ex.Message}");
                        }
                    }
                    System.Threading.Thread.Sleep(1000);  // Wait for test rumbles to complete
                }
                else
                {
                    Instance.Log.LogWarning("No ForceTube devices found after 15 seconds");
                    Instance.Log.LogWarning("Hot-plug detection will activate during gameplay");
                    forceTubeInitialized = true;  // Still set to true so hot-plug works
                }
            }
            catch (Exception e)
            {
                Instance.Log.LogError($"ForceTube initialization failed: {e.Message}");
                Instance.Log.LogError("Plugin will continue but ForceTube won't work.");
                forceTubeInitialized = false;
            }
            finally
            {
                initializationComplete = true;  // ALWAYS set this, even on failure
                Instance.Log.LogInfo("Initialization phase complete - hot-plug detection now active");
            }

            // Apply Harmony patches
            try
            {
                Harmony harmony = new Harmony(GUID);
                harmony.PatchAll();
                Instance.Log.LogInfo("Harmony patches applied!");
            }
            catch (Exception e)
            {
                Instance.Log.LogError($"Harmony patching failed: {e}");
            }

            Instance.Log.LogInfo($"Plugin {PluginName} loaded successfully!");
            Instance.Log.LogInfo($"========================================");
        }

        void SetupConfiguration()
        {
            // General settings
            configEnableAutoConfig = Config.Bind("General",
                "EnableAutoConfig",
                true,
                "Enable automatic device detection and channel configuration");

            // Advanced settings
            configVerboseLogging = Config.Bind("Advanced",
                "VerboseLogging",
                false,
                "Log detailed device detection and channel assignment information");

            configTestMode = Config.Bind("Advanced",
                "TestModeOnStartup",
                false,
                "Test mode: Rumble each configured device on game startup to help identify them");

            configTestDuration = Config.Bind("Advanced",
                "TestRumbleDuration",
                2.0f,
                new ConfigDescription(
                    "How long to rumble each device in test mode (seconds)",
                    new AcceptableValueRange<float>(0.5f, 5.0f)
                ));

            configDeviceCheckInterval = Config.Bind("Advanced",
                "DeviceCheckInterval",
                10.0f,
                new ConfigDescription(
                    "How often to check for new ForceTube devices (seconds). Allows hot-plugging devices after game start.",
                    new AcceptableValueRange<float>(5.0f, 60.0f)
                ));

            // Device configurations (4 slots)
            for (int i = 1; i <= 4; i++)
            {
                string section = $"Device_{i}";

                string defaultChannels = "";
                if (i == 1)
                    defaultChannels = "2,3";  // Rifle device: rifleButt, rifleBolt
                else if (i == 2)
                    defaultChannels = "4,5,2,3";  // ProVolver: pistol channels + rifle channels

                var deviceConfig = new DeviceConfig
                {
                    DeviceID = Config.Bind(section,
                        "DeviceID",
                        "",
                        "Device ID (auto-detected from connected ForceTube devices). Leave empty to disable this slot."),

                    Channels = Config.Bind(section,
                        "Channels",
                        defaultChannels,
                        "Channel assignment for this device (comma-separated channel numbers)\n" +
                        "Channel meanings:\n" +
                        "  2 = RifleButt   (rifle stock position)\n" +
                        "  3 = RifleBolt   (rifle bolt position)\n" +
                        "  4 = Pistol1     (right-hand pistol)\n" +
                        "  5 = Pistol2     (left-hand pistol)\n\n" +
                        "Common configurations:\n" +
                        "  2,3     = Rifle device (both rifle channels)\n" +
                        "  4,5     = Pistol device for both hands\n" +
                        "  4       = Right-hand pistol only\n" +
                        "  5       = Left-hand pistol only\n" +
                        "  2,3,4,5 = All weapons trigger this device\n" +
                        "  (empty) = Disabled"),

                    FriendlyName = Config.Bind(section,
                        "FriendlyName",
                        "",
                        "Friendly name for this device (optional, for your reference)")
                };

                deviceConfigs[section] = deviceConfig;
            }
        }

        void DetectAndConfigureDevices()
        {
            try
            {
                // Clear all channels before assigning devices (matches Onward plugin approach)
                Instance.Log.LogInfo("Clearing all ForceTube channels...");
                for (int channel = 2; channel <= 5; channel++)
                {
                    ClearChannel(channel);
                }
                Instance.Log.LogInfo("Channels cleared");

                // Get list of connected devices
                string jsonDevices = ListConnectedForceTube();

                if (configVerboseLogging.Value)
                {
                    Instance.Log.LogInfo($"Connected devices JSON: {jsonDevices}");
                }

                // Parse JSON
                using (JsonDocument doc = JsonDocument.Parse(jsonDevices))
                {
                    JsonElement root = doc.RootElement;

                    if (!root.TryGetProperty("Connected", out JsonElement connectedDevicesElement))
                    {
                        Instance.Log.LogWarning("No ForceTube devices detected!");
                        return;
                    }

                    int deviceCount = connectedDevicesElement.GetArrayLength();
                    if (deviceCount == 0)
                    {
                        Instance.Log.LogWarning("No ForceTube devices detected!");
                        return;
                    }

                    Instance.Log.LogInfo($"Found {deviceCount} ForceTube device(s):");

                    // Auto-configure devices if enabled
                    if (configEnableAutoConfig.Value)
                    {
                        int i = 0;
                        foreach (JsonElement deviceElement in connectedDevicesElement.EnumerateArray())
                        {
                            if (i >= 4) break;

                            // Handle both JSON formats:
                            // Object format: {"batteryLevel": 97, "name": "ForceTubeVR 1234"}
                            // String format: "ForceTubeVR 1234"
                            string deviceID = null;
                            if (deviceElement.ValueKind == JsonValueKind.Object)
                            {
                                if (deviceElement.TryGetProperty("name", out JsonElement nameElement))
                                {
                                    deviceID = nameElement.GetString();
                                }
                            }
                            else if (deviceElement.ValueKind == JsonValueKind.String)
                            {
                                deviceID = deviceElement.GetString();
                            }

                            if (string.IsNullOrEmpty(deviceID))
                            {
                                Instance.Log.LogWarning($"  Skipping device {i + 1}: Could not parse device ID from JSON");
                                i++;
                                continue;
                            }

                            string section = $"Device_{i + 1}";

                            // Only auto-configure if not already set
                            if (string.IsNullOrEmpty(deviceConfigs[section].DeviceID.Value))
                            {
                                deviceConfigs[section].DeviceID.Value = deviceID;
                                Instance.Log.LogInfo($"  Auto-configured {section}: {deviceID}");
                            }

                            // Track that we've configured this device
                            configuredDevices.Add(deviceID);

                            i++;
                        }

                        // Save config
                        Config.Save();
                    }
                }

                // Configure channels for each device
                foreach (var kvp in deviceConfigs)
                {
                    string section = kvp.Key;
                    DeviceConfig cfg = kvp.Value;

                    if (string.IsNullOrEmpty(cfg.DeviceID.Value))
                        continue;

                    // Parse channel list
                    cfg.ParsedChannels = ParseChannels(cfg.Channels.Value);

                    if (cfg.ParsedChannels.Count == 0)
                    {
                        Instance.Log.LogWarning($"{section} ({cfg.DeviceID.Value}) has no channels configured!");
                        continue;
                    }

                    // Add device to each configured channel
                    foreach (int channel in cfg.ParsedChannels)
                    {
                        try
                        {
                            bool success = AddToChannel(channel, cfg.DeviceID.Value);
                            if (configVerboseLogging.Value)
                            {
                                Instance.Log.LogInfo($"  {section}: Added '{cfg.DeviceID.Value}' to channel {channel} - {(success ? "SUCCESS" : "FAILED")}");
                            }
                        }
                        catch (Exception e)
                        {
                            Instance.Log.LogError($"Failed to add {cfg.DeviceID.Value} to channel {channel}: {e.Message}");
                        }
                    }

                    string channelStr = string.Join(", ", cfg.ParsedChannels);
                    string name = string.IsNullOrEmpty(cfg.FriendlyName.Value) ? cfg.DeviceID.Value : cfg.FriendlyName.Value;
                    Instance.Log.LogInfo($"  {section}: {name} -> Channels [{channelStr}]");
                }

                // Test mode
                if (configTestMode.Value)
                {
                    Instance.Log.LogInfo("Test mode enabled - rumbling each device...");
                    foreach (var kvp in deviceConfigs)
                    {
                        DeviceConfig cfg = kvp.Value;
                        if (cfg.ParsedChannels != null && cfg.ParsedChannels.Count > 0)
                        {
                            foreach (int channel in cfg.ParsedChannels)
                            {
                                RumbleChannel(150, configTestDuration.Value, channel);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Instance.Log.LogError($"Device detection failed: {e.Message}");
                Instance.Log.LogError($"Stack trace: {e.StackTrace}");
            }
        }

        List<int> ParseChannels(string channelString)
        {
            List<int> channels = new List<int>();

            if (string.IsNullOrWhiteSpace(channelString))
                return channels;

            string[] parts = channelString.Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string part in parts)
            {
                if (int.TryParse(part.Trim(), out int channel))
                {
                    if (channel >= 2 && channel <= 7)  // Valid channel range
                    {
                        channels.Add(channel);
                    }
                }
            }

            return channels;
        }

        void CheckForNewDevices()
        {
            // Wait for initialization phase to complete before hot-plugging
            if (!initializationComplete)
                return;

            if (!forceTubeInitialized)
                return;

            // Throttle checks based on config interval
            float currentTime = Time.time;
            if (currentTime - lastDeviceCheckTime < configDeviceCheckInterval.Value)
                return;

            lastDeviceCheckTime = currentTime;

            try
            {
                // Get current connected devices
                string jsonDevices = ListConnectedForceTube();

                using (JsonDocument doc = JsonDocument.Parse(jsonDevices))
                {
                    JsonElement root = doc.RootElement;

                    if (!root.TryGetProperty("Connected", out JsonElement connectedDevicesElement))
                        return;

                    bool foundNewDevice = false;
                    int deviceIndex = 0;

                    foreach (JsonElement deviceElement in connectedDevicesElement.EnumerateArray())
                    {
                        // Parse device ID (handle both object and string formats)
                        string deviceID = null;
                        if (deviceElement.ValueKind == JsonValueKind.Object)
                        {
                            if (deviceElement.TryGetProperty("name", out JsonElement nameElement))
                                deviceID = nameElement.GetString();
                        }
                        else if (deviceElement.ValueKind == JsonValueKind.String)
                        {
                            deviceID = deviceElement.GetString();
                        }

                        if (string.IsNullOrEmpty(deviceID))
                            continue;

                        // Check if this device already exists in any config slot
                        bool deviceExistsInConfig = false;
                        foreach (var kvp in deviceConfigs)
                        {
                            if (kvp.Value.DeviceID.Value == deviceID)
                            {
                                deviceExistsInConfig = true;
                                // Device exists in config but wasn't in tracker (probably wasn't connected at startup)
                                if (!configuredDevices.Contains(deviceID))
                                {
                                    // Re-register to channels in case it wasn't done at startup
                                    kvp.Value.ParsedChannels = ParseChannels(kvp.Value.Channels.Value);
                                    foreach (int channel in kvp.Value.ParsedChannels)
                                    {
                                        bool success = AddToChannel(channel, deviceID);
                                        Instance.Log.LogInfo($"  {kvp.Key}: AddToChannel({channel}, '{deviceID}') -> {(success ? "SUCCESS" : "FAILED")}");
                                    }
                                    configuredDevices.Add(deviceID);

                                    string channelStr = string.Join(", ", kvp.Value.ParsedChannels);
                                    Instance.Log.LogInfo($"Hot-plugged device detected: {kvp.Key}: {deviceID} -> Channels [{channelStr}]");
                                    foundNewDevice = true;
                                }
                                break;
                            }
                        }

                        // Only add to new slot if device truly doesn't exist anywhere
                        if (!deviceExistsInConfig && !configuredDevices.Contains(deviceID))
                        {
                            // Find first available device slot
                            for (int i = 1; i <= 4; i++)
                            {
                                string section = $"Device_{i}";
                                DeviceConfig cfg = deviceConfigs[section];

                                if (string.IsNullOrEmpty(cfg.DeviceID.Value))
                                {
                                    cfg.DeviceID.Value = deviceID;
                                    cfg.ParsedChannels = ParseChannels(cfg.Channels.Value);

                                    // Add device to channels
                                    foreach (int channel in cfg.ParsedChannels)
                                    {
                                        AddToChannel(channel, deviceID);
                                    }

                                    configuredDevices.Add(deviceID);
                                    foundNewDevice = true;

                                    string channelStr = string.Join(", ", cfg.ParsedChannels);
                                    Instance.Log.LogInfo($"Hot-plugged device detected: {section}: {deviceID} -> Channels [{channelStr}]");

                                    // Save config
                                    Config.Save();
                                    break;
                                }
                            }
                        }

                        deviceIndex++;
                    }

                    if (foundNewDevice && configTestMode.Value)
                    {
                        Instance.Log.LogInfo("New device detected - rumbling to confirm...");
                    }
                }
            }
            catch (Exception e)
            {
                if (configVerboseLogging.Value)
                {
                    Instance.Log.LogError($"Device check failed: {e.Message}");
                }
            }
        }

        // ===== WEAPON SHOT PATCH =====
        // Patch Weapon.Shot() - this method is ONLY called for local player!
        // Remote players use RemoteShot() instead, so no filtering needed.
        [HarmonyPatch]
        class Patch_Weapon_Shot
        {
            static System.Reflection.MethodBase TargetMethod()
            {
                var weaponType = AccessTools.TypeByName("SumalabVR.Weapons.Weapon");
                if (weaponType == null)
                {
                    Instance.Log.LogError("Could not find Weapon type for patching");
                    return null;
                }

                var method = AccessTools.Method(weaponType, "Shot");
                if (method == null)
                {
                    Instance.Log.LogError("Could not find Weapon.Shot method for patching");
                    return null;
                }

                Instance.Log.LogInfo($"Found Weapon.Shot method for patching: {method}");
                return method;
            }

            static void Postfix(object __instance)
            {
                try
                {
                    if (configVerboseLogging.Value)
                        Instance?.Log.LogInfo("=== Weapon.Shot() CALLED (LOCAL PLAYER) ===");

                    // Phase 2b: Remove forceTubeInitialized check - hot-plug handles device availability
                    if (Instance == null)
                        return;

                    // Get weapon type
                    WeaponType weaponType = GetWeaponType(__instance);

                    if (configVerboseLogging.Value)
                        Instance.Log.LogInfo($"  Weapon type: {weaponType}");

                    // Detect which hand is holding the weapon
                    bool isRightHand = GetWeaponHand(__instance);

                    // Get haptic parameters
                    var (kick, rumble, duration) = GetWeaponHaptics(weaponType);
                    var channels = GetChannelsForWeapon(weaponType, isRightHand);

                    if (configVerboseLogging.Value)
                    {
                        Instance.Log.LogInfo($"  Haptic params: kick={kick}, rumble={rumble}, duration={duration}");
                        Instance.Log.LogInfo($"  Channels: [{string.Join(",", channels)}]");
                    }

                    // Trigger ForceTube on all assigned channels
                    foreach (int channel in channels)
                    {
                        if (configVerboseLogging.Value)
                            Instance.Log.LogInfo($"  Calling ShotChannel({kick}, {rumble}, {duration}, {channel})");

                        ShotChannel(kick, rumble, duration, channel);
                    }

                    if (configVerboseLogging.Value)
                        Instance.Log.LogInfo($"  SUCCESS: Triggered ForceTube for {weaponType}");

                    // Periodically check for hot-plugged devices
                    Instance?.CheckForNewDevices();
                }
                catch (Exception e)
                {
                    if (configVerboseLogging.Value)
                    {
                        Instance.Log.LogError($"ForceTube shot failed: {e.Message}");
                        Instance.Log.LogError($"  Stack trace: {e.StackTrace}");
                    }
                }
            }

            public enum WeaponType
            {
                Pistol,
                Rifle,
                Shotgun,
                Other
            }

            static WeaponType GetWeaponType(object fireArm)
            {
                try
                {
                    string weaponName = ((Component)fireArm).gameObject.name.ToLower();

                    // Log actual weapon name for debugging
                    if (configVerboseLogging.Value)
                    {
                        Instance.Log.LogInfo($"Weapon detected: '{weaponName}'");
                    }

                    // Check for pistols
                    if (weaponName.Contains("pistol") || weaponName.Contains("handgun") ||
                        weaponName.Contains("glock") || weaponName.Contains("1911") ||
                        weaponName.Contains("deagle") || weaponName.Contains("revolver") ||
                        weaponName.Contains("beretta") || weaponName.Contains("sig"))
                        return WeaponType.Pistol;

                    // Check for shotguns
                    if (weaponName.Contains("shotgun") || weaponName.Contains("pump") ||
                        weaponName.Contains("spas") || weaponName.Contains("benelli"))
                        return WeaponType.Shotgun;

                    // Check for rifles (expanded patterns)
                    if (weaponName.Contains("rifle") || weaponName.Contains("ak") ||
                        weaponName.Contains("m4") || weaponName.Contains("ar") ||
                        weaponName.Contains("m16") || weaponName.Contains("scar") ||
                        weaponName.Contains("famas") || weaponName.Contains("aug") ||
                        weaponName.Contains("mp5") || weaponName.Contains("ump") ||
                        weaponName.Contains("smg") || weaponName.Contains("uzi"))
                        return WeaponType.Rifle;

                    // Log unknown weapons as rifle (default)
                    if (configVerboseLogging.Value)
                    {
                        Instance.Log.LogWarning($"Unknown weapon type '{weaponName}', defaulting to Rifle");
                    }
                    return WeaponType.Rifle;
                }
                catch
                {
                    return WeaponType.Rifle;
                }
            }

            public static (byte kick, byte rumble, float duration) GetWeaponHaptics(WeaponType weaponType)
            {
                switch (weaponType)
                {
                    case WeaponType.Pistol:
                        return (200, 150, 0.05f);  // Match Onward values
                    case WeaponType.Shotgun:
                        return (255, 200, 0.10f);  // Maximum power
                    case WeaponType.Rifle:
                    default:
                        return (200, 150, 0.05f);  // Moderate
                }
            }

            public static List<int> GetChannelsForWeapon(WeaponType weaponType, bool isRightHand)
            {
                // For Crisis VRigade 2:
                // - Pistols: trigger channel 4 (right hand) or 5 (left hand) based on hand detection
                // - Rifles/Shotguns: trigger channel 2 and 3 (rifleButt, rifleBolt) regardless of hand
                //
                // Note: Devices are configured to listen to these channels via AddToChannel
                // So a ProVolver configured for channels "4,5,2,3" will trigger on both pistol AND rifle shots

                List<int> channels = new List<int>();

                switch (weaponType)
                {
                    case WeaponType.Pistol:
                        // Hand-specific channel for dual-wielding support
                        if (isRightHand)
                            channels.Add(4);  // pistol_1 - right hand
                        else
                            channels.Add(5);  // pistol_2 - left hand
                        break;

                    case WeaponType.Rifle:
                    case WeaponType.Shotgun:
                    default:
                        channels.Add(2);  // rifleButt
                        channels.Add(3);  // rifleBolt
                        break;
                }

                return channels;
            }

            /// <summary>
            /// Detects which hand is holding the weapon using AccessTools
            /// </summary>
            /// <param name="weaponInstance">The weapon object (__instance from Harmony)</param>
            /// <returns>true if right hand, false if left hand, defaults to true on failure</returns>
            public static bool GetWeaponHand(object weaponInstance)
            {
                bool isRightHand = true; // Default to right hand if detection fails

                try
                {
                    // Work WITH Unity's architecture: use Transform/GameObject hierarchy
                    // All Unity objects (including IL2CPP) are MonoBehaviours with transform
                    var monoBehaviour = weaponInstance as MonoBehaviour;
                    if (monoBehaviour == null)
                    {
                        if (configVerboseLogging.Value)
                            Instance?.Log.LogWarning("  Hand detection: Weapon is not a MonoBehaviour");
                        return isRightHand;
                    }

                    // Check GameObject hierarchy for hand indicators
                    Transform current = monoBehaviour.transform;
                    for (int i = 0; i < 10 && current != null; i++)  // Search up to 10 parents
                    {
                        string name = current.name.ToLower();

                        if (configVerboseLogging.Value)
                            Instance?.Log.LogInfo($"    Checking parent {i}: '{current.name}'");

                        // Look for common VR hand naming patterns
                        if (name.Contains("left") || name.Contains("l_hand") || name.Contains("hand_l"))
                        {
                            isRightHand = false;
                            if (configVerboseLogging.Value)
                                Instance?.Log.LogInfo($"  Hand detected via GameObject: LEFT (found '{current.name}')");
                            return isRightHand;
                        }
                        else if (name.Contains("right") || name.Contains("r_hand") || name.Contains("hand_r"))
                        {
                            isRightHand = true;
                            if (configVerboseLogging.Value)
                                Instance?.Log.LogInfo($"  Hand detected via GameObject: RIGHT (found '{current.name}')");
                            return isRightHand;
                        }

                        current = current.parent;
                    }

                    // Fallback: Use world position (left side = left hand)
                    Vector3 weaponPos = monoBehaviour.transform.position;
                    if (configVerboseLogging.Value)
                        Instance?.Log.LogInfo($"  No hand name found, using position: x={weaponPos.x:F2}");

                    // Assume player is at origin - weapon on left side (negative X) = left hand
                    isRightHand = weaponPos.x >= 0;

                    if (configVerboseLogging.Value)
                        Instance?.Log.LogInfo($"  Hand detected via position: {(isRightHand ? "RIGHT" : "LEFT")}");
                }
                catch (Exception ex)
                {
                    if (configVerboseLogging.Value)
                        Instance?.Log.LogWarning($"  Hand detection failed: {ex.Message}");
                    // Return default (right hand)
                }

                return isRightHand;
            }
        }
    }
}
