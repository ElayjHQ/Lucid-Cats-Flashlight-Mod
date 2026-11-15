using UnityEngine;

namespace LucidCatsFlashlight;

/// <summary>
/// Local battery simulation for the local player's flashlight: drains while
/// lit, recharges while off. The level lives in a static field so it survives
/// scene changes and rig rebuilds. Remote players never simulate - they
/// receive the level byte through the network messages and just render it.
/// </summary>
public static class FlashlightBattery
{
    private static float _level = 1f;
    private static float _savedLevel = 1f;

    /// <summary>0 = dead, 1 = full.</summary>
    public static float Level => _level;

    /// <summary>True while low lucidity has locked the flashlight off.</summary>
    public static bool Suppressed { get; private set; }

    /// <summary>Whether the local player's flashlight is currently lit (drives the HUD).</summary>
    public static bool LocalOn { get; private set; }

    public static bool Enabled =>
        FlashlightPlugin.Battery != null && FlashlightPlugin.Battery.Value;

    /// <summary>A player can turn the light on whenever any charge remains.</summary>
    public static bool CanTurnOn => !Enabled || (!Suppressed && _level > 0.001f);

    /// <summary>
    /// Drains while on, recharges while off, honours the lucidity lock, and is
    /// called by the local rig each frame with the game's own dream state.
    /// </summary>
    public static void Tick(float deltaTime, bool on, bool dreaming, float lucidityNorm)
    {
        LocalOn = on;

        if (!Enabled)
        {
            Release();
            _level = 1f;
            return;
        }

        UpdateLucidLock(dreaming, lucidityNorm);

        if (Suppressed)
        {
            _level = 0f; // pinned dead until the player wakes up
            return;
        }

        _level = on
            ? Mathf.MoveTowards(_level, 0f, deltaTime / Mathf.Max(1f, FlashlightPlugin.BatteryLife.Value))
            : Mathf.MoveTowards(_level, 1f, deltaTime / Mathf.Max(1f, FlashlightPlugin.RechargeTime.Value));
    }

    /// <summary>Wire format for the battery level: 0 (dead) to 255 (full).</summary>
    public static byte ToByte() => (byte)Mathf.RoundToInt(Mathf.Clamp01(_level) * 255f);

    public static void SetFromByte(byte b) => _level = b / 255f;

    /// <summary>Session ended: drop the on-state (the charge level itself is kept).</summary>
    public static void ForgetSession() => LocalOn = false;

    /// <summary>
    /// The lucidity lock: while dreaming and lucidity is at/below the threshold,
    /// the flashlight is permanently dead until the player wakes up (or lucidity
    /// gets restored above the threshold, e.g. by a pickup).
    /// </summary>
    private static void UpdateLucidLock(bool dreaming, float lucidityNorm)
    {
        float threshold = FlashlightPlugin.LucidLock.Value;
        if (threshold <= 0f)
        {
            Release(); // feature disabled: clear any stale lock
            return;
        }

        if (!Suppressed)
        {
            if (dreaming && lucidityNorm <= threshold)
            {
                _savedLevel = _level;
                _level = 0f;
                Suppressed = true;
                FlashlightPlugin.Log.LogInfo(
                    $"Lucid low ({lucidityNorm:P0}); flashlight locked until the player wakes up.");
            }
        }
        else if (!dreaming || lucidityNorm > threshold)
        {
            Release();
        }
    }

    private static void Release()
    {
        if (!Suppressed)
        {
            return;
        }

        Suppressed = false;
        _level = _savedLevel;
        FlashlightPlugin.Log.LogInfo("Player woke up; flashlight unlocked.");
    }

    /// <summary>
    /// Overall brightness multiplier for a battery level: dims below the low
    /// threshold (down to 40%) and flickers hard when nearly dead. Pure level
    /// math - used for local simulation AND for rendering networked levels.
    /// </summary>
    public static float Brightness(float level, float lowThreshold)
    {
        float dim = level >= lowThreshold
            ? 1f
            : Mathf.Lerp(0.4f, 1f, level / Mathf.Max(0.01f, lowThreshold));
        return dim * Flicker(level);
    }

    /// <summary>Occasional dropouts below 10%, worse the closer to dead.</summary>
    private static float Flicker(float level)
    {
        if (level >= 0.10f)
        {
            return 1f;
        }

        float severity = 1f - level / 0.10f; // 0 at 10% charge, 1 at dead
        float t = Time.time * 13f;
        float n = Mathf.PerlinNoise(t, 7.31f) * 0.65f + Mathf.PerlinNoise(t * 2.7f, 3.7f) * 0.35f;
        return n < severity * 0.6f ? 0.1f : 1f;
    }
}
