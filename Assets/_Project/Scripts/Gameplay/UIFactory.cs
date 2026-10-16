using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FPS.Gameplay
{
    /// <summary>uGUI built in code for the match HUD, settings menu and workbench: one look, one place.</summary>
    internal static class UIFactory
    {
        public static readonly Color PanelColor = new Color(0.05f, 0.06f, 0.07f, 0.82f);
        public static readonly Color TextColor = new Color(1f, 1f, 1f, 0.9f);

        /// <summary>A screen-space overlay canvas scaled from 1920×1080; clickable when <paramref name="interactive"/>.</summary>
        public static RectTransform Canvas(Transform parent, string canvasName, int sortingOrder, bool interactive)
        {
            var canvasObject = new GameObject(canvasName);
            canvasObject.transform.SetParent(parent, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (interactive) canvasObject.AddComponent<GraphicRaycaster>();
            return canvas.GetComponent<RectTransform>();
        }

        public static RectTransform Panel(RectTransform parent, string panelName, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var panel = new GameObject(panelName).AddComponent<Image>();
            panel.rectTransform.SetParent(parent, false);
            Place(panel.rectTransform, anchor, position, size);
            panel.color = color;
            panel.raycastTarget = color.a > 0f;
            return panel.rectTransform;
        }

        public static Text Label(RectTransform parent, int fontSize, Vector2 anchor, Vector2 position, Vector2 size, TextAnchor alignment)
        {
            var label = new GameObject("Label").AddComponent<Text>();
            label.rectTransform.SetParent(parent, false);
            Place(label.rectTransform, anchor, position, size);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = TextColor;
            label.raycastTarget = false;
            label.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);
            return label;
        }

        public static Button Button(RectTransform parent, string text, Vector2 anchor, Vector2 position, Vector2 size, Action onClick)
        {
            var image = new GameObject($"Button {text}").AddComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            Place(image.rectTransform, anchor, position, size);
            image.color = new Color(0.22f, 0.24f, 0.26f, 0.95f);
            var button = image.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.45f, 0.5f, 0.55f, 1f);
            colors.pressedColor = new Color(0.65f, 0.7f, 0.75f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            Text label = Label(image.rectTransform, 20, new Vector2(0.5f, 0.5f), Vector2.zero, size, TextAnchor.MiddleCenter);
            label.text = text;
            return button;
        }

        public static void SetText(Button button, string text) => button.GetComponentInChildren<Text>().text = text;

        public static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>Menus need an event system reading the Input System; makes one when the scene has none.</summary>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null || UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var system = new GameObject("EventSystem", typeof(EventSystem));
            system.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }
}
