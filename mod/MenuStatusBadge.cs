using System;
using ByJP.AtprotoGaming.Core;
using RoR2;
using RoR2.UI.MainMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ByJP.Ror2.Play
{
    /// <summary>
    /// Native main-menu "@" status badge (top-right): a TMP glyph in the game's own font,
    /// coloured by the atproto login state and struck through (TMP <c>&lt;s&gt;</c>) when it
    /// can't reach the PDS. Click it to expand "Signed in as …" / the problem + the mod
    /// version. Built once under the game's main canvas and shown only while the main menu
    /// is up (so it's menu-only). Replaces the earlier always-on IMGUI overlay.
    /// </summary>
    internal sealed class MenuStatusBadge : MonoBehaviour
    {
        private static MenuStatusBadge? _instance;
        private static Sprite? _white;

        private AuthState _auth = null!;
        private string _version = "";
        private GameObject _content = null!;
        private TMP_Text _glyph = null!;
        private GameObject _panel = null!;
        private TMP_Text _panelText = null!;
        private AuthStatus _shown = (AuthStatus)(-1);

        /// <summary>Wires the badge to be (re)built when the main menu initialises.</summary>
        public static void Install(AuthState auth, string version)
        {
            MainMenuController.OnMainMenuInitialised += () =>
            {
                if (_instance != null) return;
                var canvas = RoR2Application.instance != null ? RoR2Application.instance.mainCanvas : null;
                if (canvas == null) return;

                var go = new GameObject("AtprotoStatusBadge", typeof(RectTransform));
                go.transform.SetParent(canvas.transform, false);
                _instance = go.AddComponent<MenuStatusBadge>();
                _instance._auth = auth;
                _instance._version = version;
            };
        }

        private void Start()
        {
            try { BuildUi(); }
            catch (Exception e)
            {
                Debug.LogError("[atproto] status badge build failed: " + e);
                _instance = null;
                Destroy(gameObject);
            }
        }

        // AuthState.Changed fires on the login thread; reading it from Update keeps all UI
        // mutation on the main thread. Also gates the badge to the menu (menu-only).
        private void Update()
        {
            if (_content == null) return;
            var onMenu = MainMenuController.instance != null;
            if (_content.activeSelf != onMenu) _content.SetActive(onMenu);
            if (onMenu && _auth.Status != _shown) Refresh();
        }

        private void BuildUi()
        {
            Stretch((RectTransform)transform);

            // Own nested canvas so the badge sorts + raycasts above the menu's canvases —
            // it rendered fine, but the menu's UI was intercepting the clicks. Empty areas
            // aren't raycast targets, so this doesn't block the menu behind it.
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30000;
            gameObject.AddComponent<GraphicRaycaster>();

            _content = NewUi("Content", transform);
            var crt = (RectTransform)_content.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 1f); // top-right
            crt.anchoredPosition = new Vector2(-18f, -18f);
            crt.sizeDelta = new Vector2(44f, 44f);

            var font = FindMenuFont();

            _glyph = NewText(_content.transform, "@", 30f, font, TextAlignmentOptions.Center);
            Stretch((RectTransform)_glyph.transform);
            var btn = _glyph.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { if (_panel != null) _panel.SetActive(!_panel.activeSelf); });

            _panel = NewUi("Panel", _content.transform);
            var prt = (RectTransform)_panel.transform;
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(0f, -50f);
            prt.sizeDelta = new Vector2(340f, 96f);
            var bg = _panel.AddComponent<Image>();
            bg.sprite = White();
            bg.color = new Color(0.03f, 0.04f, 0.07f, 0.93f);

            _panelText = NewText(_panel.transform, "", 16f, font, TextAlignmentOptions.TopLeft);
            var trt = (RectTransform)_panelText.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(14f, 12f); trt.offsetMax = new Vector2(-14f, -12f);

            _panel.SetActive(false);
            Refresh();
        }

        private void Refresh()
        {
            _shown = _auth.Status;
            var problem = IsProblem(_shown);
            _glyph.text = problem ? "<s>@</s>" : "@";
            _glyph.color = StatusColor(_shown);
            _panelText.text = PanelText(_shown);
        }

        private string PanelText(AuthStatus status)
        {
            string body;
            switch (status)
            {
                case AuthStatus.Ok: body = "Signed in as " + (_auth.Handle ?? "?"); break;
                case AuthStatus.Checking: body = "Checking credentials…"; break;
                case AuthStatus.Offline: body = "Offline — can't reach your PDS.\nRuns are queued; they sync when you reconnect."; break;
                case AuthStatus.Failed: body = "Not connected.\n" + (_auth.Error ?? "Credentials were rejected."); break;
                default: body = "Not configured.\nSet your handle + app password in the config."; break;
            }
            return "<b>Atproto Play Tracking</b>\n" + body + "\n<size=80%>v" + _version + "</size>";
        }

        private static bool IsProblem(AuthStatus s) =>
            s == AuthStatus.Failed || s == AuthStatus.Offline || s == AuthStatus.Unconfigured;

        private static Color StatusColor(AuthStatus s)
        {
            switch (s)
            {
                case AuthStatus.Ok: return new Color(0.45f, 1f, 0.55f);
                case AuthStatus.Failed: return new Color(1f, 0.45f, 0.45f);
                case AuthStatus.Offline: return new Color(1f, 0.8f, 0.35f);
                case AuthStatus.Checking: return Color.white;
                default: return new Color(0.7f, 0.7f, 0.7f);
            }
        }

        private static TMP_FontAsset? FindMenuFont()
        {
            var sample = MainMenuController.instance != null
                ? MainMenuController.instance.GetComponentInChildren<TMP_Text>(true)
                : null;
            return sample != null ? sample.font : null;
        }

        private static GameObject NewUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private static TMP_Text NewText(Transform parent, string text, float size, TMP_FontAsset? font, TextAlignmentOptions align)
        {
            var go = NewUi("Text", parent);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.richText = true;
            tmp.color = Color.white;
            if (font != null) tmp.font = font;
            return tmp;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static Sprite White()
        {
            if (_white == null)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _white = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
            }
            return _white;
        }
    }
}
