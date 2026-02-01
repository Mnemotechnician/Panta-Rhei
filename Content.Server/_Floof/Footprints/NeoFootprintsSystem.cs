using Content.Server._Floof.Footprints.Events;
using Robust.Shared.Map;

namespace Content.Server._Floof.Footprints;

/// <summary>
///     A version of footprints inspired by EE but with less shitcode and licensed under MIT.
/// </summary>
public sealed class NeoFootprintsSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    public override void Initialize()
    {
    }

    public override void Update(float frameTime)
    {
        return; // TODO

        var query = EntityQueryEnumerator<NeoFootprintsComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var footprints, out var xform))
        {
            // Don't leave footprints when not parented to a grid or moving between grids/maps
            if (xform.GridUid != footprints.LastStepPos.EntityId
                || xform.ParentUid != xform.GridUid
                || !xform.Coordinates.TryDelta(EntityManager, _xform, footprints.LastStepPos, out var delta))
            {
                footprints.LastStepPos = xform.Coordinates;
                continue;
            }

            if (delta.LengthSquared() < footprints.NextStepDistance)
                continue;

            // If the entity can't step right now, skip this tick
            var oldDistance = footprints.NextStepDistance;
            if (!CanStep((uid, footprints), out footprints.NextStepDistance))
                continue;

            // Edge case: if nextStepDistance was uninitialized (-inf), skip this tick as it was just computed
            if (float.IsNegativeInfinity(oldDistance))
            {
                footprints.LastStepPos = xform.Coordinates;
                continue;
            }

            // The entity can step and has travelled far enough.
            DoStep((uid, footprints, xform));
        }
    }

    /// <summary>
    ///     Check whether an entity can make a step, and how far away it must be from the last step location to make it.
    /// </summary>
    public bool CanStep(Entity<NeoFootprintsComponent?> ent, out float nextStepDistance)
    {
        if (!Resolve(ent, ref ent.Comp))
        {
            nextStepDistance = float.NegativeInfinity;
            return false;
        }

        var ev = new FootprintAttemptEvent(ent.Comp.StepDistance);
        RaiseLocalEvent(ent, ref ev);

        nextStepDistance = ev.FootprintDistance;
        return !ev.Cancelled;
    }

    /// <summary>
    ///     Forces the entity to make a footstep right now.
    /// </summary>
    public void DoStep(Entity<NeoFootprintsComponent, TransformComponent> ent, bool changeLegs = true)
    {
        var ev = new GetFootprintDataEvent(ent, ent.Comp1.FootprintScale);
        RaiseLocalEvent(ent, ref ev);

    }
}
