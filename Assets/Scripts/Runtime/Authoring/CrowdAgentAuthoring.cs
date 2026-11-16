using Unity.Entities;
using UnityEngine;

namespace DotsCrowd
{
    [DisallowMultipleComponent]
    public sealed class CrowdAgentAuthoring : MonoBehaviour
    {
        public AgentKind Kind = AgentKind.Melee;
        [Min(0.05f)] public float Radius = 0.5f;
        [Tooltip("The agent stops advancing once it is this close to the player.")]
        [Min(0f)] public float StopDistance = 2f;
        [Min(0f)] public float AttackRange = 2f;
        [Min(0.1f)] public float MaxSpeed = 3f;
        [Min(0.1f)] public float AttackCooldown = 1.2f;
        [Min(0.1f)] public float Health = 1f;

        sealed class Baker : Baker<CrowdAgentAuthoring>
        {
            public override void Bake(CrowdAgentAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.Dynamic);

                AddComponent<EnemyTag>(entity);
                AddComponent(entity, new AgentConfig
                {
                    Radius = authoring.Radius,
                    StopDistance = authoring.StopDistance,
                    AttackRange = authoring.AttackRange,
                    MaxSpeed = authoring.MaxSpeed,
                    AttackCooldown = authoring.AttackCooldown,
                    Kind = authoring.Kind,
                });
                AddComponent<AgentVelocity>(entity);
                AddComponent<SteeringState>(entity);
                AddComponent<AttackState>(entity);
                AddComponent(entity, new Health { Value = authoring.Health, Max = authoring.Health });
                AddComponent<Dying>(entity);
                SetComponentEnabled<Dying>(entity, false);
                AddComponent<AnimParams>(entity);
            }
        }
    }
}
