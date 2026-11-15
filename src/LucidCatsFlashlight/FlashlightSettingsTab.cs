using System;
using System.Collections.Generic;
using System.Reflection;
using Game.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace LucidCatsFlashlight;

/// <summary>
/// A FLASHLIGHT tab for the game's Settings screen. Everything renders on our
/// own screen-space canvas - the game's hierarchy is never modified. The
/// settings panel's on-screen rect is measured every frame, and the tab button
/// / cover are positioned to match. Activating the tab shows an opaque cover
/// with a FLASHLIGHT section whose controls are clones of the game's own
/// (identical look). Everything applies instantly; config saves debounced.
/// </summary>
public static class FlashlightSettingsTab
{
    private const int CanvasOrder = 30000; // always above the game's menus
    private const int CursorOrder = 32000; // the game's own cursor goes above everything

    private static Canvas _canvas;               // our overlay canvas (built once)
    private static GameObject _tabButton;        // container with the FLASHLIGHT tab
    private static GameObject _cover;            // opaque panel cover + flashlight rows
    private static TMP_Text _keyBoxText;
    private static SettingsMenu _menu;
    private static bool _tabActive;
    private static float _nextMenuSearch;
    private static float _nextBuildAttempt;
    private static bool _scalerCopied;
    private static bool _configDirty;
    private static float _configDirtyAt;
    private static float _builtW = -1f;
    private static float _builtH = -1f;
    private static Rect _coverRect;

    // The game draws its own software cursor on a UI canvas; it must render
    // above our cover or it vanishes whenever the pointer is over the panel.
    private static bool _cursorScanDone;
    private static Canvas _cursorCanvas;
    private static Sprite _cursorSprite;
    private static bool _cursorRaised;
    private static GameObject _cursorDot;

    // Style stolen from the game's own controls (fallbacks if reflection fails).
    private static TMP_FontAsset _font;
    private static float _labelSize = 20f;
    private static float _titleSize = 34f;
    private static Color _valueColor = new(1f, 0.96f, 0.88f);
    private static Color _headerColor = new(0.52f, 0.52f, 0.52f);
    private static Color _panelColor = new(0.04f, 0.04f, 0.05f, 1f);

    /// <summary>Called by the loader every frame.</summary>
    public static void Tick(FlashlightLoader loader)
    {
        // Debounced config save, flushed even while the menu is closed.
        if (_configDirty && Time.unscaledTime - _configDirtyAt >= 0.6f)
        {
            FlashlightPlugin.SaveConfig();
            _configDirty = false;
        }

        bool open = IsSettingsOpen(out var menu);
        if (!open)
        {
            if (_tabButton != null && _tabButton.activeSelf)
            {
                _tabButton.SetActive(false);
            }

            if (_cover != null && _cover.activeSelf)
            {
                _cover.SetActive(false);
            }

            _tabActive = false;
            return;
        }

        _menu = menu;
        EnsureCanvas();
        StealStyle(menu);
        if (!_scalerCopied)
        {
            return; // let the copied canvas scaler settle one tick before measuring
        }

        if (!MeasurePanel(out var rect))
        {
            return;
        }

        _coverRect = rect;

        // Resolution changed mid-session: rebuild the cover to the new size.
        if (_cover != null && (Mathf.Abs(rect.width - _builtW) > 4f || Mathf.Abs(rect.height - _builtH) > 4f))
        {
            UnityEngine.Object.Destroy(_cover);
            _cover = null;
            _builtW = _builtH = -1f;
        }

        if (_cover == null)
        {
            if (Time.unscaledTime < _nextBuildAttempt || !BuildCover(loader, rect))
            {
                return;
            }
        }

        if (_tabButton == null)
        {
            _tabButton = BuildTabButton(rect);
            FlashlightPlugin.Log.LogInfo("Flashlight settings tab ready (panel measured).");
        }

        // Track the panel every frame (menus can move; resolution can change).
        PositionRect(_tabButton.GetComponent<RectTransform>(), rect);
        PositionRect(_cover.GetComponent<RectTransform>(), rect);

        if (_tabButton.activeSelf == _tabActive)
        {
            _tabButton.SetActive(!_tabActive);
        }

        if (_cover.activeSelf != _tabActive)
        {
            _cover.SetActive(_tabActive);
        }

        if (_tabActive && _keyBoxText != null)
        {
            _keyBoxText.text = loader.CurrentKeyName;
        }

        MaintainCursorVisibility();
    }

    // ------------------------------------------------------------ game cursor

    private static Vector2 MouseScreenPos()
    {
        var mouse = UnityEngine.InputSystem.Mouse.current;
        return mouse != null ? (Vector2)mouse.position.ReadValue() : Vector2.zero;
    }

    private static void MaintainCursorVisibility()
    {
        if (!_cursorScanDone)
        {
            _cursorScanDone = true;
            ScanForGameCursor();
        }
        else if (_cursorCanvas != null && _cursorCanvas.sortingOrder < CursorOrder)
        {
            // The game may reset its canvas order; keep ours underneath it.
            _cursorCanvas.overrideSorting = true;
            _cursorCanvas.sortingOrder = CursorOrder;
        }

        // Fallback: while the pointer is over our cover and the game's cursor
        // could not be raised, draw a small follower dot above the cover.
        bool wantDot = _tabActive && !_cursorRaised && CoverContainsMouse();
        if (wantDot)
        {
            EnsureCursorDot();
        }

        if (_cursorDot != null)
        {
            if (_cursorDot.activeSelf != wantDot)
            {
                _cursorDot.SetActive(wantDot);
            }

            if (wantDot)
            {
                _cursorDot.transform.SetAsLastSibling();
                var ourRect = (RectTransform)_canvas.transform;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    ourRect, MouseScreenPos(), null, out var local);
                _cursorDot.GetComponent<RectTransform>().anchoredPosition = local;
            }
        }
    }

    private static void ScanForGameCursor()
    {
        try
        {
            var menuCanvas = _menu != null ? _menu.GetComponentInParent<Canvas>() : null;
            var seen = new HashSet<Canvas>();

            foreach (var img in UnityEngine.Object.FindObjectsOfType<Image>(false))
            {
                if (!LooksLikeCursor(img.gameObject.name, img.sprite != null ? img.sprite.name : null))
                {
                    continue;
                }

                var canvas = img.GetComponentInParent<Canvas>();
                if (canvas == null || canvas == _canvas || !seen.Add(canvas))
                {
                    continue;
                }

                if (_cursorSprite == null && img.sprite != null)
                {
                    _cursorSprite = img.sprite; // reuse for the fallback dot's look
                }

                if (canvas != menuCanvas && canvas.sortingOrder < CursorOrder)
                {
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = CursorOrder;
                    _cursorCanvas = canvas;
                    _cursorRaised = true;
                }
            }

            foreach (var raw in UnityEngine.Object.FindObjectsOfType<RawImage>(false))
            {
                if (!LooksLikeCursor(raw.gameObject.name, raw.texture != null ? raw.texture.name : null))
                {
                    continue;
                }

                var canvas = raw.GetComponentInParent<Canvas>();
                if (canvas == null || canvas == _canvas || !seen.Add(canvas))
                {
                    continue;
                }

                if (canvas != menuCanvas && canvas.sortingOrder < CursorOrder)
                {
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = CursorOrder;
                    _cursorCanvas = canvas;
                    _cursorRaised = true;
                }
            }

            FlashlightPlugin.Log.LogInfo(
                $"Game cursor canvas {(_cursorRaised ? $"raised to {CursorOrder}" : "not found by name; using follower dot over the cover")}.");
        }
        catch (Exception e)
        {
            FlashlightPlugin.Log.LogWarning($"Cursor canvas scan failed: {e.Message}");
        }
    }

    private static bool LooksLikeCursor(string objectName, string textureName)
    {
        string o = objectName != null ? objectName.ToLowerInvariant() : string.Empty;
        string t = textureName != null ? textureName.ToLowerInvariant() : string.Empty;
        return o.Contains("cursor") || t.Contains("cursor");
    }

    private static void EnsureCursorDot()
    {
        if (_cursorDot != null)
        {
            return;
        }

        var go = new GameObject("LCF_CursorDot");
        go.transform.SetParent(_canvas.transform, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        if (_cursorSprite != null)
        {
            img.sprite = _cursorSprite;
            img.preserveAspect = true;
            img.color = Color.white;
            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(34f, 34f));
        }
        else
        {
            img.color = new Color(1f, 0.96f, 0.88f, 0.9f);
            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(10f, 10f));
        }

        _cursorDot = go;
        _cursorDot.SetActive(false);
    }

    private static bool CoverContainsMouse()
    {
        if (_canvas == null)
        {
            return false;
        }

        var ourRect = (RectTransform)_canvas.transform;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            ourRect, MouseScreenPos(), null, out var local);
        var origin = ToLocal(ourRect, Vector2.zero);
        return _coverRect.Contains(local - origin);
    }

    // ------------------------------------------------------------ state probing

    private static bool IsSettingsOpen(out SettingsMenu menu)
    {
        menu = _menu;
        if (menu == null && Time.unscaledTime >= _nextMenuSearch)
        {
            _nextMenuSearch = Time.unscaledTime + 1f;
            _menu = UnityEngine.Object.FindObjectOfType<SettingsMenu>();
            menu = _menu;
        }

        if (menu == null || !menu.gameObject.activeInHierarchy)
        {
            return false;
        }

        // Menus may either SetActive or fade a CanvasGroup; a clearly hidden
        // group means closed. (Some menus keep blocksRaycasts false even while
        // visible, so it must NOT be used as a visibility signal.)
        var group = menu.GetComponentInParent<CanvasGroup>();
        if (group != null && group.alpha < 0.05f)
        {
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------ canvas + measuring

    private static void EnsureCanvas()
    {
        if (_canvas != null)
        {
            return;
        }

        var go = new GameObject("LCF_SettingsCanvas");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasOrder;
        go.AddComponent<GraphicRaycaster>();
    }

    private static void StealStyle(SettingsMenu menu)
    {
        try
        {
            var valueLabel = GetField<TMP_Text>("masterVolumeLabel", menu);
            if (valueLabel != null)
            {
                _font = valueLabel.font;
                _labelSize = valueLabel.fontSize;
                _valueColor = valueLabel.color;
            }

            foreach (var t in menu.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.text.Trim().Equals("SETTINGS", StringComparison.OrdinalIgnoreCase))
                {
                    _titleSize = t.fontSize;
                    break;
                }
            }

            if (!_scalerCopied)
            {
                // Match the game's UI scaling so stolen font sizes and cloned
                // control sizes land at the same visual size.
                var src = menu.GetComponentInParent<CanvasScaler>();
                if (src != null)
                {
                    var dst = _canvas.gameObject.AddComponent<CanvasScaler>();
                    dst.uiScaleMode = src.uiScaleMode;
                    dst.referenceResolution = src.referenceResolution;
                    dst.screenMatchMode = src.screenMatchMode;
                    dst.matchWidthOrHeight = src.matchWidthOrHeight;
                }

                _scalerCopied = true;
            }
        }
        catch
        {
            // Style fallbacks remain in effect.
        }

        // Auto-sized or odd TMP values can come out unusable; clamp to sane ones.
        if (float.IsNaN(_labelSize) || _labelSize <= 0f || _labelSize > 200f)
        {
            _labelSize = 20f;
        }

        if (float.IsNaN(_titleSize) || _titleSize <= 0f || _titleSize > 300f)
        {
            _titleSize = 34f;
        }
    }

    /// <summary>Measures the settings panel's rect in OUR canvas' local units.</summary>
    private static bool MeasurePanel(out Rect result)
    {
        result = default;
        var label = GetField<TMP_Text>("masterVolumeLabel", _menu);
        Transform t = label != null ? label.transform : _menu.transform;

        var gameCanvas = _menu.GetComponentInParent<Canvas>();
        if (gameCanvas == null || gameCanvas.renderMode == RenderMode.WorldSpace)
        {
            return false;
        }

        Camera cam = gameCanvas.renderMode == RenderMode.ScreenSpaceCamera ? gameCanvas.worldCamera : null;
        var ourRect = (RectTransform)_canvas.transform;

        float screenArea = (float)Screen.width * Screen.height;
        bool found = false;
        float bestArea = float.MaxValue;
        Rect best = default;

        while (t != null)
        {
            if (t is RectTransform rt)
            {
                var img = t.GetComponent<Image>();
                if (img != null && img.color.a >= 0.85f)
                {
                    var corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                    Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                    var screenRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                    float area = screenRect.width * screenRect.height;

                    // The panel is a large opaque rect, but smaller than any
                    // full-screen dim behind it - hence the smallest match.
                    if (area >= screenArea * 0.25f && area < bestArea)
                    {
                        bestArea = area;
                        var localMin = ToLocal(ourRect, min);
                        var localMax = ToLocal(ourRect, max);
                        // Our canvas' local space is centred on its pivot, but
                        // bottom-left-anchored children measure from the canvas'
                        // bottom-left corner - so shift by the local position of
                        // the screen's own bottom-left.
                        var origin = ToLocal(ourRect, Vector2.zero);
                        best = Rect.MinMaxRect(localMin.x - origin.x, localMin.y - origin.y,
                            localMax.x - origin.x, localMax.y - origin.y);
                        var col = img.color;
                        col.a = 1f;
                        _panelColor = col;
                        found = true;
                    }
                }
            }

            t = t.parent;
        }

        if (!found)
        {
            // Fallback: a centred column so the tab still works.
            float w = ourRect.rect.width * 0.4f;
            float h = ourRect.rect.height * 0.8f;
            best = new Rect((ourRect.rect.width - w) / 2f, (ourRect.rect.height - h) / 2f, w, h);
        }

        result = best;
        return true;
    }

    private static Vector2 ToLocal(RectTransform ourRect, Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(ourRect, screenPoint, null, out var local);
        return local;
    }

    // ------------------------------------------------------------ build

    private static bool BuildCover(FlashlightLoader loader, Rect rect)
    {
        GameObject go = null;
        try
        {
            go = new GameObject("LCF_FlashlightCover");
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            PositionRect(rt, rect);
            var bg = go.AddComponent<Image>();
            bg.color = _panelColor;
            bg.raycastTarget = true; // the cover blocks clicks into the rows beneath

            float w = rect.width;
            float h = rect.height;
            float titleBand = TitleBand();
            float padLeft = Mathf.Max(24f, w * 0.055f);
            float labelH = _labelSize * 1.5f;

            // BACK button, vertically centred in the title band.
            Safe(() => BuildTextButton(go.transform, "BACK", _valueColor,
                new Vector2(18f, -(titleBand / 2f)), new Vector2(120f, Mathf.Max(38f, _labelSize * 2f)),
                () => _tabActive = false), "BACK button");

            // SETTINGS title, centred like the game's own.
            Safe(() =>
            {
                var title = MakeLabel(go.transform, "SETTINGS", _titleSize, _valueColor, TextAlignmentOptions.Center);
                SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(w / 2f, -(titleBand / 2f)), new Vector2(w * 0.5f, titleBand * 0.9f));
            }, "title label");

            // Section header.
            Safe(() =>
            {
                var header = MakeLabel(go.transform, "FLASHLIGHT", _labelSize, _headerColor, TextAlignmentOptions.Left);
                SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0f),
                    new Vector2(padLeft, -titleBand), new Vector2(w * 0.4f, labelH));
            }, "section header");

            float rowsTop = titleBand + labelH + 14f;
            const int RowCount = 6;
            float rowsArea = h - rowsTop - 20f;
            float rowH = Mathf.Max(34f, Mathf.Min(60f, rowsArea / RowCount));
            float r0 = rowsTop;
            float r1 = r0 + rowH;
            float r2 = r1 + rowH;
            float r3 = r2 + rowH;
            float r4 = r3 + rowH;
            float r5 = r4 + rowH;
            float rc(float row) => row + rowH / 2f;

            Safe(() =>
            {
                RowLabel(go.transform, "TOGGLE KEY", padLeft, w, rc(r0), labelH);
                BuildKeyBox(go.transform, loader, w, rc(r0), rowH);
            }, "TOGGLE KEY row");

            Safe(() =>
            {
                RowLabel(go.transform, "TOGGLE SOUND", padLeft, w, rc(r1), labelH);
                BuildToggle(go.transform, FlashlightPlugin.Sound.Value, w, rc(r1), rowH,
                    on => FlashlightPlugin.Sound.Value = on);
            }, "TOGGLE SOUND row");

            Safe(() =>
            {
                RowLabel(go.transform, "CLICK VOLUME", padLeft, w, rc(r2), labelH);
                BuildSlider(go.transform, "masterVolumeSlider", 0f, 1f, false, FlashlightPlugin.Volume.Value,
                    w, rc(r2), rowH, v => $"{Mathf.RoundToInt(v * 100f)}%", v => FlashlightPlugin.Volume.Value = v);
            }, "CLICK VOLUME row");

            Safe(() =>
            {
                RowLabel(go.transform, "HUD SIZE", padLeft, w, rc(r3), labelH);
                BuildSlider(go.transform, "masterVolumeSlider", 0.5f, 3f, false, FlashlightPlugin.HudScale.Value,
                    w, rc(r3), rowH, v => $"{v:0.0}x", v => FlashlightPlugin.HudScale.Value = v);
            }, "HUD SIZE row");

            Safe(() =>
            {
                RowLabel(go.transform, "GLOW INTENSITY", padLeft, w, rc(r4), labelH);
                BuildSlider(go.transform, "masterVolumeSlider", 0f, 3f, false, FlashlightPlugin.GlowIntensity.Value,
                    w, rc(r4), rowH, v => v.ToString("0.0"), v => FlashlightPlugin.GlowIntensity.Value = v);
            }, "GLOW INTENSITY row");

            Safe(() =>
            {
                RowLabel(go.transform, "BATTERY HUD", padLeft, w, rc(r5), labelH);
                BuildToggle(go.transform, FlashlightPlugin.ShowBatteryHud.Value, w, rc(r5), rowH,
                    on => FlashlightPlugin.ShowBatteryHud.Value = on);
            }, "BATTERY HUD row");

            _cover = go;
            _builtW = rect.width;
            _builtH = rect.height;
            go.SetActive(_tabActive);
            return true;
        }
        catch (Exception e)
        {
            FlashlightPlugin.Log.LogWarning($"Flashlight settings tab could not be built: {e}");
            if (go != null)
            {
                UnityEngine.Object.Destroy(go); // no partial leaks
            }

            _cover = null;
            _nextBuildAttempt = Time.unscaledTime + 2f;
            return false;
        }
    }

    private static GameObject BuildTabButton(Rect rect)
    {
        var container = new GameObject("LCF_TabContainer");
        container.transform.SetParent(_canvas.transform, false);
        PositionRect(container.AddComponent<RectTransform>(), rect);

        var btnGo = new GameObject("TabButton");
        btnGo.transform.SetParent(container.transform, false);
        var img = btnGo.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.06f);
        img.raycastTarget = true;
        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = img;
        SetRect(btnGo.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            new Vector2(20f, -TitleBand() / 2f),
            new Vector2(Mathf.Max(150f, _labelSize * 7f), Mathf.Max(38f, _labelSize * 2f)));

        var label = MakeLabel(btnGo.transform, "FLASHLIGHT", _labelSize * 0.95f, _headerColor, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);

        btn.onClick = new Button.ButtonClickedEvent();
        btn.onClick.AddListener(() => _tabActive = true);
        container.SetActive(false);
        return container;
    }

    // ------------------------------------------------------------ rows

    private static void RowLabel(Transform parent, string text, float padLeft, float w, float rowCenter, float labelH)
    {
        var label = MakeLabel(parent, text, _labelSize, _valueColor, TextAlignmentOptions.Left);
        SetRect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 0.5f),
            new Vector2(padLeft, -rowCenter), new Vector2(w * 0.34f, labelH));
    }

    private static void BuildKeyBox(Transform parent, FlashlightLoader loader, float w, float rowCenter, float rowH)
    {
        GameObject go = null;
        bool wasCloned = false;
        var proto = GetField<TMP_Dropdown>("microphoneDropdown", _menu);
        if (proto != null)
        {
            // Clone the game's dropdown box for an identical look. Unity can
            // throw while instantiating the game's live object, so this path
            // is fully guarded and never leaves a half-built clone behind.
            try
            {
                go = BuildClonedKeyBox(proto, parent, loader);
                wasCloned = go != null;
            }
            catch (Exception e)
            {
                FlashlightPlugin.Log.LogWarning($"Settings tab: key box clone failed, using plain box: {e.Message}");
                go = null;
            }
        }

        // The plain box has no failure modes - the key box must always exist.
        if (go == null)
        {
            go = BuildPlainKeyBox(parent, loader.CurrentKeyName, new Vector2(w * 0.28f, rowH));
        }

        var rt = go.GetComponent<RectTransform>();
        Place(rt, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(w * 0.51f, -rowCenter));
        var size = rt.sizeDelta;
        rt.sizeDelta = new Vector2(Mathf.Min(w * 0.28f, size.x), size.y); // narrower than the mic dropdown was

        var boxImage = go.GetComponent<Image>();

        // Unity's == also catches destroyed-component wrappers, which C#'s `??`
        // silently keeps and then throws on when the wrapper is touched.
        var btn = go.GetComponent<Button>();
        if (btn == null)
        {
            btn = go.AddComponent<Button>();
        }

        if (btn == null)
        {
            // The root refused a Button; hang the click handler on a fresh
            // invisible overlay child instead.
            btn = EnsureClickChild(go.transform);
        }

        if (btn == null)
        {
            // Nothing accepted a click handler this frame; tear the box down
            // so the next rebuild attempt can try again from scratch.
            UnityEngine.Object.Destroy(go);
            throw new InvalidOperationException(
                $"key box click handler unavailable (cloned={wasCloned}, image={boxImage != null}, root active={go.activeInHierarchy})");
        }

        btn.targetGraphic = boxImage;
        btn.onClick = new Button.ButtonClickedEvent();
        btn.onClick.AddListener(loader.BeginRebindFromUi);
        FlashlightPlugin.Log.LogInfo(
            $"Settings tab: key box built (cloned={wasCloned}, image={boxImage != null}, button={btn != null}).");
    }

    private static GameObject BuildClonedKeyBox(TMP_Dropdown proto, Transform parent, FlashlightLoader loader)
    {
        var go = UnityEngine.Object.Instantiate(proto.gameObject, parent);
        try
        {
            go.name = "LCF_KeyBox";
            UnityEngine.Object.Destroy(go.GetComponent<TMP_Dropdown>());
            StripChildren(go, "Template", "Arrow", "Scrollbar", "Handle Slide Area");

            var tmp = go.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = loader.CurrentKeyName;
                if (proto.captionText != null)
                {
                    tmp.color = proto.captionText.color;
                }

                tmp.alignment = TextAlignmentOptions.Center;
                tmp.raycastTarget = false;
                _keyBoxText = tmp;
            }
        }
        catch
        {
            UnityEngine.Object.Destroy(go); // no half-built clones
            throw;
        }

        return go;
    }

    private static GameObject BuildPlainKeyBox(Transform parent, string keyName, Vector2 size)
    {
        var go = new GameObject("LCF_KeyBox");
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.82f, 0.82f, 0.82f);
        ((RectTransform)go.transform).sizeDelta = size;
        var label = MakeLabel(go.transform, keyName, _labelSize, new Color(0.1f, 0.1f, 0.1f), TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        _keyBoxText = label;
        return go;
    }

    /// <summary>Transparent stretched overlay child used purely for click events.</summary>
    private static Button EnsureClickChild(Transform parent)
    {
        try
        {
            var hit = new GameObject("LCF_KeyBoxClick");
            hit.transform.SetParent(parent, false);
            Stretch(hit.AddComponent<RectTransform>());
            var img = hit.AddComponent<Image>();
            img.color = Color.clear; // invisible, raycast only
            img.raycastTarget = true;
            return hit.AddComponent<Button>();
        }
        catch (Exception e)
        {
            FlashlightPlugin.Log.LogWarning($"Settings tab: click-child fallback failed: {e.Message}");
            return null;
        }
    }

    private static void BuildToggle(Transform parent, bool initial, float w, float rowCenter, float rowH, Action<bool> onChange)
    {
        var proto = GetField<Toggle>("hideCrosshairToggle", _menu);
        if (proto == null)
        {
            return;
        }

        var go = UnityEngine.Object.Instantiate(proto.gameObject, parent);
        go.name = "LCF_Toggle";
        var toggle = go.GetComponent<Toggle>();
        toggle.onValueChanged = new Toggle.ToggleEvent(); // drop anything cloned
        Place(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(w * 0.51f, -rowCenter)); // natural size kept
        toggle.SetIsOnWithoutNotify(initial);
        toggle.onValueChanged.AddListener(v =>
        {
            onChange(v);
            MarkDirty();
        });
    }

    private static void BuildSlider(Transform parent, string protoField, float min, float max, bool whole, float initial,
        float w, float rowCenter, float rowH, Func<float, string> format, Action<float> onChange)
    {
        var proto = GetField<Slider>(protoField, _menu);
        if (proto == null)
        {
            return;
        }

        var go = UnityEngine.Object.Instantiate(proto.gameObject, parent);
        go.name = "LCF_Slider";
        var slider = go.GetComponent<Slider>();
        slider.onValueChanged = new Slider.SliderEvent(); // drop anything cloned
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = whole;
        slider.SetValueWithoutNotify(Mathf.Clamp(initial, min, max));

        var rt = go.GetComponent<RectTransform>();
        Place(rt, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(w * 0.755f, -rowCenter));
        var size = rt.sizeDelta;
        rt.sizeDelta = new Vector2(w * 0.41f, size.y); // game's height, our length

        var valueLabel = MakeLabel(parent, format(initial), _labelSize, _valueColor, TextAlignmentOptions.Center);
        SetRect(valueLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
            new Vector2(w * 0.44f, -rowCenter), new Vector2(w * 0.1f, rowH * 0.8f));

        slider.onValueChanged.AddListener(v =>
        {
            onChange(v);
            valueLabel.text = format(v);
            MarkDirty();
        });
    }

    private static void BuildTextButton(Transform parent, string text, Color color, Vector2 centre, Vector2 size, Action onClick)
    {
        var go = new GameObject("Btn_" + text);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.06f);
        img.raycastTarget = true;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        SetRect(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), centre, size);

        var label = MakeLabel(go.transform, text, _labelSize * 0.95f, color, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);

        btn.onClick = new Button.ButtonClickedEvent();
        btn.onClick.AddListener(() => onClick());
    }

    // ------------------------------------------------------------ helpers

    private static void Safe(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            FlashlightPlugin.Log.LogWarning($"Settings tab: {what} failed: {e}");
        }
    }

    private static float TitleBand() => Mathf.Max(64f, _titleSize * 2.4f);

    private static void MarkDirty()
    {
        _configDirty = true;
        _configDirtyAt = Time.unscaledTime;
    }

    private static T GetField<T>(string name, SettingsMenu menu) where T : class
    {
        var field = typeof(SettingsMenu).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
        return field?.GetValue(menu) as T;
    }

    private static TMP_Text MakeLabel(Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (_font != null)
        {
            tmp.font = _font;
        }

        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>Sizes/positions a container to the measured panel rect (our canvas' local units).</summary>
    private static void PositionRect(RectTransform rt, Rect rect)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.anchoredPosition = new Vector2(rect.x, rect.y);
        rt.sizeDelta = new Vector2(rect.width, rect.height);
    }

    private static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
    }

    private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 anchoredPos)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void StripChildren(GameObject go, params string[] names)
    {
        for (int i = go.transform.childCount - 1; i >= 0; i--)
        {
            var child = go.transform.GetChild(i);
            foreach (var name in names)
            {
                if (child.name == name)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                    break;
                }
            }
        }
    }
}
