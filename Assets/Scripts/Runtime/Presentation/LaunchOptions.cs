using Unity.Entities;
using UnityEngine;

namespace DotsCrowd
{
    /// <summary>Applies command-line options to the ECS world before the crowd is spawned.</summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class LaunchOptions : MonoBehaviour
    {
        void Awake()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
                return;

            if (LaunchArguments.TryGetInt("-agents", out int agents) && agents > 0)
                world.EntityManager.CreateSingleton(new CrowdCountOverride { Count = agents }, "CrowdCountOverride");
        }
    }
}
