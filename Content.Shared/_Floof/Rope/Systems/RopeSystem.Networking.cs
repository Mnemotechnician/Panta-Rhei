using Content.Shared._Floof.Rope.Components;
using Robust.Shared.GameStates;

namespace Content.Shared._Floof.Rope.Systems;

public sealed partial class RopeSystem
{
    private void InitializeNetworking()
    {
        SubscribeLocalEvent<RopeComponent, ComponentGetState>(OnRopeGetState);
        SubscribeLocalEvent<RopeComponent, ComponentHandleState>(OnRopeHandleState);
    }

    private void OnRopeGetState(Entity<RopeComponent> ent, ref ComponentGetState args)
    {
        args.State = new RopeComponent.State(ent.Comp, EntityManager);
    }

    private void OnRopeHandleState(Entity<RopeComponent> ent, ref ComponentHandleState args)
    {
        if (args.Next is not RopeComponent.State { } state)
            return;

        state.Apply(ent.Comp, EntityManager);
    }
}
