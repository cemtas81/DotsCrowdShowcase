using System.Text;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace DotsCrowd
{
    /// <summary>
    /// Minimal on-screen readout: crowd size, combat counters and frame time. Built from uGUI at runtime and
    /// refreshed four times a second, so it costs nothing on the frames in between.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StatsOverlay : MonoBehaviour
    {
        const float RefreshInterval = 0.25f;

        [SerializeField, Range(0.01f, 1f)] float frameSmoothing = 0.05f;
        [SerializeField, Min(8)] int fontSize = 16;

        readonly StringBuilder m_Builder = new StringBuilder(256);
        World m_World;
        EntityQuery m_Stats;
        Text m_Text;
        float m_SmoothedDelta = 1f / 60f;
        float m_NextRefresh;

        void Start()
        {
            var canvasObject = new GameObject("StatsCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(canvasObject.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(12f, -12f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            var fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(12, 12, 8, 8);

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(panel.transform, false);
            m_Text = label.GetComponent<Text>();
            m_Text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_Text.fontSize = fontSize;
            m_Text.color = new Color(0.92f, 0.92f, 0.92f);
            m_Text.raycastTarget = false;
        }

        void Update()
        {
            m_SmoothedDelta = Mathf.Lerp(m_SmoothedDelta, Time.unscaledDeltaTime, frameSmoothing);

            if (m_Text == null || Time.unscaledTime < m_NextRefresh)
                return;

            m_NextRefresh = Time.unscaledTime + RefreshInterval;
            if (TryGetStats(out var stats))
                m_Text.text = Format(stats);
        }

        string Format(in CrowdStats stats)
        {
            float milliseconds = m_SmoothedDelta * 1000f;
            m_Builder.Clear()
                .Append("Agents   ").Append(stats.Agents.ToString("N0")).Append("    alive ").Append(stats.Alive.ToString("N0")).Append('\n')
                .Append("Kills    ").Append(stats.Kills.ToString("N0")).Append("    hits taken ").Append(stats.PlayerHits.ToString("N0")).Append('\n')
                .Append("Frame    ").Append(milliseconds.ToString("0.0")).Append(" ms   ")
                .Append((1000f / Mathf.Max(milliseconds, 0.01f)).ToString("0")).Append(" fps\n\n")
                .Append("WASD move · mouse aim · LMB / Space fire\nWheel zoom · T auto-fire");
            return m_Builder.ToString();
        }

        bool TryGetStats(out CrowdStats stats)
        {
            stats = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            if (world != m_World)
            {
                m_World = world;
                m_Stats = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CrowdStats>());
            }

            if (m_Stats.IsEmptyIgnoreFilter)
                return false;

            stats = m_Stats.GetSingleton<CrowdStats>();
            return true;
        }
    }
}
