/*
 * ====================================================================================
 * ValueOverlay.cs
 * Author: Nathanael Hondi
 * Copyright: © 2026 NPIRE EXE. Licensed under the MIT License.
 * Description: Draws a card in a corner of the Game View that shows live values (a
 *              component field or property, a material property or a shader global)
 *              with an optional bar, and flashes a value when it changes. The card is a
 *              Screen Space Overlay canvas, so GameplayRecorder and screenshots capture
 *              it. Sweep() drives a value from one number to another over time, for
 *              showing a setting change on camera. The generated canvas is never saved.
 * ====================================================================================
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace NPIREEXE.Capture
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("NPIRE EXE/Capture/Value Overlay")]
    public class ValueOverlay : MonoBehaviour
    {
        public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }
        public enum Source { Member, MaterialProperty, ShaderGlobal }

        [Serializable]
        public class Row
        {
            [Tooltip("Name shown on the card. Sweep() finds rows by this.")]
            public string label = "Value";
            public Source source = Source.Member;

            [Tooltip("Component that holds the value (Member source).")]
            public Component target;
            [Tooltip("Field or property name on the target (public or private).")]
            public string member;

            [Tooltip("Material that holds the value (Material Property source).")]
            public Material material;
            [Tooltip("Shader property name, e.g. _Intensity (Material Property and Shader Global sources).")]
            public string property;

            [Tooltip("Draw a bar under the value, filled between Min and Max.")]
            public bool showBar = true;
            public float min = 0f;
            public float max = 1f;
            [Range(0, 4)] public int decimals = 2;

            [NonSerialized] internal MemberInfo cachedMember;
            [NonSerialized] internal Component cachedTarget;
            [NonSerialized] internal string cachedName;
            [NonSerialized] internal float lastValue = float.NaN;
            [NonSerialized] internal float changedAt = -100f;
            [NonSerialized] internal Text labelText;
            [NonSerialized] internal Text valueText;
            [NonSerialized] internal RectTransform fill;
            [NonSerialized] internal Image fillImage;
        }

        [Tooltip("Optional heading at the top of the card.")]
        public string title = "";
        public List<Row> rows = new List<Row>();

        [Header("Look")]
        public Corner corner = Corner.BottomRight;
        [Tooltip("Card size. 1 = sized for 1080 on the screen's short side, and scales with it.")]
        [Range(0.5f, 3f)] public float size = 1f;
        [Tooltip("Distance from the screen corner, in 1080p pixels.")]
        public Vector2 margin = new Vector2(36f, 36f);
        [Tooltip("Card width, in 1080p pixels.")]
        public float width = 360f;
        public Color background = new Color(0.06f, 0.06f, 0.08f, 0.85f);
        public Color textColor = new Color(0.93f, 0.93f, 0.95f, 1f);
        [Tooltip("Bar colour, and the flash on a value when it changes.")]
        public Color accent = new Color(1f, 0.76f, 0.24f, 1f);
        [Tooltip("How long a changed value stays highlighted, in seconds.")]
        public float highlightSeconds = 0.6f;

        [Header("Capture")]
        [Tooltip("Keep the card in 'Take Screenshot (No UI)' captures, which hide every other canvas.")]
        public bool keepInNoUICaptures = true;
        [Tooltip("Show the card outside Play Mode too (for edit-mode stills).")]
        public bool showInEditMode = true;

        private const float Pad = 20f, TitleH = 34f, LineH = 30f, BarH = 6f, BarGap = 5f, RowGap = 14f;

        private const string RootName = "Value Overlay (generated)";
        private static readonly HashSet<Canvas> KeptCanvases = new HashSet<Canvas>();
        private static readonly HashSet<GameObject> LiveRoots = new HashSet<GameObject>();
        private static Sprite _rounded;
        private static Font _font;

        private GameObject _root;
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private RectTransform _card;
        private bool _dirty = true;
        private int _builtRows = -1;

        /// <summary>True for an overlay canvas that 'No UI' screenshots should leave visible.</summary>
        public static bool KeepsCanvas(Canvas canvas) => canvas != null && KeptCanvases.Contains(canvas);

        private bool Visible => Application.isPlaying || showInEditMode;

        private void OnEnable() => _dirty = true;
        private void OnDisable() => Teardown();
        private void OnDestroy() => Teardown();
        private void OnValidate() => _dirty = true;

        private void Update()
        {
            if (!Visible) { Teardown(); return; }
            if (_root == null || _dirty || _builtRows != rows.Count) Build();
            Refresh();
        }

        // ---------------------------------------------------------------- sweeping

        /// <summary>
        /// Drives the row with this label from <paramref name="from"/> to <paramref name="to"/>
        /// over <paramref name="seconds"/> (Play Mode only). Returns false if no row has the
        /// label or its value can't be written.
        /// </summary>
        public bool Sweep(string label, float from, float to, float seconds, bool easeInOut = false)
        {
            int index = rows.FindIndex(r => r.label == label);
            return index >= 0 && Sweep(index, from, to, seconds, easeInOut);
        }

        /// <summary>Drives row <paramref name="index"/> from one value to another (Play Mode only).</summary>
        public bool Sweep(int index, float from, float to, float seconds, bool easeInOut = false)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[ValueOverlay] Sweep runs in Play Mode only.");
                return false;
            }
            if (index < 0 || index >= rows.Count || !TryWrite(rows[index], from)) return false;
            StartCoroutine(SweepRoutine(rows[index], from, to, Mathf.Max(0.01f, seconds), easeInOut));
            return true;
        }

        /// <summary>Stops every running sweep, leaving values where they are.</summary>
        public void StopSweeps() => StopAllCoroutines();

        private IEnumerator SweepRoutine(Row row, float from, float to, float seconds, bool easeInOut)
        {
            float start = Time.unscaledTime;
            while (true)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - start) / seconds);
                float e = easeInOut ? k * k * (3f - 2f * k) : k;
                TryWrite(row, Mathf.Lerp(from, to, e));
                if (k >= 1f) yield break;
                yield return null;
            }
        }

        // ---------------------------------------------------------------- reading and writing

        private static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>Finds a field or readable property by name on the type or any base type.</summary>
        public static MemberInfo FindMember(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name)) return null;
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, Flags | BindingFlags.DeclaredOnly);
                if (f != null) return f;
                PropertyInfo p = t.GetProperty(name, Flags | BindingFlags.DeclaredOnly);
                if (p != null && p.CanRead && p.GetIndexParameters().Length == 0) return p;
            }
            return null;
        }

        private static MemberInfo Resolve(Row row)
        {
            if (row.cachedTarget != row.target || row.cachedName != row.member)
            {
                row.cachedTarget = row.target;
                row.cachedName = row.member;
                row.cachedMember = row.target != null ? FindMember(row.target.GetType(), row.member) : null;
            }
            return row.cachedMember;
        }

        /// <summary>Reads a row. <paramref name="number"/> is NaN for values that aren't numbers.</summary>
        private static bool TryRead(Row row, out float number, out string text, out Color? swatch)
        {
            number = float.NaN;
            text = null;
            swatch = null;
            try
            {
                switch (row.source)
                {
                    case Source.Member:
                    {
                        MemberInfo m = Resolve(row);
                        if (m == null) return false;
                        object v = m is FieldInfo f ? f.GetValue(row.target) : ((PropertyInfo)m).GetValue(row.target);
                        return Describe(v, row.decimals, out number, out text, out swatch);
                    }
                    case Source.MaterialProperty:
                    {
                        Material mat = row.material;
                        if (mat == null || string.IsNullOrEmpty(row.property)) return false;
                        if (mat.HasFloat(row.property)) return Describe(mat.GetFloat(row.property), row.decimals, out number, out text, out swatch);
                        if (mat.HasInteger(row.property)) return Describe(mat.GetInteger(row.property), row.decimals, out number, out text, out swatch);
                        if (mat.HasColor(row.property)) return Describe(mat.GetColor(row.property), row.decimals, out number, out text, out swatch);
                        if (mat.HasVector(row.property)) return Describe(mat.GetVector(row.property), row.decimals, out number, out text, out swatch);
                        return false;
                    }
                    case Source.ShaderGlobal:
                        if (string.IsNullOrEmpty(row.property)) return false;
                        return Describe(Shader.GetGlobalFloat(row.property), row.decimals, out number, out text, out swatch);
                }
            }
            catch (Exception)
            {
                // A property getter threw or the target was destroyed: show the row as unavailable.
            }
            return false;
        }

        private static bool Describe(object v, int decimals, out float number, out string text, out Color? swatch)
        {
            number = float.NaN;
            swatch = null;
            string fmt = "F" + decimals;
            switch (v)
            {
                case null: text = "null"; return true;
                case bool b: number = b ? 1f : 0f; text = b ? "On" : "Off"; return true;
                case float f: number = f; text = f.ToString(fmt); return true;
                case double d: number = (float)d; text = d.ToString(fmt); return true;
                case int i: number = i; text = i.ToString(); return true;
                case long l: number = l; text = l.ToString(); return true;
                case Enum e: number = Convert.ToInt32(e); text = e.ToString(); return true;
                case Vector2 v2: text = v2.ToString(fmt); return true;
                case Vector3 v3: text = v3.ToString(fmt); return true;
                case Vector4 v4: text = v4.ToString(fmt); return true;
                case Color c: swatch = c; text = "#" + ColorUtility.ToHtmlStringRGB(c); return true;
                default: text = v.ToString(); return true;
            }
        }

        private static bool TryWrite(Row row, float value)
        {
            try
            {
                switch (row.source)
                {
                    case Source.Member:
                    {
                        MemberInfo m = Resolve(row);
                        Type type = m is FieldInfo fi ? fi.FieldType : (m as PropertyInfo)?.PropertyType;
                        if (type == null) return false;
                        object boxed;
                        if (type == typeof(float)) boxed = value;
                        else if (type == typeof(double)) boxed = (double)value;
                        else if (type == typeof(int)) boxed = Mathf.RoundToInt(value);
                        else if (type == typeof(long)) boxed = (long)Mathf.RoundToInt(value);
                        else if (type == typeof(bool)) boxed = value >= 0.5f;
                        else if (type.IsEnum) boxed = Enum.ToObject(type, Mathf.RoundToInt(value));
                        else return false;
                        if (m is FieldInfo field) { field.SetValue(row.target, boxed); return true; }
                        var prop = (PropertyInfo)m;
                        if (!prop.CanWrite) return false;
                        prop.SetValue(row.target, boxed);
                        return true;
                    }
                    case Source.MaterialProperty:
                        if (row.material == null) return false;
                        if (row.material.HasFloat(row.property)) { row.material.SetFloat(row.property, value); return true; }
                        if (row.material.HasInteger(row.property)) { row.material.SetInteger(row.property, Mathf.RoundToInt(value)); return true; }
                        return false;
                    case Source.ShaderGlobal:
                        if (string.IsNullOrEmpty(row.property)) return false;
                        Shader.SetGlobalFloat(row.property, value);
                        return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ValueOverlay] Couldn't set '{row.label}': {ex.Message}");
            }
            return false;
        }

        // ---------------------------------------------------------------- drawing

        private void Refresh()
        {
            float now = Time.realtimeSinceStartup;
            foreach (Row row in rows)
            {
                if (row.valueText == null) continue;
                if (row.labelText.text != row.label) row.labelText.text = row.label;

                if (!TryRead(row, out float number, out string text, out Color? swatch))
                {
                    row.valueText.text = "n/a";
                    row.valueText.color = new Color(textColor.r, textColor.g, textColor.b, 0.4f);
                    if (row.fill != null) row.fill.anchorMax = new Vector2(0f, 1f);
                    continue;
                }

                if (!float.IsNaN(number))
                {
                    if (!float.IsNaN(row.lastValue) && Mathf.Abs(number - row.lastValue) > 1e-5f)
                        row.changedAt = now;
                    row.lastValue = number;
                }
                float h = highlightSeconds > 0f ? 1f - Mathf.Clamp01((now - row.changedAt) / highlightSeconds) : 0f;
                row.valueText.text = text;
                row.valueText.color = Color.Lerp(textColor, accent, h);

                if (row.fill != null)
                {
                    float t = swatch.HasValue ? 1f
                        : float.IsNaN(number) ? 0f
                        : Mathf.Approximately(row.max, row.min) ? 0f
                        : Mathf.Clamp01((number - row.min) / (row.max - row.min));
                    row.fill.anchorMax = new Vector2(t, 1f);
                    row.fillImage.color = swatch ?? Color.Lerp(accent, Color.white, h * 0.35f);
                }
            }
        }

        private void Build()
        {
            Teardown();
            _dirty = false;
            _builtRows = rows.Count;
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_rounded == null) _rounded = MakeRoundedSprite();

            // A card left behind by an editor reload (no live owner) would draw twice: remove it.
            foreach (Canvas leftover in Resources.FindObjectsOfTypeAll<Canvas>())
                if (leftover != null && leftover.name == RootName && leftover.gameObject.scene.IsValid()
                    && !LiveRoots.Contains(leftover.gameObject))
                    DestroyImmediate(leftover.gameObject);

            _root = new GameObject(RootName) { hideFlags = HideFlags.HideAndDontSave };
            LiveRoots.Add(_root);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;
            _scaler = _root.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink; // scales with the short side
            float reference = 1080f / Mathf.Max(0.1f, size);
            _scaler.referenceResolution = new Vector2(reference, reference);
            if (keepInNoUICaptures) KeptCanvases.Add(_canvas);

            _card = NewRect("Card", _root.transform);
            var cardImage = _card.gameObject.AddComponent<Image>();
            cardImage.sprite = _rounded;
            cardImage.type = Image.Type.Sliced;
            cardImage.pixelsPerUnitMultiplier = 1.4f;
            cardImage.color = background;
            cardImage.raycastTarget = false;

            bool right = corner == Corner.TopRight || corner == Corner.BottomRight;
            bool top = corner == Corner.TopLeft || corner == Corner.TopRight;
            var anchor = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
            _card.anchorMin = _card.anchorMax = _card.pivot = anchor;
            _card.anchoredPosition = new Vector2(right ? -margin.x : margin.x, top ? -margin.y : margin.y);

            float y = Pad;
            if (!string.IsNullOrEmpty(title))
            {
                Text t = NewText("Title", _card, y, TitleH, TextAnchor.MiddleLeft, 19, FontStyle.Bold);
                t.text = title.ToUpperInvariant();
                t.color = new Color(textColor.r, textColor.g, textColor.b, 0.6f);
                y += TitleH;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                row.lastValue = float.NaN;
                row.labelText = NewText("Label", _card, y, LineH, TextAnchor.MiddleLeft, 22, FontStyle.Normal);
                row.labelText.color = textColor;
                row.labelText.text = row.label;
                row.valueText = NewText("Value", _card, y, LineH, TextAnchor.MiddleRight, 22, FontStyle.Bold);
                y += LineH;

                row.fill = null;
                row.fillImage = null;
                if (row.showBar)
                {
                    y += BarGap;
                    RectTransform bar = NewRect("Bar", _card);
                    PlaceRow(bar, y, BarH);
                    var barImage = bar.gameObject.AddComponent<Image>();
                    barImage.sprite = _rounded;
                    barImage.type = Image.Type.Sliced;
                    barImage.pixelsPerUnitMultiplier = 7f;
                    barImage.color = new Color(1f, 1f, 1f, 0.12f);
                    barImage.raycastTarget = false;

                    row.fill = NewRect("Fill", bar);
                    row.fill.anchorMin = Vector2.zero;
                    row.fill.anchorMax = new Vector2(0f, 1f);
                    row.fill.offsetMin = row.fill.offsetMax = Vector2.zero;
                    row.fillImage = row.fill.gameObject.AddComponent<Image>();
                    row.fillImage.sprite = _rounded;
                    row.fillImage.type = Image.Type.Sliced;
                    row.fillImage.pixelsPerUnitMultiplier = 7f;
                    row.fillImage.color = accent;
                    row.fillImage.raycastTarget = false;
                    y += BarH;
                }
                if (i < rows.Count - 1) y += RowGap;
            }
            y += Pad;
            _card.sizeDelta = new Vector2(width, y);
            Refresh();
        }

        private void Teardown()
        {
            if (_canvas != null) KeptCanvases.Remove(_canvas);
            LiveRoots.Remove(_root);
            if (_root != null)
            {
                if (Application.isPlaying) Destroy(_root);
                else DestroyImmediate(_root);
            }
            _root = null;
            _canvas = null;
            _card = null;
            foreach (Row row in rows)
            {
                row.labelText = row.valueText = null;
                row.fill = null;
                row.fillImage = null;
            }
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.HideAndDontSave };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void PlaceRow(RectTransform rt, float y, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-2f * Pad, height);
            rt.anchoredPosition = new Vector2(0f, -y);
        }

        private static Text NewText(string name, Transform parent, float y, float height, TextAnchor align, int fontSize, FontStyle style)
        {
            RectTransform rt = NewRect(name, parent);
            PlaceRow(rt, y, height);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = align;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>A white rounded square with 9-slice borders, built once instead of shipping a texture.</summary>
        private static Sprite MakeRoundedSprite()
        {
            const int size = 64, radius = 20;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - (x + 0.5f), 0f, (x + 0.5f) - (size - radius));
                float dy = Mathf.Max(radius - (y + 0.5f), 0f, (y + 0.5f) - (size - radius));
                float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
