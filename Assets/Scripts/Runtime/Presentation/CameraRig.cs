using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DotsCrowd
{
    /// <summary>
    /// Top-down follow camera with wheel zoom. Reads the player position from the frame context, which
    /// is only written on the main thread, so following never forces a sync on crowd jobs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField, Min(1f)] float distance = 46f;
        [SerializeField, Min(1f)] float minDistance = 14f;
        [SerializeField, Min(1f)] float maxDistance = 260f;
        [SerializeField, Range(20f, 89f)] float pitch = 56f;
        [SerializeField, Min(0f)] float smoothTime = 0.12f;
        [SerializeField, Range(0.01f, 0.5f)] float zoomStep = 0.12f;

        World m_World;
        EntityQuery m_Context;
        Vector3 m_Velocity;

        public float Distance
        {
            get => distance;
            set => distance = Mathf.Clamp(value, minDistance, maxDistance);
        }

        void LateUpdate()
        {
            if (!TryGetFocus(out var focus))
                return;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    Distance = distance * (1f - Mathf.Sign(scroll) * zoomStep);
            }

            var rotation = Quaternion.Euler(pitch, 0f, 0f);
            var target = focus - rotation * Vector3.forward * distance;
            transform.SetPositionAndRotation(Vector3.SmoothDamp(transform.position, target, ref m_Velocity, smoothTime), rotation);
        }

        bool TryGetFocus(out Vector3 focus)
        {
            focus = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return false;

            if (world != m_World)
            {
                m_World = world;
                m_Context = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<FrameContext>());
            }

            if (m_Context.IsEmptyIgnoreFilter)
                return false;

            focus = m_Context.GetSingleton<FrameContext>().PlayerPosition;
            return true;
        }
    }
}
