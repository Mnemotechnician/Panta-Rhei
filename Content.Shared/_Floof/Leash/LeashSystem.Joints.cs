using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._Floof.Leash.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Dynamics.Joints;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Shared._Floof.Leash;

public sealed partial class LeashSystem
{
    [Dependency] private readonly SharedJointSystem _joints = default!;

    public static readonly string LeashJointIdPrefix = "leash-joint-";

    private List<(Entity<LeashComponent>, Entity<LeashedComponent>, Entity<LeashAnchorComponent>)> _pendingJointUpdates = new();

    private void InitializeJoints()
    {
        SubscribeLocalEvent<LeashedComponent, JointAddedEvent>(OnJointAdded);
        SubscribeLocalEvent<LeashedComponent, JointRemovedEvent>(OnJointRemoved, after: [typeof(SharedJointSystem)]);
    }

    private void OnJointAdded(Entity<LeashedComponent> ent, ref JointAddedEvent args)
    {
        // If we're on the client side, set the leash length to infinity to avoid predicting the leash
        if (_net.IsClient && args.Joint.ID.StartsWith(LeashJointIdPrefix) && args.Joint is DistanceJoint dj)
            dj.MaxLength = float.MaxValue;
    }

    private void OnJointRemoved(Entity<LeashedComponent> ent, ref JointRemovedEvent args)
    {
        // JointRemoved is called on both bodies, we only do this kinda check on the leashed
        var id = args.Joint.ID;
        if (_net.IsClient
            || ent.Comp.LifeStage >= ComponentLifeStage.Removing
            || GetEntity(ent.Comp.Leash) is not { } leashEnt
            || GetEntity(ent.Comp.Anchor) is not { } anchorEnt
            || TerminatingOrDeleted(leashEnt)
            || !TryComp<LeashAnchorComponent>(anchorEnt, out var anchor)
            || !TryComp<LeashComponent>(leashEnt, out var leash))
            return;

        _pendingJointUpdates.Add(((leashEnt, leash), ent, (anchorEnt, anchor)));
    }

    private void RefreshRelays(Entity<LeashComponent, TransformComponent> leash)
    {
        if (!ShouldPredictLeashes())
            return;

        // Server - ensure the holder of the leash is always correct
        // I do not know why, perhaps because RobustToolbox joint tooling is shitty,
        // but if the leash is inside a container that is inside another container (e.g. person inside a locker),
        // and then the middle container leaves the outer (person leaves the locker),
        // RobustToolbox won't update the joint between the leashed person and the leash (which should be relayed to the outer container - locker).
        // This means the person will stay attached to the outer container (locker).
        // To fix this, we force RT to update the joint relay
        if (TryComp<JointComponent>(leash, out var leashJointComp)
            && _container.TryGetOuterContainer(leash, leash.Comp2, out var jointRelayTarget)
            && leashJointComp.Relay != null
            && leashJointComp.Relay != jointRelayTarget.Owner)
            _joints.RefreshRelay(leash);

        // Also do the same for all leashed entities
        foreach (var data in leash.Comp1.Leashed)
        {
            if (!TryGetEntity(data.Pulled, out var pulled) || !TryComp<LeashedComponent>(pulled, out var leashed))
                continue;

            if (TryComp<JointComponent>(pulled, out var jointComp)
                && _container.TryGetOuterContainer(pulled.Value, Transform(pulled.Value), out jointRelayTarget)
                && jointComp.Relay != null
                && jointComp.Relay != jointRelayTarget.Owner)
                _joints.RefreshRelay(pulled.Value);
        }
    }

    private void ProcessPendingJointUpdate(Entity<LeashComponent> leash,
        Entity<LeashedComponent> leashed,
        Entity<LeashAnchorComponent> anchor)
    {
        var canRestore = !TerminatingOrDeleted(leash) && !TerminatingOrDeleted(leashed) &&
                         !TerminatingOrDeleted(anchor);
        if (canRestore)
        {
            var leashXform = Transform(leash);
            var leashedXform = Transform(leashed);
            canRestore &= leashXform.MapUid == leashedXform.MapUid
                          && leashXform.Coordinates.TryDistance(EntityManager, leashedXform.Coordinates, out var dst)
                          && dst <= leash.Comp.MaxDistance;
            // The anchor must be either the entity itself or something parented to them (clothing)
            canRestore &= anchor.Owner == leashed.Owner || _xform.ContainsEntity(leashed, anchor.Owner);
        }

        RemoveLeash(leashed!, leash!, false);
        if (canRestore)
            DoLeash(anchor, leash, leashed, true);
    }

    /// <summary>
    ///     Returns true if a leash joint can be created between the two specified entities.
    ///     This will return false if one of the entities is a parent of another, or if the entities are on different maps.
    /// </summary>
    public bool CanCreateJoint(EntityUid a, EntityUid b)
    {
        BaseContainer? aOuter = null, bOuter = null;

        // Unless the entities are inside the same container, it should be safe to create a joint
        var aXform = Transform(a);
        var bXform = Transform(b);

        if (aXform.MapUid != bXform.MapUid)
            return false;

        if (!_container.TryGetOuterContainer(a, aXform, out aOuter)
            && !_container.TryGetOuterContainer(b, bXform, out bOuter))
            return true;

        // Otherwise, we need to make sure that neither of the entities contain the other, and that they are not in the same container.
        return a != bOuter?.Owner && b != aOuter?.Owner && aOuter?.Owner != bOuter?.Owner;
    }

    private List<LeashComponent.LeashLinkData> CreateLeashJoint(string jointIdBase, Entity<LeashComponent> leash, EntityUid leashTarget)
    {
        // Client cant predict for shit
        if (_net.IsClient)
            return new List<LeashComponent.LeashLinkData>()
            {
                new()
                {
                    Start = GetNetEntity(leash),
                    End = GetNetEntity(leashTarget),
                }
            };

        var mapPosA = Transform(leash).Coordinates.ToMap(EntityManager, _xform);
        var mapPosB = Transform(leashTarget).Coordinates.ToMap(EntityManager, _xform);
        var mapId = mapPosA.MapId;

        // Spawn each link entity at an interpolated position
        var numberOfEntities = 10;
        var linkEntities = new EntityUid[10 + 2];
        linkEntities[0] = leash;
        linkEntities[linkEntities.Length - 1] = leashTarget;
        for (int i = 0; i < numberOfEntities; i++)
        {
            float t = (i + 1) / (float)(numberOfEntities + 2);
            var interpolated = Vector2.Lerp(mapPosA.Position, mapPosB.Position, t);
            var spawnCoords = new MapCoordinates(interpolated, mapId);
            var link = Spawn("LeashLink", spawnCoords);

            linkEntities[i + 1] = link;
        }

        // Link first entitye to the leash, last entity to the target, and interlink everything else
        var links = new List<LeashComponent.LeashLinkData>(numberOfEntities + 1);
        for (int i = 1; i < linkEntities.Length; i++)
        {
            var a = linkEntities[i - 1];
            var b = linkEntities[i];
            links.Add(ActuallyCreateJoint(a, b, i));
        }

        return links;

        LeashComponent.LeashLinkData ActuallyCreateJoint(EntityUid a, EntityUid b, int suffix)
        {
            var data = new LeashComponent.LeashLinkData();
            data.Start = GetNetEntity(a);
            data.End = GetNetEntity(b);

            var joint = _joints.CreateDistanceJoint(a, b, id: jointIdBase + "-" + suffix);
            joint.MinLength = 0f;
            joint.MaxLength = 100; // Will be updated in the update loop and shortened if possible. If not, we want to avoid pulling to too close anyway.
            joint.Stiffness = 0f;
            joint.CollideConnected = true; // This is just for performance reasons and doesn't actually make mobs collide.
            joint.Damping = 0f;
            data.JointId = joint.ID;

            _container.EnsureContainer<ContainerSlot>(a, LeashedComponent.VisualsContainerName);
            if (leash.Comp.LeashSprite is not null && EntityManager.TrySpawnInContainer(null, a, LeashedComponent.VisualsContainerName, out var visualEntity))
            {
                var visualComp = EnsureComp<LeashedVisualsComponent>(visualEntity.Value);
                visualComp.Sprite = leash.Comp.LeashSprite;
                visualComp.Source = a;
                visualComp.Target = b;

                if (TryComp<LeashAnchorComponent>(leashTarget, out var anchor))
                    visualComp.OffsetTarget = anchor.Offset;

                data.LeashVisuals = GetNetEntity(visualEntity);
            }

            return data;
        }
    }

    private void DestroyJoint(Entity<LeashComponent> leash, LeashComponent.LeashData data, Entity<LeashedComponent> leashed)
    {
        // All links except the fist one have their start connected to another link
        // Still we perform extra checks just in case
        var leashNetEnt = GetNetEntity(leash);
        for (var i = 1; i < data.Links.Count; i++)
        {
            var linkData = data.Links[i];
            if (linkData.Start == leashNetEnt)
                continue;

            QueueDel(GetEntity(linkData.Start));
        }

        // The leash only needs removing the leash visuals
        if (_container.TryGetContainer(leash, LeashedComponent.VisualsContainerName, out var visualsCont))
            _container.CleanContainer(visualsCont);

        data.Links.Clear();
    }

    /// <summary>
    ///     Refreshes all joints for the specified leash.
    ///     This will remove all obsolete joints, such as those for which CanCreateJoint returns false,
    ///     and re-add all joints that were previously removed for the same reason, but became valid later.
    /// </summary>
    public void RefreshJoints(Entity<LeashComponent> leash)
    {
        foreach (var data in leash.Comp.Leashed)
        {
            if (!TryGetEntity(data.Pulled, out var pulled) || !TryComp<LeashedComponent>(pulled, out var leashedComp))
                continue;

            RefreshJoint(leash, data, (pulled.Value, leashedComp));
        }
    }

    /// <seealso cref="RefreshJoints"/>
    private void RefreshJoint(Entity<LeashComponent> leash, LeashComponent.LeashData data, Entity<LeashedComponent> leashed)
    {
        var shouldExist = CanCreateJoint(leashed, leash);
        var exists = data.Links.Count > 0;

        if (exists && !shouldExist)
        {
            DisableJointFor(leash, data, leashed);
            Log.Debug($"Removed obsolete leash joint between {leash.Owner} and {leashed.Owner}");
        }
        else if (!exists && shouldExist)
        {
            EnableJointFor(leash, data, leashed);
            Log.Debug($"Added new leash joint between {leash.Owner} and {leashed.Owner}");
        }
    }

    /// <summary>
    ///     Enables a previously disabled leash joint.
    /// </summary>
    private void EnableJointFor(Entity<LeashComponent> leash, LeashComponent.LeashData data, Entity<LeashedComponent> leashed)
    {
        var jointId = $"${LeashJointIdPrefix}{data.Pulled}";
        var joint = CreateLeashJoint(jointId, leash, leashed);

        data.Links = joint;
    }

    /// <summary>
    ///     Disables the leash joint by destroying the underlying leash joints and components.
    /// </summary>
    private void DisableJointFor(Entity<LeashComponent> leash, LeashComponent.LeashData data, Entity<LeashedComponent> leashed)
    {
        foreach (var link in data.Links)
        {
            if (GetEntity(link.Start) is {} start)
                _joints.RemoveJoint(start, link.JointId!);
        }

        data.Links.Clear();
    }
}
