using Content.Shared._Floof.Ropes.Components;
using Robust.Shared.Physics;

namespace Content.Shared._Floof.Ropes.Systems;

public sealed partial class RopeSystem
{
    public void InitializeLifecycle()
    {
        SubscribeLocalEvent<RopeComponent, ComponentShutdown>(OnShutdown);

        SubscribeLocalEvent<RopeLinkComponent, JointRemovedEvent>(OnJointRemoved);
    }

    private void OnShutdown(Entity<RopeComponent> ent, ref ComponentShutdown args)
    {
        // On shutdown, destroy all links
        foreach (var link in ent.Comp.Links)
        {
            // Client can have these set to EntityUid.Invalid during network sync
            if (link.LinkEntity.Valid)
                PredictedQueueDel(link.LinkEntity);
        }
    }

    private void OnJointRemoved(Entity<RopeLinkComponent> link, ref JointRemovedEvent args)
    {


        Log.Debug($"Joint {args.Joint.ID} is removed.");
    }
}
