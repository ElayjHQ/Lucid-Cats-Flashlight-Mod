using UnityEngine;

namespace LucidCatsFlashlight;

/// <summary>
/// A low-poly flashlight built from Unity primitives, styled to fit the game's
/// PSX look. Constructed entirely at runtime - no imported assets. Shows a warm
/// glowing lens while the flashlight is on; bobs and sways in first person.
/// </summary>
public class FlashlightViewModel : MonoBehaviour
{
    private Transform _sway;
    private Transform _bob;
    private Material _lensMaterial;

    private bool _isLocal;
    private Game.Player.PlayerManager _player;
    private Transform _cam;

    private float _bobPhase;
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
        if (isLocal && Camera.main != null)
        {
            _cam = Camera.main.transform;
            CacheCameraAngles();
        }

        // Sway (rotation lag) -> bob (walk bounce) -> geometry
        _sway = new GameObject("LCF_Sway").transform;
        _sway.SetParent(transform, false);
        _bob = new GameObject("LCF_Bob").transform;
        _bob.SetParent(_sway, false);

        var bodyMat = MakeMaterial(new Color(0.13f, 0.13f, 0.15f), 0.65f, 0.7f);
        var ringMat = MakeMaterial(new Color(0.07f, 0.07f, 0.08f), 0.4f, 0.6f);
        _lensMaterial = MakeMaterial(new Color(2.2f, 1.8f, 1.1f), 0f, 0f, emissive: true);

        // All parts point down +Z of the rig (same direction the light shines).
        Part(_bob, bodyMat, radius: 0.032f, length: 0.19f, z: 0.010f);             // body
        Part(_bob, ringMat, radius: 0.048f, length: 0.022f, z: 0.062f);            // head ring
        Part(_bob, bodyMat, radius: 0.046f, length: 0.075f, z: 0.112f);            // flared head
        Part(_bob, _lensMaterial, radius: 0.040f, length: 0.006f, z: 0.151f);      // lens
        Part(_bob, ringMat, radius: 0.034f, length: 0.018f, z: -0.093f);           // tail cap

        // Marker at the lens face: the light + volumetric cone mount here, so the
        // beam physically starts at the lens instead of behind the model.
        LensTip = new GameObject("LCF_LensTip").transform;
        LensTip.SetParent(_bob, false);
        LensTip.localPosition = new Vector3(0f, 0f, 0.151f);
    }

    /// <summary>Transform at the lens face; the rig mounts its light here.</summary>
    public Transform LensTip { get; private set; }

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
        if (!_isLocal)
        {
            return;
        }

        if (_cam == null && Camera.main != null)
        {
            _cam = Camera.main.transform;
            CacheCameraAngles();
        }

        // Sway: lag slightly behind camera rotation, spring back.
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

        // Bob: gentle walk bounce scaled by movement speed.
        float speed = GetMoveSpeed();
        if (speed > 0.05f)
        {
            _bobPhase += Time.deltaTime * Mathf.Lerp(6f, 11f, Mathf.Clamp01(speed / 6f));
        }

        float amp = 0.011f * Mathf.Clamp01(speed / 3f);
        _bob.localPosition = new Vector3(
            Mathf.Sin(_bobPhase * 0.5f) * amp * 0.7f,
            -Mathf.Abs(Mathf.Sin(_bobPhase)) * amp,
            0f);
    }

    private void CacheCameraAngles()
    {
        var e = _cam.rotation.eulerAngles;
        _lastYaw = e.y;
        _lastPitch = e.x;
    }

    private float GetMoveSpeed()
    {
        try
        {
            var state = _player != null && _player.PlayerController != null
                ? _player.PlayerController.GetPlayerState()
                : null;
            if (state != null)
            {
                var v = state.CurrentVelocity;
                v.y = 0f;
                return v.magnitude;
            }
        }
        catch
        {
            // Character controller state unavailable; no bob is fine.
        }

        return 0f;
    }
}
