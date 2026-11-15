using System;
using System.Collections;
using Game.Player;
using UnityEngine;
using VLB;

namespace LucidCatsFlashlight;

/// <summary>
/// A flashlight rig attached to one player's body. Renders locally on every client;
/// the on/off state is kept in sync by FlashlightLoader's network messages.
/// </summary>
public class FlashlightRig : MonoBehaviour
{
    public PlayerManager Player { get; private set; }
    public ulong OwnerId { get; private set; }
    public bool IsLocal { get; private set; }
    public bool HandAttached { get; set; }

    private Light _spot;
    private VolumetricLightBeamSD _beam;
    private bool _beamBroken;
    private FlashlightViewModel _viewModel;
    private AudioSource _sound;
    private bool _lastOn;
    private bool _soundInitialized;
    private Renderer[] _ownShadowRenderers;
    private UnityEngine.Rendering.ShadowCastingMode[] _originalShadowModes;

    // Battery: local players simulate; remotes receive the level by network.
    private float _baseIntensity;
    private float _baseBeamIntensity;
    private float _batteryLevel = 1f;
    private AudioSource _static;

    public void Init(PlayerManager player)
    {
        Player = player;
        OwnerId = player.OwnerClientId;
        IsLocal = player.IsOwner;

        // The local cat's own model is invisible to its camera but still casts
        // shadows - which turns into a giant dark dome in front of the player
        // when our light sits at the head. Suppress its shadow casting only
        // while OUR flashlight is on, restore when off.
        if (IsLocal && player.PlayerModel != null)
        {
            _ownShadowRenderers = player.PlayerModel.GetComponentsInChildren<Renderer>(true);
            _originalShadowModes = new UnityEngine.Rendering.ShadowCastingMode[_ownShadowRenderers.Length];
            for (int i = 0; i < _ownShadowRenderers.Length; i++)
            {
                _originalShadowModes[i] = _ownShadowRenderers[i].shadowCastingMode;
            }
        }

        // Held flashlight model (procedural, PSX-style). Built first: it exposes
        // the lens tip where the light + beam get mounted.
        Transform opticsParent = transform;
        Vector3 opticsLocalPos = Vector3.zero;
        if (FlashlightPlugin.ShowModel.Value)
        {
            try
            {
                // Local: extends back from the rig origin so the lens sits ahead.
                // Remote: the body's centre sits on the hand, so the lens points forward from the palm.
                Vector3 modelPos = IsLocal
                    ? new Vector3(0f, 0f, 0.02f)
                    : new Vector3(0f, 0f, -0.026f);
                _viewModel = FlashlightViewModel.Build(
                    transform, modelPos, FlashlightPlugin.ModelScale.Value, IsLocal, player);
                _viewModel.gameObject.SetActive(false); // starts off

                opticsParent = _viewModel.LensTip.parent;
                opticsLocalPos = _viewModel.LensTip.localPosition;
            }
            catch (Exception e)
            {
                FlashlightPlugin.Log.LogWarning($"Flashlight model could not be created ({e.Message}).");
                _viewModel = null;
            }
        }

        // Optics node: the light and volumetric cone live exactly at the lens
        // face, so the beam starts at the lens instead of behind the model.
        var optics = new GameObject("LCF_Optics");
        optics.transform.SetParent(opticsParent, false);
        optics.transform.localPosition = opticsLocalPos;
        optics.transform.localRotation = Quaternion.identity;

        _spot = optics.AddComponent<Light>();
        _spot.type = LightType.Spot;
        _spot.color = new Color(1f, 0.93f, 0.8f); // warm white
        _spot.intensity = FlashlightPlugin.Intensity.Value;
        _baseIntensity = _spot.intensity;
        _spot.range = FlashlightPlugin.Range.Value;
        _spot.spotAngle = Mathf.Clamp(FlashlightPlugin.Angle.Value, 10f, 120f);
        _spot.innerSpotAngle = _spot.spotAngle * 0.65f;
        _spot.shadows = FlashlightPlugin.CastShadows.Value ? LightShadows.Soft : LightShadows.None;
        _spot.shadowStrength = 0.85f;
        _spot.enabled = false; // starts off

        // Toggle click sound on the rig root (which never gets deactivated by
        // on/off): if it lived under the model, switching OFF would hide the
        // AudioSource before the close click could play. Spatialized from the
        // flashlight position: friends hear it from your cat, you hear it from
        // beside you.
        _sound = gameObject.AddComponent<AudioSource>();
        _sound.playOnAwake = false;
        _sound.spatialBlend = 1f;
        _sound.rolloffMode = AudioRolloffMode.Linear;
        _sound.minDistance = 1.5f;
        _sound.maxDistance = 20f;
        _sound.volume = FlashlightPlugin.Volume.Value;
        if (FlashlightPlugin.Sound.Value)
        {
            FlashlightSound.LoadAsync(this);
        }

        // Low-battery static: a quiet looping crackle while the light drains.
        // Spatialized like the clicks, so friends hear your torch sputtering too.
        if (FlashlightPlugin.Sound.Value && FlashlightPlugin.LowStatic.Value)
        {
            var staticClip = FlashlightStatic.GetClip();
            if (staticClip != null)
            {
                _static = gameObject.AddComponent<AudioSource>();
                _static.playOnAwake = false;
                _static.loop = true;
                _static.clip = staticClip;
                _static.spatialBlend = 1f;
                _static.rolloffMode = AudioRolloffMode.Linear;
                _static.minDistance = 1.5f;
                _static.maxDistance = 12f;
                _static.volume = 0f;
            }
        }

        // Volumetric cone using the game's own VLB asset. Best-effort: if it fails,
        // the plain spotlight still works. Tuned subtle so the camera doesn't sit
        // inside a giant white dome.
        if (FlashlightPlugin.BeamEnabled.Value)
        {
            try
            {
                _beam = optics.AddComponent<VolumetricLightBeamSD>();
                _beam.trackChangesDuringPlaytime = true; // follow light property changes
                _beam.coneRadiusStart = 0.05f;           // narrow throat, less near-camera shell
                _beam.intensityFromLight = false;        // decouple cone opacity from light power
                _beam.intensityGlobal = Mathf.Clamp01(FlashlightPlugin.BeamIntensity.Value);
                _baseBeamIntensity = _beam.intensityGlobal;
                _beam.intensityInside = 1f;
                _beam.intensityOutside = 0.4f;
                _beam.glareFrontal = 0f;
                _beam.glareBehind = 0f;
                _beam.fallOffEndFromLight = false;
                _beam.fallOffEnd = FlashlightPlugin.Range.Value * 0.7f;
                _beam.enabled = false; // starts off
            }
            catch (Exception e)
            {
                FlashlightPlugin.Log.LogWarning($"VLB beam could not be created ({e.Message}); light-only mode.");
                _beam = null;
            }
        }
    }

    public void SetOn(bool on)
    {
        // Apply visual states FIRST: the sound below must play while the model /
        // optics GameObject is active, or Unity silently ignores PlayOneShot
        // on an inactive AudioSource.
        if (_spot != null)
        {
            _spot.enabled = on;
        }

        // While our light is on, stop the local cat's own (invisible) model from
        // casting shadows - otherwise its head paints a huge dark dome into view.
        if (_ownShadowRenderers != null)
        {
            for (int i = 0; i < _ownShadowRenderers.Length; i++)
            {
                var r = _ownShadowRenderers[i];
                if (r == null)
                {
                    continue;
                }

                r.shadowCastingMode = on
                    ? UnityEngine.Rendering.ShadowCastingMode.Off
                    : _originalShadowModes[i];
            }
        }

        if (_beam != null && !_beamBroken)
        {
            _beam.enabled = on;
            if (on)
            {
                StartCoroutine(CheckBeamGeometry());
            }
        }

        // The model's visibility is animated by the viewmodel: on a real state
        // change it spins into place or dissolves away. The first application
        // per rig (spawn / late-join sync) applies instantly and silently.
        if (!_soundInitialized)
        {
            _soundInitialized = true;
            _lastOn = on;
            if (_viewModel != null)
            {
                _viewModel.ApplyInstant(on);
            }
        }
        else if (on != _lastOn)
        {
            _lastOn = on;
            if (_viewModel != null)
            {
                _viewModel.PlayToggle(on); // activates the model synchronously when appearing
            }

            if (FlashlightPlugin.Sound.Value && _sound != null && FlashlightSound.TryGetClips(out var openClip, out var closeClip))
            {
                var clip = on ? openClip : closeClip;
                if (clip == null)
                {
                    clip = openClip != null ? openClip : closeClip; // fall back to whichever exists
                }

                if (clip != null)
                {
                    _sound.volume = FlashlightPlugin.Volume.Value;
                    _sound.PlayOneShot(clip);
                }
            }
        }
    }

    /// <summary>True while this player is inside a dream (game's own sleep system).</summary>
    private bool Dreaming()
    {
        return Player != null && Player.SleepManager != null && Player.SleepManager.IsDreaming;
    }

    /// <summary>The game's lucidity, normalized to 0-1 (1 when unavailable).</summary>
    private float LucidityNorm()
    {
        var lucidity = Player != null ? Player.LucidityManager : null;
        return lucidity != null ? Mathf.Clamp01(lucidity.currentLucidity.Value / 100f) : 1f;
    }

    /// <summary>Networked battery level for a remote rig (0-255 byte from its owner).</summary>
    public void SetBattery(byte level)
    {
        _batteryLevel = level / 255f;
    }

    /// <summary>Dead-battery feedback: a quiet click, but no light.</summary>
    public void PlayDeadClick()
    {
        if (!FlashlightPlugin.Sound.Value || _sound == null ||
            !FlashlightSound.TryGetClips(out var openClip, out var closeClip))
        {
            return;
        }

        var clip = closeClip != null ? closeClip : openClip;
        if (clip != null)
        {
            _sound.volume = FlashlightPlugin.Volume.Value * 0.5f;
            _sound.PlayOneShot(clip);
        }
    }

    private void Update()
    {
        // Local players tick the battery simulation; remotes render the level
        // that arrived through the network messages.
        if (IsLocal)
        {
            FlashlightBattery.Tick(Time.deltaTime, _lastOn, Dreaming(), LucidityNorm());
            _batteryLevel = FlashlightBattery.Level;
        }

        // Battery-driven brightness: the light dims and flickers as it dies.
        float brightness = FlashlightBattery.Brightness(
            _batteryLevel, FlashlightPlugin.LowThreshold.Value);
        if (_spot != null && _spot.enabled)
        {
            _spot.intensity = _baseIntensity * brightness;
        }

        if (_beam != null && !_beamBroken && _beam.enabled)
        {
            _beam.intensityGlobal = Mathf.Clamp01(_baseBeamIntensity * Mathf.Max(brightness, 0.04f));
        }

        if (_viewModel != null)
        {
            _viewModel.ApplyBattery(brightness);
        }

        UpdateStatic();
    }

    /// <summary>
    /// Static crackle while a low battery drains: only while the light is ON
    /// (dying-torch sputter), louder the emptier it gets, silent once it dies
    /// or is switched off. Remote rigs crackle too, via the synced level.
    /// </summary>
    private void UpdateStatic()
    {
        if (_static == null)
        {
            return;
        }

        float lowThreshold = FlashlightPlugin.LowThreshold.Value;
        bool low = _lastOn && FlashlightBattery.Enabled && _batteryLevel < lowThreshold;
        if (low)
        {
            if (!_static.isPlaying)
            {
                _static.Play();
            }

            // Silence just under the threshold, full crackle at dead.
            float severity = 1f - Mathf.Clamp01(_batteryLevel / Mathf.Max(0.01f, lowThreshold));
            _static.volume = FlashlightPlugin.Volume.Value * 0.5f * (0.3f + 0.7f * severity);
        }
        else if (_static.isPlaying)
        {
            _static.Stop();
        }
    }

    // VLB generates its cone mesh from Start(); if the shader/config asset is missing
    // in this build we fall back to light-only mode instead of erroring forever.
    private IEnumerator CheckBeamGeometry()
    {
        yield return new WaitForSecondsRealtime(2f);
        if (!_beamBroken && _beam != null && _beam.enabled && !_beam.hasGeometry)
        {
            _beamBroken = true;
            FlashlightPlugin.Log.LogWarning("VLB beam geometry never generated; switching to light-only mode.");
            Destroy(_beam);
            _beam = null;
        }
    }
}
