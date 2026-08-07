using Content.Shared._Floof.Rope.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Floof.Rope.Components;

/// <summary>
///     Contains data about a rope - an array of entities (links) connected into a chain via distance joints.
///     The ends of a rope can be connected to different entities.
///
///     Under certain circumstances, such as when both ends of a rope are connected to entities inside the same storage,
///     the links of the rope may become temporarily disconnected from the relevant entities and sent to nullspace.
///
///     Entities of this type are always created at runtime.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(Systems.RopeSystem))]
public sealed partial class RopeComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public ProtoId<RopeConfigurationPrototype> Configuration;

    /// <summary>
    ///     Entities to which the rope is connected on the start and end, as well as the IDs of their respective joints.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public (EntityUid, string)? ConnectedStart, ConnectedEnd;

    /// <summary>
    ///     List of all links this rope is made of.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public List<Link> Links;

    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public float RopeLength, LinkLength;

    /// <summary>
    ///     True if the links of this rope have been temporarily sent to nullspace for preservation while both entities are in the same container.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public bool IsTemporarilyNullspaced;

    [Serializable, NetSerializable]
    public sealed class Link
    {
        /// <summary>
        ///     The entity that represents this link.
        /// </summary>
        public EntityUid LinkEntity;

        /// <summary>
        ///     IDs of joints that connect this link to the ones to the left and right.
        /// </summary>
        public string? LeftJoint, RightJoint;
    }
}
