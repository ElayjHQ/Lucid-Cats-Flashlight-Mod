using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using UnityEngine;

namespace LucidCatsFlashlight;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public class FlashlightPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "lucidcats.flashlight";
    public const string PluginName = "Lucid Cats Flashlight";
    public const string PluginVersion = "1.2.0";

    internal static ManualLogSource Log;

    private static ConfigFile _configFile;

    // Light appearance
    internal static ConfigEntry<bool> BeamEnabled;
    internal static ConfigEntry<float> Intensity;
    internal static ConfigEntry<float> Range;
    internal static ConfigEntry<float> Angle;
    internal static ConfigEntry<bool> CastShadows;

    // Volumetric cone appearance
    internal static ConfigEntry<float> BeamIntensity;

    // Held model
    internal static ConfigEntry<bool> ShowModel;
    internal static ConfigEntry<float> ModelScale;
    internal static ConfigEntry<bool> Sound;
    internal static ConfigEntry<float> Volume;

    // Floating effects
    internal static ConfigEntry<bool> Effects;
    internal static ConfigEntry<float> GlowIntensity;
    internal static ConfigEntry<float> RemoteRight;

    // Controls
    internal static ConfigEntry<string> ToggleKey;

    // Battery system
    internal static ConfigEntry<bool> Battery;
    internal static ConfigEntry<float> BatteryLife;
    internal static ConfigEntry<float> RechargeTime;
    internal static ConfigEntry<float> LowThreshold;
    internal static ConfigEntry<bool> ShowBatteryHud;
    internal static ConfigEntry<float> HudScale;
    internal static ConfigEntry<bool> LowStatic;
    internal static ConfigEntry<float> LucidLock;

    // Your held flashlight, in camera space
    internal static ConfigEntry<float> LocalRight;
    internal static ConfigEntry<float> LocalDown;
    internal static ConfigEntry<float> LocalForward;
    internal static ConfigEntry<float> LocalYaw;

    // Remote (other players') beam placement, relative to their cat model
    internal static ConfigEntry<float> RemoteForward;   // extra forward offset from paw anchor
    internal static ConfigEntry<float> RemoteYaw;      // degrees, in case the model faces -Z
    internal static ConfigEntry<float> RemotePitch;    // degrees, negative = down

    private void Awake()
    {
        Log = Logger;
        _configFile = Config;

        BeamEnabled = Config.Bind("Flashlight", "Beam", true,
            "Show the visible volumetric beam cone. Set false for a plain spotlight look.");
        Intensity = Config.Bind("Flashlight", "Intensity", 10f,
            "Spotlight intensity (URP units). Raise if too dim, lower if too bright.");
        Range = Config.Bind("Flashlight", "Range", 16f,
            "How far the light reaches, in meters.");
        Angle = Config.Bind("Flashlight", "Angle", 38f,
            "Beam cone width in degrees (10-120).");
        CastShadows = Config.Bind("Flashlight", "Shadows", true,
            "Cast shadows from the flashlight (slightly more expensive).");
        BeamIntensity = Config.Bind("Flashlight", "BeamIntensity", 0.35f,
            "Volumetric cone opacity (0 = invisible cone, 1 = very solid).");
        Effects = Config.Bind("Flashlight", "Effects", true,
            "Floating-flashlight effects: gentle hover, pulsing glow, drifting motes.");
        GlowIntensity = Config.Bind("Flashlight", "GlowIntensity", 1.2f,
            "Strength of the pulsing glow light around the flashlight (0 = off).");
        Sound = Config.Bind("Flashlight", "Sound", true,
            "Play a click sound when the flashlight toggles.");
        Volume = Config.Bind("Flashlight", "Volume", 0.45f,
            "Toggle click volume (0-1).");

        ShowModel = Config.Bind("Flashlight", "ShowModel", true,
            "Show the 3D flashlight floating beside you (and beside other players).");
        ModelScale = Config.Bind("Flashlight", "ModelScale", 1f,
            "Flashlight model size multiplier.");
        RemoteRight = Config.Bind("Placement", "RemoteRight", 0f,
            "Other players' flashlight: sideways nudge from their right hand, in meters.");
        RemoteForward = Config.Bind("Placement", "RemoteForward", 0.05f,
            "Other players' flashlight: forward nudge from their right hand, in meters.");

        LocalRight = Config.Bind("Placement", "LocalRight", 0.28f,
            "Floating flashlight offset to the right of your camera, in meters.");
        LocalDown = Config.Bind("Placement", "LocalDown", 0.16f,
            "Floating flashlight offset below your camera, in meters (higher = lower in view).");
        LocalForward = Config.Bind("Placement", "LocalForward", 0.42f,
            "Floating flashlight offset in front of your camera, in meters.");
        LocalYaw = Config.Bind("Placement", "LocalYaw", -6f,
            "Floating flashlight aim in degrees (negative = toward the screen centre).");
        ToggleKey = Config.Bind("Controls", "ToggleKey", "F",
            "Key or mouse button that toggles the flashlight (e.g. F, G, LeftShift, Space, MiddleMouse, Mouse4). Hold the current key for 2 seconds in-game to rebind without editing this file.");

        Battery = Config.Bind("Battery", "Enabled", true,
            "Battery system: the flashlight drains while lit and recharges while off.");
        BatteryLife = Config.Bind("Battery", "LifeSeconds", 40f,
            "Seconds of continuous light on a full battery.");
        RechargeTime = Config.Bind("Battery", "RechargeSeconds", 60f,
            "Seconds to fully recharge while the flashlight is off.");
        LowThreshold = Config.Bind("Battery", "LowThreshold", 0.25f,
            "Charge level where the light starts dimming (0-1).");
        ShowBatteryHud = Config.Bind("Battery", "ShowHud", true,
            "Show the small battery bar at the bottom of the screen.");
        HudScale = Config.Bind("Battery", "HudScale", 1f,
            "Battery HUD bar size multiplier (1 = default 150x20 px).");
        LowStatic = Config.Bind("Battery", "LowStatic", true,
            "Quiet static crackle while a low battery drains (respects the master Sound toggle).");
        LucidLock = Config.Bind("Battery", "LucidLock", 0.30f,
            "Lock the flashlight off while dreaming and lucidity is at or below this level (0-1, 0 = off). Unlocks when the player wakes up.");

        RemoteYaw = Config.Bind("Placement", "RemoteYaw", 0f,
            "Other players' beam yaw offset in degrees (use 180 if the beam points backwards).");
        RemotePitch = Config.Bind("Placement", "RemotePitch", -5f,
            "Other players' beam pitch offset in degrees (negative = slightly down).");

        var loader = new GameObject("LCF_Loader");
        Object.DontDestroyOnLoad(loader);
        loader.AddComponent<FlashlightLoader>();

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded. Press {ToggleKey.Value} in-game to toggle your flashlight.");
    }

    internal static void SaveConfig()
    {
        try
        {
            if (_configFile != null)
            {
                _configFile.Save();
            }
        }
        catch (System.Exception e)
        {
            Log.LogWarning($"Could not save config: {e.Message}");
        }
    }
}
