using Content.Shared._Floof.Ropes.Components;

namespace Content.Shared._Floof.Ropes.Systems;

public sealed partial class RopeSystem
{
    public void InitializeLifecycle()
    {
        SubscribeLocalEvent<RopeComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<RopeComponent> ent, ref ComponentShutdown args)
    {
        // On shutdown, destroy all links
        foreach (var link in ent.Comp.Links)
        {
            PredictedQueueDel(link.LinkEntity);
        }
    }
}
