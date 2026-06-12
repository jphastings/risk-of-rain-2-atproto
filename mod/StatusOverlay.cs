using ByJP.AtprotoGaming.Core;
using UnityEngine;

namespace ByJP.Ror2.Play
{
    /// <summary>
    /// A small "@" status badge (top-right) reflecting the atproto login state at a glance
    /// — struck through when it can't reach the PDS — that, when clicked, shows who you're
    /// signed in as (or what's wrong) plus the mod version. IMGUI for now: functional,
    /// not yet RoR2-skinned.
    /// </summary>
    internal sealed class StatusOverlay : MonoBehaviour
    {
        private AuthState? _auth;
        private string _version = "";
        private bool _open;
        private GUIStyle? _badge, _title, _body;
        private static Texture2D? _pixel;

        public void Init(AuthState auth, string version)
        {
            _auth = auth;
            _version = version;
        }

        private void OnGUI()
        {
            if (_auth == null) return;
            EnsureStyles();
            var status = _auth.Status;

            const float size = 30f, margin = 10f;
            var badge = new Rect(Screen.width - size - margin, margin, size, size);

            var color = StatusColor(status);
            _badge!.normal.textColor = _badge.hover.textColor = _badge.active.textColor = color;
            if (GUI.Button(badge, "@", _badge)) _open = !_open;
            if (IsProblem(status)) DrawStrike(badge);

            if (_open) DrawPanel(badge, status);
        }

        private void DrawPanel(Rect badge, AuthStatus status)
        {
            const float w = 340f, margin = 10f;
            var lines = StatusLines(status);
            var h = 56f + lines.Length * 18f;
            var panel = new Rect(Screen.width - w - margin, badge.yMax + 6f, w, h);

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(panel, Pixel());
            GUI.color = prev;

            GUI.Label(new Rect(panel.x + 12, panel.y + 8, w - 24, 22), "Atproto Play Tracking", _title);
            var y = panel.y + 32;
            foreach (var line in lines)
            {
                GUI.Label(new Rect(panel.x + 12, y, w - 24, 18), line, _body);
                y += 18;
            }
            GUI.Label(new Rect(panel.x + 12, panel.yMax - 22, w - 24, 18), "v" + _version, _body);
        }

        private string[] StatusLines(AuthStatus status)
        {
            switch (status)
            {
                case AuthStatus.Ok:
                    return new[] { "Signed in as " + (_auth!.Handle ?? "?") };
                case AuthStatus.Checking:
                    return new[] { "Checking credentials..." };
                case AuthStatus.Offline:
                    return new[] { "Offline - can't reach your PDS.", "Runs are queued; they sync when you reconnect." };
                case AuthStatus.Failed:
                    return new[] { "Not connected.", _auth!.Error ?? "Credentials were rejected." };
                default:
                    return new[] { "Not configured.", "Set your handle + app password in the config." };
            }
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

        private void DrawStrike(Rect r)
        {
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, r.center);
            var prev = GUI.color;
            GUI.color = new Color(1f, 0.3f, 0.3f);
            GUI.DrawTexture(new Rect(r.center.x - r.width * 0.6f, r.center.y - 1.5f, r.width * 1.2f, 3f), Pixel());
            GUI.color = prev;
            GUI.matrix = matrix;
        }

        private void EnsureStyles()
        {
            if (_badge != null) return;
            _badge = new GUIStyle(GUI.skin.button) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold };
            _title.normal.textColor = Color.white;
            _body = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _body.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        }

        private static Texture2D Pixel()
        {
            if (_pixel == null)
            {
                _pixel = new Texture2D(1, 1);
                _pixel.SetPixel(0, 0, Color.white);
                _pixel.Apply();
            }
            return _pixel;
        }
    }
}
