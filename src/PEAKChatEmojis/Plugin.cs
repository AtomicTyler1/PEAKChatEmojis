using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using PeakTextChat;

namespace PEAKChatEmojis
{
    [BepInAutoPlugin]
    [BepInDependency("com.borealityy.peaktextchat", BepInDependency.DependencyFlags.HardDependency)]
    public partial class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; } = null!;

        internal static readonly Dictionary<string, TMP_SpriteAsset> EmojiAssets = new Dictionary<string, TMP_SpriteAsset>();
        internal static TMP_SpriteAsset PrimarySpriteAsset;

        private const float EmojiSize = 1f;
        private const float BaselineRatio = 0.8f;

        private void Awake()
        {
            Log = Logger;

            foreach (var folder in Directory.GetDirectories(Paths.PluginPath, "Emojis", SearchOption.AllDirectories))
            {
                foreach (var file in Directory.GetFiles(folder))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                    {
                        Register(file);
                    }
                }
            }

            if (EmojiAssets.Count > 0)
            {
                PrimarySpriteAsset = EmojiAssets.Values.First();
                PrimarySpriteAsset.fallbackSpriteAssets = EmojiAssets.Values.Skip(1).ToList();
            }

            new Harmony(Id).PatchAll();
            Log.LogInfo($"Loaded {EmojiAssets.Count} emojis");
        }

        private static void Register(string path)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (EmojiAssets.ContainsKey(name))
                {
                    string oldName = name;
                    int suffix = 1;
                    while (EmojiAssets.ContainsKey(name + "~" + suffix))
                    {
                        suffix++;
                    }
                    name += "~" + suffix;
                    Log.LogWarning($"Duplicate emoji name {oldName} in {path}. Renamed to {name}");
                }

                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path)))
                {
                    Log.LogError($"Failed to load {path}");
                    return;
                }

                texture.filterMode = FilterMode.Trilinear;
                texture.wrapMode = TextureWrapMode.Clamp;

                var shader = Shader.Find("TextMeshPro/Sprite") ?? Shader.Find("UI/Default");
                var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
                asset.name = "Emoji_" + name;
                asset.spriteSheet = texture;
                asset.material = new Material(shader) { mainTexture = texture };

                asset.spriteInfoList = new List<TMP_Sprite>
                {
                    new TMP_Sprite
                    {
                        id = 0,
                        name = name,
                        hashCode = TMP_TextUtilities.GetSimpleHashCode(name),
                        x = 0,
                        y = 0,
                        width = texture.width,
                        height = texture.height,
                        xOffset = 0,
                        yOffset = texture.height * BaselineRatio,
                        xAdvance = texture.width,
                        scale = EmojiSize,
                        pivot = new Vector2(0f, 0f)
                    }
                };

                asset.UpdateLookupTables();
                EmojiAssets.Add(name, asset);
            }
            catch (Exception ex)
            {
                Log.LogError($"Error registering {path}: {ex}");
            }
        }
    }

    [HarmonyPatch]
    public static class ChatPatches
    {
        private static readonly Regex EmojiIdentifier = new Regex(@":([a-zA-Z0-9_\-~]+):", RegexOptions.Compiled);
        private static readonly Regex SpriteTag = new Regex("<sprite name=\"[^\"]*\">", RegexOptions.Compiled);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TextChatDisplay), nameof(TextChatDisplay.Start))]
        public static void StartPostfix(TextChatDisplay __instance)
        {
            if (Plugin.EmojiAssets.Count == 0) return;
            if (__instance.usingIMGUI) return;
            __instance.gameObject.AddComponent<EmojiPicker>().Init(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TextChatDisplay), nameof(TextChatDisplay.CreateText))]
        public static void CreateTextPostfix(TMP_Text __result)
        {
            if (__result != null && Plugin.PrimarySpriteAsset != null)
            {
                __result.spriteAsset = Plugin.PrimarySpriteAsset;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TextChatDisplay), nameof(TextChatDisplay.AddMessage), typeof(string))]
        public static void AddMessagePrefix(ref string message)
        {
            if (string.IsNullOrEmpty(message) || Plugin.PrimarySpriteAsset == null) return;

            try
            {
                int bodyStartIndex = 0;

                int matchIndex = message.IndexOf("]</color>: ");
                if (matchIndex != -1)
                {
                    bodyStartIndex = matchIndex + "]</color>: ".Length;
                }
                else if ((matchIndex = message.IndexOf("</color>: ")) != -1)
                {
                    bodyStartIndex = matchIndex + "</color>: ".Length;
                }
                else if ((matchIndex = message.IndexOf("]: ")) != -1)
                {
                    bodyStartIndex = matchIndex + "]: ".Length;
                }

                string header = message.Substring(0, bodyStartIndex);
                string body = message.Substring(bodyStartIndex);

                bool containsValidEmoji = false;
                string processedBody = EmojiIdentifier.Replace(body, m =>
                {
                    if (Plugin.EmojiAssets.ContainsKey(m.Groups[1].Value))
                    {
                        containsValidEmoji = true;
                        return $"<sprite name=\"{m.Groups[1].Value}\">";
                    }
                    return m.Value;
                });

                if (containsValidEmoji)
                {
                    string bodyWithoutSprites = SpriteTag.Replace(processedBody, string.Empty);
                    if (string.IsNullOrWhiteSpace(bodyWithoutSprites))
                    {
                        processedBody = $"<size=\"40\">{processedBody}</size>";
                    }
                }

                message = header + processedBody;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Error formatting chat message in AddMessagePrefix: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TextChatDisplay), nameof(TextChatDisplay.AddMessage), typeof(string))]
        public static void AddMessagePostfix(TextChatDisplay __instance)
        {
            try
            {
                var viewport = __instance.chatLogViewportTransform;
                if (viewport == null || viewport.childCount == 0) return;

                var rect = viewport.GetChild(viewport.childCount - 1) as RectTransform;
                var text = rect != null ? rect.GetComponent<TMP_Text>() : null;
                if (text == null || !text.text.Contains("<sprite")) return;

                var box = __instance.boxSize;
                string flat = SpriteTag.Replace(text.text, "W");
                float height = text.GetPreferredValues(flat, box.x - 24f, 1000f).y;
                rect.sizeDelta = new Vector2(0f, height);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(ex);
            }
        }
    }
}