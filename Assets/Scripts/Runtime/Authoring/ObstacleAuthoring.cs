using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace DotsCrowd
{
    /// <summary>Bakes a box-shaped prop into a circular obstacle the crowd and the player steer around.</summary>
    [DisallowMultipleComponent]
    public sealed class ObstacleAuthoring : MonoBehaviour
    {
        [Tooltip("Clearance added to the circle that encloses the footprint.")]
        [Min(0f)] public float Padding = 0.25f;

        sealed class Baker : Baker<ObstacleAuthoring>
        {
            public override void Bake(ObstacleAuthoring authoring)
            {
                var transform = GetComponent<Transform>();
                float3 scale = transform.lossyScale;
                float3 position = transform.position;

                AddComponent(GetEntity(TransformUsageFlags.None), new Obstacle
                {
                    Center = position.xz,
                    Radius = 0.5f * math.length(scale.xz) + authoring.Padding,
                });
            }
        }
    }
}
