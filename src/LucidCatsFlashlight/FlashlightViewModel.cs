using System.Collections;
using UnityEngine;

namespace LucidCatsFlashlight;

/// <summary>
/// A low-poly flashlight built from Unity primitives, styled to fit the game's
/// PSX look. Constructed entirely at runtime - no imported assets. Presented as
/// a floating dream artifact: a gentle hover, a pulsing warm glow, motes that
/// fall away beneath it, and a spin-in / dissolve-out toggle animation.
/// </summary>
public class FlashlightViewModel : MonoBehaviour
{
    private Transform _sway;
    private Transform _float;
    private Material _lensMaterial;
    private Light _glow;
    private ParticleSystem _dust;
    private float _phase;

    // Toggle animation state.
    private Vector3 _baseScale;
    private Coroutine _toggleAnim;
    private bool _targetOn;
    private float _animGlow = 1f;

    private bool _isLocal;
    private Game.Player.PlayerManager _player;
    private Transform _cam;

    private float _swayYaw;
    private float _swayPitch;
    private float _lastYaw;
    private float _lastPitch;

    public static FlashlightViewModel Build(Transform parent, Vector3 localPos, float scale, bool isLocal, Game.Player.PlayerManager player)
    {
        var root = new GameObject("LCF_FlashlightModel");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPos;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * Mathf.Clamp(scale, 0.3f, 3f);

        var vm = root.AddComponent<FlashlightViewModel>();
        vm.BuildInternal(isLocal, player);
        return vm;
    }

    private void BuildInternal(bool isLocal, Game.Player.PlayerManager player)
    {
        _isLocal = isLocal;
        _player = player;
        _baseScale = transform.localScale;
        _targetOn = false; // starts hidden

        if (isLocal && Camera.main != null)
        {
            _cam = Camera.main.transform;
            CacheCameraAngles();
        }

        // Sway (camera lag) -> float (idle hover) -> geometry
        _sway = new GameObject("LCF_Sway").transform;
        _sway.SetParent(transform, false);
        _float = new GameObject("LCF_Float").transform;
        _float.SetParent(_sway, false);

        var bodyMat = MakeMaterial(new Color(0.13f, 0.13f, 0.15f), 0.65f, 0.7f);
        var ringMat = MakeMaterial(new Color(0.07f, 0.07f, 0.08f), 0.4f, 0.6f);
        _lensMaterial = MakeMaterial(new Color(2.2f, 1.8f, 1.1f), 0f, 0f, emissive: true);

        // All parts point down +Z of the rig (same direction the light shines).
        Part(_float, bodyMat, radius: 0.032f, length: 0.19f, z: 0.010f);             // body
        Part(_float, ringMat, radius: 0.048f, length: 0.022f, z: 0.062f);            // head ring
        Part(_float, bodyMat, radius: 0.046f, length: 0.075f, z: 0.112f);            // flared head
        Part(_float, _lensMaterial, radius: 0.040f, length: 0.006f, z: 0.151f);      // lens
        Part(_float, ringMat, radius: 0.034f, length: 0.018f, z: -0.093f);           // tail cap

        // Marker at the lens face: the light + volumetric cone mount here, so the
        // beam physically starts at the lens instead of behind the model.
        LensTip = new GameObject("LCF_LensTip").transform;
        LensTip.SetParent(_float, false);
        LensTip.localPosition = new Vector3(0f, 0f, 0.151f);

        if (FlashlightPlugin.Effects.Value)
        {
            BuildEffects(_float);
        }
    }

    /// <summary>Transform at the lens face; the rig mounts its light here.</summary>
    public Transform LensTip { get; private set; }

    // ------------------------------------------------------------ effects

    /// <summary>Pulsing halo light + motes falling beneath: the "dream artifact" look.</summary>
    private void BuildEffects(Transform parent)
    {
        // Pulsing halo: a small warm point light that also brightens the body
        // itself and the surfaces right around it.
        var glowGo = new GameObject("LCF_Glow");
        glowGo.transform.SetParent(parent, false);
        glowGo.transform.localPosition = new Vector3(0f, 0f, 0.10f);
        _glow = glowGo.AddComponent<Light>();
        _glow.type = LightType.Point;
        _glow.color = new Color(1f, 0.85f, 0.6f);
        _glow.intensity = FlashlightPlugin.GlowIntensity.Value;
        _glow.range = 3f;
        _glow.shadows = LightShadows.None;

        // Dream dust: soft motes that drift DOWN from around the flashlight,
        // like levitation residue falling away beneath it.
        var dustGo = new GameObject("LCF_Dust");
        dustGo.transform.SetParent(parent, false);
        var ps = dustGo.AddComponent<ParticleSystem>();
        _dust = ps;

        var main = ps.main;
        main.loop = true;
        main.duration = 4f;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.88f, 0.65f, 0.55f));
        main.maxParticles = 24;
        main.gravityModifier = new ParticleSystem.MinMaxCurve(0.02f); // fall gently downward
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 7f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.16f;

        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.9f, 0.7f), 0f),
                new GradientColorKey(new Color(1f, 0.8f, 0.5f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.5f, 0.25f),
                new GradientAlphaKey(0f, 1f),
            });
        colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1f));

        var psr = dustGo.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psr.material = MakeDustMaterial();
    }

    private static Material MakeDustMaterial()
    {
        var tex = MakeSoftCircle(32);
        Shader shader = null;
        foreach (var name in new[]
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Sprites/Default",
        })
        {
            shader = Shader.Find(name);
            if (shader != null)
            {
                break;
            }
        }

        var m = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
        if (m.HasProperty("_BaseMap"))
        {
            m.SetTexture("_BaseMap", tex);
        }

        if (m.HasProperty("_MainTex"))
        {
            m.SetTexture("_MainTex", tex);
        }

        return m;
    }

    private static Texture2D MakeSoftCircle(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
        };

        var pixels = new Color[size * size];
        float radius = size / 2f - 1f;
        var center = new Vector2(size / 2f - 0.5f, size / 2f - 0.5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center) / radius;
                float a = Mathf.Clamp01(1f - d);
                a *= a; // soft falloff
                pixels[y * size + x] = new Color(1f, 0.9f, 0.7f, a);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return tex;
    }

    // ------------------------------------------------------------ toggle animation

    /// <summary>Called by the rig when the on/off state actually changes.</summary>
    public void PlayToggle(bool on)
    {
        _targetOn = on;
        if (_toggleAnim != null)
        {
            StopCoroutine(_toggleAnim);
        }

        // Coroutines cannot start while the GameObject is inactive, so the model
        // must be activated BEFORE the appear animation begins.
        if (on)
        {
            gameObject.SetActive(true);
        }

        if (_dust != null)
        {
            _dust.Emit(on ? 10 : 14); // small poof of motes at each transition
        }

        _toggleAnim = StartCoroutine(on ? AppearRoutine() : DisappearRoutine());
    }

    /// <summary>Applies the state instantly and silently (spawn / late-join sync).</summary>
    public void ApplyInstant(bool on)
    {
        _targetOn = on;
        if (_toggleAnim != null)
        {
            StopCoroutine(_toggleAnim);
            _toggleAnim = null;
        }

        gameObject.SetActive(on);
        ApplyProgress(on ? 1f : 0f, on);
    }

    private IEnumerator AppearRoutine()
    {
        // Note: the GameObject is already active - PlayToggle activates it before
        // starting this coroutine, since coroutines can't run on inactive objects.
        yield return AnimateRoutine(appearing: true, duration: 0.45f);
    }

    private IEnumerator DisappearRoutine()
    {
        yield return AnimateRoutine(appearing: false, duration: 0.3f);
        gameObject.SetActive(false);
    }

    private IEnumerator AnimateRoutine(bool appearing, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            ApplyProgress(Mathf.Clamp01(t / duration), appearing);
            yield return null;
        }

        ApplyProgress(1f, appearing);
        _toggleAnim = null;
    }

    /// <summary>Scale-only toggle animation: 0 = gone, 1 = settled in place.</summary>
    private void ApplyProgress(float p, bool appearing)
    {
        // Appear: easeOutBack - pops in and briefly overshoots ~10% before settling.
        // Disappear: mirrored easeInBack - swells slightly, then collapses to nothing.
        float e = appearing ? EaseOutBack(p) : 1f - EaseInBack(p);
        _animGlow = Mathf.Clamp01(e);
        transform.localScale = _baseScale * Mathf.Max(e, 0.0001f);
    }

    private const float BackOvershoot = 1.70158f;

    private static float EaseOutBack(float x)
    {
        float c3 = BackOvershoot + 1f;
        float xm1 = x - 1f;
        return 1f + c3 * xm1 * xm1 * xm1 + BackOvershoot * xm1 * xm1;
    }

    private static float EaseInBack(float x)
    {
        float c3 = BackOvershoot + 1f;
        return c3 * x * x * x - BackOvershoot * x * x;
    }

    // ------------------------------------------------------------ per-frame

    private void OnEnable()
    {
        // Avoid a sway spike after re-enabling (stale angle cache).
        if (_cam != null)
        {
            CacheCameraAngles();
        }

        _swayYaw = 0f;
        _swayPitch = 0f;
    }

    private void Update()
    {
        // Self-heal: if a toggle animation was interrupted (spectating, scene
        // change), settle to the intended state.
        if (_toggleAnim == null)
        {
            if (_targetOn && transform.localScale != _baseScale)
            {
                ApplyProgress(1f, appearing: true);
            }
            else if (!_targetOn && gameObject.activeSelf)
            {
                ApplyProgress(0f, appearing: false);
                gameObject.SetActive(false);
            }
        }

        _phase += Time.deltaTime;

        // Idle hover: a slow, gentle float so the flashlight reads as deliberately
        // suspended rather than stuck to the screen.
        _float.localPosition = new Vector3(
            Mathf.Sin(_phase * 0.9f) * 0.008f,
            Mathf.Sin(_phase * 1.35f) * 0.012f,
            0f);
        _float.localRotation = Quaternion.Euler(
            Mathf.Sin(_phase * 0.65f) * 2f,
            Mathf.Sin(_phase * 0.5f) * 2.5f,
            Mathf.Sin(_phase * 0.8f) * 1.5f);

        // Glow pulse (scaled by the toggle animation).
        if (_glow != null)
        {
            _glow.intensity = FlashlightPlugin.GlowIntensity.Value *
                (0.82f + 0.18f * Mathf.Sin(_phase * 2.1f)) * _animGlow;
        }

        // Sway only for the local player: the flashlight lags slightly behind the
        // camera, like it's trailing along beside you.
        if (!_isLocal)
        {
            return;
        }

        if (_cam == null && Camera.main != null)
        {
            _cam = Camera.main.transform;
            CacheCameraAngles();
        }

        if (_cam != null)
        {
            var e = _cam.rotation.eulerAngles;
            float yawDelta = Mathf.DeltaAngle(_lastYaw, e.y);
            float pitchDelta = Mathf.DeltaAngle(_lastPitch, e.x);
            CacheCameraAngles();

            _swayYaw = Mathf.Clamp(_swayYaw - yawDelta * 0.35f, -7f, 7f);
            _swayPitch = Mathf.Clamp(_swayPitch - pitchDelta * 0.35f, -7f, 7f);
        }

        float damp = Mathf.Exp(-8f * Time.deltaTime);
        _swayYaw *= damp;
        _swayPitch *= damp;
        _sway.localRotation = Quaternion.Euler(_swayPitch, _swayYaw, 0f);
    }

    private void CacheCameraAngles()
    {
        var e = _cam.rotation.eulerAngles;
        _lastYaw = e.y;
        _lastPitch = e.x;
    }

    // ------------------------------------------------------------ parts

    private static GameObject Part(Transform parent, Material mat, float radius, float length, float z)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(go.GetComponent<Collider>());

        var t = go.transform;
        t.SetParent(parent, false);
        // Unity cylinder: diameter 1, height 2 -> scale x/z by diameter, y by half length.
        t.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        t.localRotation = Quaternion.Euler(90f, 0f, 0f); // axis along +Z
        t.localPosition = new Vector3(0f, 0f, z);

        var renderer = go.GetComponent<Renderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.sharedMaterial = mat;
        return go;
    }

    private static Material MakeMaterial(Color color, float smoothness, float metallic, bool emissive = false)
    {
        Shader shader = null;
        foreach (var name in new[]
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Sprites/Default",
        })
        {
            shader = Shader.Find(name);
            if (shader != null)
            {
                break;
            }
        }

        if (shader == null)
        {
            FlashlightPlugin.Log.LogWarning("No URP shader found for the flashlight model; model will be invisible-ish.");
            return new Material(Shader.Find("Sprites/Default"));
        }

        var m = new Material(shader);
        if (m.HasProperty("_BaseColor"))
        {
            m.SetColor("_BaseColor", color);
        }

        if (m.HasProperty("_Color"))
        {
            m.SetColor("_Color", color);
        }

        if (m.HasProperty("_Smoothness"))
        {
            m.SetFloat("_Smoothness", smoothness);
        }

        if (m.HasProperty("_Metallic"))
        {
            m.SetFloat("_Metallic", metallic);
        }

        if (emissive && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color);
        }

        return m;
    }
}
