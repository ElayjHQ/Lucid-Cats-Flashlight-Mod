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
    public const string PluginVersion = "1.1.0";

    internal static ManualLogSource Log;

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
    internal static ConfigEntry<float> RemoteRight;

    // Local (own) beam placement, in camera space
    internal static ConfigEntry<float> LocalRight;
    internal static ConfigEntry<float> LocalDown;
    internal static ConfigEntry<float> LocalForward;

    // Remote (other players') beam placement, relative to their cat model
    internal static ConfigEntry<float> RemoteForward;   // extra forward offset from paw anchor
    internal static ConfigEntry<float> RemoteYaw;      // degrees, in case the model faces -Z
    internal static ConfigEntry<float> RemotePitch;    // degrees, negative = down

    private void Awake()
    {
        Log = Logger;

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

        ShowModel = Config.Bind("Flashlight", "ShowModel", true,
            "Show the 3D flashlight held in your paw (and other players' paws).");
        ModelScale = Config.Bind("Flashlight", "ModelScale", 1f,
            "Flashlight model size multiplier.");
        RemoteRight = Config.Bind("Placement", "RemoteRight", 0.12f,
            "Other players' flashlight: extra sideways offset from the paw anchor, in meters.");
        RemoteForward = Config.Bind("Placement", "RemoteForward", 0.05f,
            "Other players' flashlight: extra forward offset from the paw anchor, in meters.");

        LocalRight = Config.Bind("Placement", "LocalRight", 0.35f,
            "Beam origin offset to the right of your camera, in meters (simulates holding it in your paw).");
        LocalDown = Config.Bind("Placement", "LocalDown", 0.12f,
            "Beam origin offset below your camera, in meters.");
        LocalForward = Config.Bind("Placement", "LocalForward", 0.28f,
            "Beam origin offset in front of your camera, in meters.");
        RemoteYaw = Config.Bind("Placement", "RemoteYaw", 0f,
            "Other players' beam yaw offset in degrees (use 180 if the beam points backwards).");
        RemotePitch = Config.Bind("Placement", "RemotePitch", -5f,
            "Other players' beam pitch offset in degrees (negative = slightly down).");

        var loader = new GameObject("LCF_Loader");
        Object.DontDestroyOnLoad(loader);
        loader.AddComponent<FlashlightLoader>();

        Log.LogInfo($"{PluginName} v{PluginVersion} loaded. Press F in-game to toggle your flashlight.");
    }
}
