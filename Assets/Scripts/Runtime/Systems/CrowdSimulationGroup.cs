using Unity.Entities;
using Unity.Transforms;

namespace DotsCrowd
{
    /// <summary>
    /// All crowd gameplay runs here, before the transform hierarchy is resolved, so the frame that
    /// moves an agent is the frame that renders it.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class CrowdSimulationGroup : ComponentSystemGroup
    {
    }

    /// <summary>Creates the singletons every crowd system relies on.</summary>
    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial struct CrowdBootstrapSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.EntityManager.CreateSingleton(new FrameContext(), "FrameContext");
            state.EntityManager.CreateSingleton(new CrowdStats(), "CrowdStats");
            state.Enabled = false;
        }
    }
}
