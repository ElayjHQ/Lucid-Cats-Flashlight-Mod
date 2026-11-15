using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace LucidCatsFlashlight;

/// <summary>
/// Minimal uGUI battery bar for the local player, built in code (no assets).
/// Bottom-centre of the screen, shown ONLY while the flashlight is lit (plus a
/// short linger after it goes out, so a battery death is actually seen).
/// White while healthy, amber when low, red when nearly dead, blinking when
/// dead, with the charge percentage inside the bar. Never intercepts input.
/// </summary>
public class FlashlightHud : MonoBehaviour
{
    private const float Width = 150f;
    private const float Height = 20f;
    private const float LingerSeconds = 2f;

    private static readonly Color Healthy = new(1f, 0.96f, 0.88f);
    private static readonly Color Low = new(1f, 0.72f, 0.28f);
    private static readonly Color Critical = new(0.95f, 0.28f, 0.22f);

    private static FlashlightHud _instance;
    internal static Sprite _whiteSprite;

    private RectTransform _fillRect;
    private RectTransform _bgRect;
    private Image _fillImage;
    private Text _label;
    private CanvasGroup _group;
    private float _lingerUntil;

    /// <summary>Creates the HUD once, if the battery system and HUD are enabled.</summary>
    public static void EnsureCreated()
    {
        if (_instance != null || !FlashlightBattery.Enabled || !FlashlightPlugin.ShowBatteryHud.Value)
        {
            return;
        }

        try
        {
            var canvasGo = new GameObject("LCF_BatteryHud");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            Object.DontDestroyOnLoad(canvasGo);

            var group = canvasGo.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false; // never steal mouse input from the game

            // Backing bar.
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(canvasGo.transform, false);
            var bg = bgGo.AddComponent<Image>();
            bg.sprite = WhiteSprite;
            bg.raycastTarget = false;
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            var bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = bgRect.anchorMax = new Vector2(0.5f, 0f);
            bgRect.pivot = new Vector2(0.5f, 0f);
            bgRect.anchoredPosition = new Vector2(0f, 30f);
            bgRect.sizeDelta = new Vector2(Width + 4f, Height + 4f);

            // Charge fill: anchorMax.x is driven every frame = the charge level.
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(bgGo.transform, false);
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = WhiteSprite;
            fill.raycastTarget = false;
            var fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);

            var hud = canvasGo.AddComponent<FlashlightHud>();
            hud._fillRect = fillRect;
            hud._fillImage = fill;
            hud._group = group;
            hud._bgRect = bgRect;

            // Percentage label, centred inside the bar (built-in font, no assets).
            var font = BuiltinFont();
            if (font != null)
            {
                var labelGo = new GameObject("Label");
                labelGo.transform.SetParent(bgGo.transform, false);
                var label = labelGo.AddComponent<Text>();
                label.font = font;
                label.fontSize = 14;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = new Color(1f, 1f, 1f, 0.95f);
                label.raycastTarget = false;
                label.text = "100%";

                var outline = labelGo.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1f, -1f);

                var labelRect = labelGo.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                hud._label = label;
            }

            _instance = hud;
        }
        catch (System.Exception e)
        {
            FlashlightPlugin.Log.LogWarning($"Battery HUD could not be created ({e.Message}).");
        }
    }

    private static Sprite WhiteSprite
    {
        get
        {
            if (_whiteSprite == null)
            {
                _whiteSprite = Sprite.Create(
                    Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 100f);
            }

            return _whiteSprite;
        }
    }

    /// <summary>The built-in UI font; name changed across Unity versions.</summary>
    internal static Font BuiltinFont()
    {
        foreach (var name in new[] { "LegacyRuntime.ttf", "Arial.ttf" })
        {
            try
            {
                var font = Resources.GetBuiltinResource<Font>(name);
                if (font != null)
                {
                    return font;
                }
            }
            catch
            {
                // Try the next name.
            }
        }

        return null;
    }

    private void Update()
    {
        if (_group == null)
        {
            return;
        }

        // Respect the settings toggle live (no restart needed).
        if (!FlashlightPlugin.ShowBatteryHud.Value)
        {
            _group.alpha = 0f;
            return;
        }

        // Respect the size slider live as well.
        ApplyScale();

        // Hide in menus entirely.
        var nm = NetworkManager.Singleton;
        bool inSession = nm != null && nm.IsListening;

        // Visible only while the flashlight is lit; a short linger after it goes
        // out lets a battery death (blink) and the final level actually register.
        if (FlashlightBattery.LocalOn)
        {
            _lingerUntil = Time.unscaledTime + LingerSeconds;
        }

        bool show = inSession && Time.unscaledTime < _lingerUntil;
        if (!show)
        {
            _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, Time.deltaTime * 5f);
            return;
        }

        float level = FlashlightBattery.Level;

        // Fill color: white -> amber -> red as it dies.
        if (level > FlashlightPlugin.LowThreshold.Value)
        {
            _fillImage.color = Healthy;
        }
        else if (level > 0.10f)
        {
            _fillImage.color = Low;
        }
        else
        {
            _fillImage.color = Critical;
        }

        // Grow/shrink with the charge level.
        _fillRect.anchorMax = new Vector2(Mathf.Clamp01(level), 1f);

        // Percentage inside the bar.
        if (_label != null)
        {
            _label.text = $"{Mathf.RoundToInt(level * 100f)}%";
        }

        // Blink while dead; otherwise softly settle at normal opacity.
        bool dead = level <= 0.0005f;
        float targetAlpha = dead ? (Time.time % 0.7f < 0.35f ? 1f : 0.35f) : 0.9f;
        _group.alpha = Mathf.MoveTowards(_group.alpha, targetAlpha, Time.deltaTime * 8f);
    }

    /// <summary>Applies the HudScale slider to the bar and its label, only when changed.</summary>
    private void ApplyScale()
    {
        if (_bgRect == null)
        {
            return;
        }

        float s = Mathf.Max(0.25f, FlashlightPlugin.HudScale.Value);
        var size = new Vector2((Width + 4f) * s, (Height + 4f) * s);
        if (_bgRect.sizeDelta != size)
        {
            _bgRect.sizeDelta = size;
            _bgRect.anchoredPosition = new Vector2(0f, 30f * s);
        }

        if (_label != null)
        {
            int fontSize = Mathf.Clamp(Mathf.RoundToInt(14f * s), 8, 96);
            if (_label.fontSize != fontSize)
            {
                _label.fontSize = fontSize;
            }
        }
    }
}
