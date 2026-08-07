using System.Numerics;
using Robust.Shared.Physics.Dynamics.Joints;

namespace Content.Shared._Floof.Rope.Systems;

public sealed partial class RopeSystem
{
    private DistanceJoint CreateDistanceJoint(EntityUid a, EntityUid b, float distance, Vector2 anchorA = default, Vector2 anchorB = default)
    {
        var id = GetEffectiveJointId(a, b);
        var joint = _joints.CreateDistanceJoint(
            a,
            b,
            anchorA,
            anchorB,
            id: id,
            minimumDistance: distance);

        joint.Length = distance;
        joint.MaxLength = distance * 1.5f;
        joint.Damping = 0.1f;
        joint.Stiffness = 10f;

        return joint;
    }

    private string GetEffectiveJointId(EntityUid a, EntityUid b)
    {
        var prefix = _net.IsServer ? "rj" : "rj-PREDICTED";
        return $"{prefix}-{a.Id}-{b.Id}";
    }
}
