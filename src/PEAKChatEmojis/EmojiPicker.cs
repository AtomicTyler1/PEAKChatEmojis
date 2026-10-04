using HarmonyLib;
using PeakTextChat;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace PEAKChatEmojis
{
    public class EmojiPicker : MonoBehaviour
    {
        private const int MaxRows = 8;

        private TextChatDisplay display;
        private TMP_InputField input;
        private RectTransform panel;
        private readonly List<EmojiRow> rows = new List<EmojiRow>();
        private readonly List<string> matches = new List<string>();

        private bool dirty;
        private bool open;
        private int selected;
        private int tokenStart;
        private int tokenEnd;
        private string lastText = "";

        private static T Read<T>(object owner, string field)
        {
            return (T)AccessTools.Field(typeof(TextChatDisplay), field).GetValue(owner);
        }

        public void Init(TextChatDisplay owner)
        {
            display = owner;
            input = Read<TMP_InputField>(owner, "inputField");
            var baseTransform = Read<RectTransform>(owner, "baseTransform");
            float fontSize = Read<float>(owner, "fontSize");
            Color panelColor = input.targetGraphic != null ? input.targetGraphic.color : Read<Color>(owner, "offWhite");
            Color textColor = input.textComponent != null ? input.textComponent.color : Color.black;
            textColor.a = 1f;
            float rowHeight = fontSize * 1.3f;

            var canvas = baseTransform.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            var panelObj = new GameObject("EmojiPicker", typeof(RectTransform));
            panel = (RectTransform)panelObj.transform;
            panel.SetParent(baseTransform, false);
            panel.anchorMin = new Vector2(0f, 0f);
            panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, fontSize * 1.75f + 4f);
            panel.sizeDelta = new Vector2(-10f, 0f);

            var background = panelObj.AddComponent<ProceduralImage>();
            background.color = panelColor;
            background.SetModifierType<UniformModifier>().Radius = fontSize / 4f;

            var panelCanvas = panelObj.AddComponent<Canvas>();
            panelCanvas.overrideSorting = true;
            panelCanvas.sortingOrder = 100;
            panelObj.AddComponent<GraphicRaycaster>();

            var layout = panelObj.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(4, 4, 4, 4);

            var fitter = panelObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            for (int i = 0; i < MaxRows; i++)
            {
                rows.Add(EmojiRow.Create(this, i, panel, rowHeight, fontSize, textColor, panelColor));
            }

            panelObj.SetActive(false);

            var previous = input.onValidateInput;
            input.onValidateInput = (text, index, c) =>
            {
                if (c == '\t') return '\0';
                return previous != null ? previous(text, index, c) : c;
            };
            input.onValueChanged.AddListener(_ => dirty = true);
        }

        private static bool IsNameChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '~';
        }

        private void Refresh()
        {
            dirty = false;

            string text = input.text ?? "";
            lastText = text;
            int caret = Mathf.Clamp(input.caretPosition, 0, text.Length);

            int start = -1;
            for (int i = caret - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == ':')
                {
                    start = i;
                    break;
                }
                if (!IsNameChar(c)) break;
            }

            if (start < 0 || (start > 0 && char.IsLetterOrDigit(text[start - 1])))
            {
                Hide();
                return;
            }

            string query = text.Substring(start + 1, caret - start - 1);

            matches.Clear();
            matches.AddRange(
                Plugin.EmojiAssets.Keys
                    .Where(k => k.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(k => k.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .Take(MaxRows));

            if (matches.Count == 0)
            {
                Hide();
                return;
            }

            tokenStart = start;
            tokenEnd = caret;
            selected = 0;
            open = true;
            panel.gameObject.SetActive(true);
            UpdateRows();
        }

        private void UpdateRows()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (i < matches.Count)
                {
                    rows[i].Show(matches[i], Plugin.EmojiAssets[matches[i]], i == selected);
                }
                else
                {
                    rows[i].Hide();
                }
            }
        }

        private void Hide()
        {
            open = false;
            if (panel != null) panel.gameObject.SetActive(false);
        }

        public void Hover(int index)
        {
            if (!open || index == selected || index >= matches.Count) return;
            selected = index;
            UpdateRows();
        }

        public void Accept(int index, bool refocus)
        {
            if (!open || index < 0 || index >= matches.Count) return;

            string text = input.text ?? "";
            if (text != lastText || tokenEnd > text.Length) return;

            string tail = text.Substring(tokenEnd);
            bool spaceFollows = tail.StartsWith(" ");
            string insert = ":" + matches[index] + ":" + (spaceFollows ? "" : " ");
            int caret = tokenStart + insert.Length + (spaceFollows ? 1 : 0);

            input.SetTextWithoutNotify(text.Substring(0, tokenStart) + insert + tail);
            input.caretPosition = caret;
            lastText = input.text;
            Hide();

            if (refocus)
            {
                display.isBlockingInput = true;
                EventSystem.current.SetSelectedGameObject(input.gameObject);
                input.ActivateInputField();
            }

            StartCoroutine(SetCaretNextFrame(caret));
        }

        private IEnumerator SetCaretNextFrame(int caret)
        {
            yield return null;
            if (input != null) input.caretPosition = caret;
        }

        private void Update()
        {
            if (input == null) return;

            if (dirty) Refresh();
            if (!open) return;

            if (!display.isBlockingInput)
            {
                Hide();
                return;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                selected = (selected + 1) % matches.Count;
                UpdateRows();
            }
            else if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                selected = (selected - 1 + matches.Count) % matches.Count;
                UpdateRows();
            }
            else if (Input.GetKeyDown(KeyCode.Tab))
            {
                Accept(selected, false);
            }
        }

        private class EmojiRow : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler
        {
            private EmojiPicker picker;
            private int index;
            private Image background;
            private RawImage icon;
            private AspectRatioFitter aspect;
            private TMP_Text label;

            private Color selectedColor;

            public static EmojiRow Create(EmojiPicker owner, int index, Transform parent, float height, float fontSize, Color textColor, Color panelColor)
            {
                var obj = new GameObject("Row", typeof(RectTransform));
                obj.transform.SetParent(parent, false);

                var row = obj.AddComponent<EmojiRow>();
                row.picker = owner;
                row.index = index;
                row.selectedColor = new Color(panelColor.r * 0.82f, panelColor.g * 0.82f, panelColor.b * 0.82f, panelColor.a);
                var rowImage = obj.AddComponent<ProceduralImage>();
                rowImage.SetModifierType<UniformModifier>().Radius = fontSize / 4f;
                row.background = rowImage;
                row.background.color = Color.clear;

                var element = obj.AddComponent<LayoutElement>();
                element.minHeight = height;
                element.preferredHeight = height;

                float iconWidth = height - 4f;

                var cell = new GameObject("IconCell", typeof(RectTransform));
                cell.transform.SetParent(obj.transform, false);
                var cellRect = (RectTransform)cell.transform;
                cellRect.anchorMin = new Vector2(0f, 0f);
                cellRect.anchorMax = new Vector2(0f, 1f);
                cellRect.pivot = new Vector2(0f, 0.5f);
                cellRect.offsetMin = new Vector2(6f, 2f);
                cellRect.offsetMax = new Vector2(6f + iconWidth, -2f);

                var iconObj = new GameObject("Icon", typeof(RectTransform));
                iconObj.transform.SetParent(cell.transform, false);
                row.icon = iconObj.AddComponent<RawImage>();
                row.icon.raycastTarget = false;
                row.aspect = iconObj.AddComponent<AspectRatioFitter>();
                row.aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

                var labelObj = new GameObject("Label", typeof(RectTransform));
                labelObj.transform.SetParent(obj.transform, false);
                var text = labelObj.AddComponent<TextMeshProUGUI>();

                if (GUIManagerPatch.darumaDropOneFont != null)
                {
                    text.font = GUIManagerPatch.darumaDropOneFont;
                }

                text.fontSize = fontSize * 0.8f;
                text.color = textColor;
                text.raycastTarget = false;
                text.horizontalAlignment = HorizontalAlignmentOptions.Left;
                text.verticalAlignment = VerticalAlignmentOptions.Middle;
                text.textWrappingMode = TextWrappingModes.Normal;
                text.overflowMode = TextOverflowModes.Ellipsis;
                var labelRect = (RectTransform)labelObj.transform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.offsetMin = new Vector2(6f + iconWidth + 10f, 0f);
                labelRect.offsetMax = new Vector2(-6f, 0f);
                row.label = text;

                return row;
            }

            public void Show(string name, TMP_SpriteAsset asset, bool highlighted)
            {
                gameObject.SetActive(true);
                var texture = asset.spriteSheet;
                icon.texture = texture;
                if (texture != null && texture.height > 0)
                {
                    aspect.aspectRatio = (float)texture.width / texture.height;
                }
                label.text = ":" + name + ":";
                label.ForceMeshUpdate();
                background.color = highlighted ? selectedColor : Color.clear;
            }

            public void Hide()
            {
                gameObject.SetActive(false);
            }

            public void OnPointerDown(PointerEventData eventData)
            {
                picker.Accept(index, true);
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                picker.Hover(index);
            }
        }
    }
}