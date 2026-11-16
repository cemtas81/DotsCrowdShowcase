using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace DotsCrowd
{
    /// <summary>
    /// Main-thread bridge from the Input System and the camera into ECS. Reads devices once per frame
    /// and writes <see cref="PlayerInput"/> and the camera position; everything downstream is Burst code.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class PlayerInputSystem : SystemBase
    {
        const float AutoAimTurnRate = 1.1f;

        float m_AutoAimAngle;

        /// <summary>Hands-free demo mode: fire continuously while the aim sweeps around the player.</summary>
        public bool AutoFire { get; set; }

        protected override void OnCreate()
        {
            RequireForUpdate<PlayerTag>();
            RequireForUpdate<FrameContext>();
        }

        protected override void OnUpdate()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var camera = Camera.main;

            float2 move = float2.zero;
            bool fire = false;
            if (keyboard != null)
            {
                move.x = Held(keyboard.dKey, keyboard.rightArrowKey) - Held(keyboard.aKey, keyboard.leftArrowKey);
                move.y = Held(keyboard.wKey, keyboard.upArrowKey) - Held(keyboard.sKey, keyboard.downArrowKey);
                fire = keyboard.spaceKey.isPressed;
                if (keyboard.tKey.wasPressedThisFrame)
                    AutoFire = !AutoFire;
            }

            bool hasAim = false;
            float3 aim = float3.zero;
            if (mouse != null)
            {
                fire |= mouse.leftButton.isPressed;
                if (camera != null && TryGroundPoint(camera.ScreenPointToRay(mouse.position.ReadValue()), out aim))
                    hasAim = true;
            }

            m_AutoAimAngle += SystemAPI.Time.DeltaTime * AutoAimTurnRate;

            foreach (var (input, transform) in SystemAPI.Query<RefRW<PlayerInput>, RefRO<LocalTransform>>().WithAll<PlayerTag>())
            {
                float3 origin = transform.ValueRO.Position;
                if (AutoFire)
                {
                    math.sincos(m_AutoAimAngle, out float sin, out float cos);
                    aim = origin + new float3(sin, 0f, cos) * 10f;
                    hasAim = true;
                    fire = true;
                }

                input.ValueRW = new PlayerInput
                {
                    Move = move,
                    AimPoint = hasAim ? aim : input.ValueRO.AimPoint,
                    Fire = fire,
                };
            }

            if (camera != null)
                SystemAPI.GetSingletonRW<FrameContext>().ValueRW.CameraPosition = camera.transform.position;
        }

        static float Held(KeyControl primary, KeyControl secondary)
        {
            return primary.isPressed || secondary.isPressed ? 1f : 0f;
        }

        static bool TryGroundPoint(Ray ray, out float3 point)
        {
            point = float3.zero;
            if (math.abs(ray.direction.y) < 1e-4f)
                return false;

            float distance = -ray.origin.y / ray.direction.y;
            if (distance <= 0f)
                return false;

            point = (float3)(ray.origin + ray.direction * distance);
            return true;
        }
    }
}
