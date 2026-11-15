using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace LucidCatsFlashlight;

/// <summary>
/// Loads and caches the flashlight click sounds shipped next to the mod DLL
/// (BepInEx/plugins/LucidCatsFlashlight/flashlight_open.mp3 + flashlight_close.mp3).
/// </summary>
public static class FlashlightSound
{
    public const string OpenFileName = "flashlight_open.mp3";
    public const string CloseFileName = "flashlight_close.mp3";

    private static AudioClip _openClip;
    private static AudioClip _closeClip;
    private static bool _loadFailed;

    public static bool TryGetClips(out AudioClip open, out AudioClip close)
    {
        open = _openClip != null ? _openClip : null;
        close = _closeClip != null ? _closeClip : null;
        return open != null || close != null;
    }

    /// <summary>Kicks off an async load from disk on the given host's coroutine runner.</summary>
    public static void LoadAsync(MonoBehaviour host)
    {
        if ((host == null) || (_openClip != null) || (_closeClip != null) || _loadFailed)
        {
            return;
        }

        try
        {
            string openPath = FindSoundFile(OpenFileName);
            string closePath = FindSoundFile(CloseFileName);
            if (openPath == null && closePath == null)
            {
                FlashlightPlugin.Log.LogWarning($"Sound files '{OpenFileName}' / '{CloseFileName}' not found next to the mod DLL; no toggle sound.");
                _loadFailed = true;
                return;
            }

            if (openPath != null)
            {
                host.StartCoroutine(LoadRoutine(new Uri(openPath).AbsoluteUri, AudioType.MPEG, clip => _openClip = clip, OpenFileName));
            }

            if (closePath != null)
            {
                host.StartCoroutine(LoadRoutine(new Uri(closePath).AbsoluteUri, AudioType.MPEG, clip => _closeClip = clip, CloseFileName));
            }
        }
        catch (Exception e)
        {
            FlashlightPlugin.Log.LogWarning($"Sound load failed to start: {e.Message}");
            _loadFailed = true;
        }
    }

    private static string FindSoundFile(string fileName)
    {
        string assemblyDir = null;
        try
        {
            assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }
        catch
        {
            // Assembly.Location can be empty for some loaders; fall back to BepInEx paths.
        }

        string pluginDir = string.IsNullOrEmpty(assemblyDir) ? BepInEx.Paths.PluginPath : assemblyDir;

        string[] candidates =
        {
            Path.Combine(pluginDir, "LucidCatsFlashlight", fileName),
            Path.Combine(pluginDir, fileName),
            Path.Combine(BepInEx.Paths.PluginPath, "LucidCatsFlashlight", fileName),
            Path.Combine(BepInEx.Paths.PluginPath, fileName),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerator LoadRoutine(string uri, AudioType type, Action<AudioClip> assign, string label)
    {
        using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, type))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                FlashlightPlugin.Log.LogWarning($"Sound load error ({label}): {request.error}");
                yield break;
            }

            var clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip != null)
            {
                clip.name = label;
                assign(clip);
                FlashlightPlugin.Log.LogInfo($"Flashlight sound loaded: {label}");
            }
        }
    }
}
