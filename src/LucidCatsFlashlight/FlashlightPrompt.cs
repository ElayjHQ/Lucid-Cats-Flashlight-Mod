using UnityEngine;
using UnityEngine.UI;

namespace LucidCatsFlashlight;

/// <summary>
/// Tiny centred text prompt used while rebinding the flashlight key. Built on
/// demand with uGUI (no assets), never intercepts input, hides when done.
/// </summary>
public static class FlashlightPrompt
{
    private static Canvas _canvas;
    private static Text _label;

    public static void Show(string message)
    {
        try
        {
            EnsureBuilt();
            if (_label == null)
            {
                return; // no built-in font available; rebinding still works silently
            }

            _label.text = message;
            _canvas.gameObject.SetActive(true);
        }
        catch (System.Exception e)
        {
            FlashlightPlugin.Log.LogWarning($"Rebind prompt could not be shown ({e.Message}).");
        }
    }

    public static void Hide()
    {
        if (_canvas != null)
        {
            _canvas.gameObject.SetActive(false);
        }
    }

    private static void EnsureBuilt()
    {
        if (_canvas != null)
        {
            return;
        }

        var font = FlashlightHud.BuiltinFont();
        if (font == null)
        {
            return;
        }

        var go = new GameObject("LCF_RebindPrompt");
        Object.DontDestroyOnLoad(go);
        _canvas = go.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 31000; // above the settings tab cover

        var group = go.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false; // never steal mouse input from the game

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        _label = textGo.AddComponent<Text>();
        _label.font = font;
        _label.fontSize = 22;
        _label.alignment = TextAnchor.MiddleCenter;
        _label.color = new Color(1f, 0.97f, 0.9f);
        _label.raycastTarget = false;
        _label.text = string.Empty;

        var outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        var rect = textGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(700f, 90f);
        rect.anchoredPosition = Vector2.zero; // centre of the screen
    }
}
