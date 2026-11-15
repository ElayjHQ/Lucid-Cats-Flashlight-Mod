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

    private Light _spot;
    private VolumetricLightBeamSD _beam;
    private bool _beamBroken;
    private FlashlightViewModel _viewModel;
    private Renderer[] _ownShadowRenderers;
    private UnityEngine.Rendering.ShadowCastingMode[] _originalShadowModes;

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
                // Remote: sits at the paw with the body trailing toward the cat.
                Vector3 modelPos = IsLocal
                    ? new Vector3(0f, 0f, 0.02f)
                    : new Vector3(0f, -0.02f, -0.08f);
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
        _spot.range = FlashlightPlugin.Range.Value;
        _spot.spotAngle = Mathf.Clamp(FlashlightPlugin.Angle.Value, 10f, 120f);
        _spot.innerSpotAngle = _spot.spotAngle * 0.65f;
        _spot.shadows = FlashlightPlugin.CastShadows.Value ? LightShadows.Soft : LightShadows.None;
        _spot.shadowStrength = 0.85f;
        _spot.enabled = false; // starts off

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

        if (_viewModel != null)
        {
            _viewModel.gameObject.SetActive(on);
        }

        if (_beam != null && !_beamBroken)
        {
            _beam.enabled = on;
            if (on)
            {
                StartCoroutine(CheckBeamGeometry());
            }
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
