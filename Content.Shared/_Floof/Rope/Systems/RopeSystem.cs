using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;

namespace Content.Shared._Floof.Rope.Systems;

public sealed partial class RopeSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;

    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SharedJointSystem _joints = default!;

    public override void Initialize()
    {
        InitializeLifecycle();
        InitializeNetworking();
    }
}
