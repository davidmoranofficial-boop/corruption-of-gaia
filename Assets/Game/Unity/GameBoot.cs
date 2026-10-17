using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// Builds the whole game when you press Play, in whatever scene is open.
    /// No scene setup or prefabs needed.
    /// </summary>
    public static class GameBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<GameUI>() != null) return;

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = "Loran Panel";
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            var theme = Resources.Load<ThemeStyleSheet>("UI/LoranTheme");
            if (theme != null) panel.themeStyleSheet = theme;

            var go = new GameObject("Loran Game");
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            go.AddComponent<GameUI>();
            go.SetActive(true);
            Object.DontDestroyOnLoad(go);
        }
    }

    /// <summary>Saves and loads with Unity's built-in JSON.</summary>
    public class UnityJson : IJson
    {
        public string Write(object o) => JsonUtility.ToJson(o);
        public T Read<T>(string json) => JsonUtility.FromJson<T>(json);
    }
}
