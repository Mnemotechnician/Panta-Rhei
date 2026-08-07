using System.Diagnostics.CodeAnalysis;
using Content.Shared._Floof.Leash.Components;
using Content.Shared._Floof.Rope.Components;
using Content.Shared._Floof.Rope.Prototypes;
using Robust.Shared.Map;

namespace Content.Shared._Floof.Rope.Systems;

public sealed partial class RopeSystem
{
    // If the distance between two entities is x, then a joint of length AT LEAST x - tolerance can be created between them
    private float _connectionDstTolerance = 1;

    /// <summary>
    ///     Creates a rope between the two entities. Returns the rope data entity. By default, the data entity is placed on the same coordinates as the left entity.
    ///     Callers are advised to move it to an appropriate spot.
    /// </summary>
    public bool TryCreateRope(EntityUid left, EntityUid right, RopeConfigurationPrototype config, float length, [NotNullWhen(true)] out Entity<RopeComponent>? createdRope)
    {
        var leftXform = Transform(left);
        var rightXform = Transform(left);
        // Can't joint entities on different maps.
        if (leftXform.MapID != rightXform.MapID)
        {
            createdRope = null;
            return false;
        }


        if (GetEffectiveDistance(leftXform, rightXform) > length + _connectionDstTolerance)
        {
            Log.Warning($"Refusing to create a rope longer than the distance between the two entities: {ToPrettyString(left)}, {ToPrettyString(right)}");
            createdRope = null;
            return false;
        }

        // Get world positions of the two anchors
        var leftPos = _xform.GetWorldPosition(left);
        var rightPos = _xform.GetWorldPosition(right);
        var direction = (rightPos - leftPos).Normalized();
        var distance = direction.Length();

        var rope = CreateRopeEntityUninitialized(config, length, leftXform.Coordinates);
        createdRope = rope;

        // Place each link along the line
        var segmentCount = config.Segments;
        var step = distance / (segmentCount + 2);
        for (var i = 0; i < segmentCount; i++)
        {
            var pos = leftPos + (i + 1) * step * direction;
            var link = rope.Comp.Links[i];
            _xform.SetWorldPosition(link.LinkEntity, pos);
        }

        TryConnectRopeStart(rope!, left);
        TryConnectRopeEnd(rope!, right);

        // Dirtying shouldn't be necessary since the rope has just been created
        return true;
    }

    // TODO code duplication?
    public bool TryConnectRopeStart(Entity<RopeComponent?> rope, EntityUid connector)
    {
        if (!Resolve(rope, ref rope.Comp) || rope.Comp.ConnectedStart != null)
            return false; // already attached

        if (rope.Comp.Links.Count == 0)
        {
            throw new NotImplementedException(); // TODO
        }

        // Check distance
        var firstLink = rope.Comp.Links[0];
        var length = rope.Comp.LinkLength;
        var dist = GetEffectiveDistance(connector, firstLink.LinkEntity);
        if (float.IsInfinity(dist) || dist > length + _connectionDstTolerance)
            return false;

        // Create a distance joint
        var joint = CreateDistanceJoint(connector, firstLink.LinkEntity, length);
        rope.Comp.ConnectedStart = (connector, joint.ID);
        firstLink.LeftJoint = joint.ID;

        Dirty(rope, rope.Comp);
        return true;
    }

    public bool TryConnectRopeEnd(Entity<RopeComponent?> rope, EntityUid connector)
    {
        if (!Resolve(rope, ref rope.Comp) || rope.Comp.ConnectedEnd != null)
            return false; // already attached

        if (rope.Comp.Links.Count == 0)
        {
            throw new NotImplementedException(); // TODO
        }

        // Check distance
        var lastLink = rope.Comp.Links[^1];
        var length = rope.Comp.LinkLength;
        var dist = GetEffectiveDistance(connector, lastLink.LinkEntity);
        if (float.IsInfinity(dist) || dist > length + _connectionDstTolerance)
            return false;

        // Create a distance joint
        var joint = CreateDistanceJoint(connector, lastLink.LinkEntity, length);
        rope.Comp.ConnectedEnd = (connector, joint.ID);
        lastLink.RightJoint = joint.ID;

        Dirty(rope, rope.Comp);
        return true;
    }

    public bool TryDetachStart(Entity<RopeComponent?> rope)
    {
        if (!Resolve(rope, ref rope.Comp) || rope.Comp.ConnectedStart == null)
            return false;

        if (rope.Comp.Links.Count == 0)
        {
            Log.Error("Cannot detach a rope with 0 links. Delete the rope entity instead!");
            return false;
        }

        var firstLink = rope.Comp.Links[0];
        _joints.RemoveJoint(firstLink.LinkEntity, rope.Comp.ConnectedStart.Value.Item2);

        rope.Comp.ConnectedStart = null;
        firstLink.LeftJoint = null;

        Dirty(rope, rope.Comp);
        return true;
    }

    public bool TryDetachEnd(Entity<RopeComponent?> rope)
    {
        if (!Resolve(rope, ref rope.Comp) || rope.Comp.ConnectedEnd == null)
            return false;

        if (rope.Comp.Links.Count == 0)
        {
            Log.Error("Cannot detach a rope with 0 links. Delete the rope entity instead!");
            return false;
        }

        var lastLink = rope.Comp.Links[^1];
        _joints.RemoveJoint(lastLink.LinkEntity, rope.Comp.ConnectedEnd.Value.Item2);

        rope.Comp.ConnectedEnd = null;
        lastLink.RightJoint = null;

        Dirty(rope, rope.Comp);
        return true;
    }

    /// <summary>
    ///     Sets the position of the rope and all of its links to the given coordinates.
    ///     Entities will end up stacked.
    ///     Does not teleport the attached entities.
    /// </summary>
    public void SetRopePosition(Entity<RopeComponent?> rope, EntityCoordinates coords)
    {
        if (!Resolve(rope, ref rope.Comp))
            return;

        _xform.SetCoordinates(rope, coords);
    }

    /// <summary>
    ///     Creates a rope entity and all of its links at the given coordinates (stacking them in the same spot).
    /// </summary>
    public Entity<RopeComponent> CreateRopeEntityUninitialized(RopeConfigurationPrototype config, float length, EntityCoordinates coords)
    {
        var uid = Spawn(config.DataPrototype, coords);
        var rope = EnsureComp<RopeComponent>(uid);
        var segmentCount = config.Segments;

        rope.Configuration = config;
        rope.RopeLength = length;
        rope.LinkLength = segmentCount == 0 ? 0 : length / config.Segments;
        rope.IsTemporarilyNullspaced = false;

        // Spawn links
        var links = rope.Links = new();
        for (var i = 0; i < segmentCount; i++)
        {
            var linkUid = Spawn(config.LinkPrototype, coords);
            var link = new RopeComponent.Link()
            {
                LinkEntity = linkUid,
            };
            links.Add(link);
        }

        // Create joints between consecutive links
        for (var i = 1; i < segmentCount; i++)
        {
            var a = rope.Links[i - 1];
            var b = rope.Links[i];
            var joint = CreateDistanceJoint(a.LinkEntity, b.LinkEntity, rope.LinkLength);
            a.RightJoint = b.LeftJoint = joint.ID;

            // For debugging purposes only
            var leash = EnsureComp<LeashedVisualsComponent>(a.LinkEntity);
            leash.Source = a.LinkEntity;
            leash.Target = b.LinkEntity;
            leash.Sprite = config.Sprite;
        }

        return (uid, rope);
    }

    // There could NOT be a worse transform API than RobustToolbox'es
    private float GetEffectiveDistance(EntityUid a, EntityUid b) => GetEffectiveDistance(Transform(a), Transform(b));

    private float GetEffectiveDistance(TransformComponent a, TransformComponent b) =>
        a.Coordinates.TryDistance(EntityManager, _xform, b.Coordinates, out var dst)
            ? dst
            : float.PositiveInfinity;
}
