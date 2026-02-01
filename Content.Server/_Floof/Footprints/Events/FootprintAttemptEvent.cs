namespace Content.Server._Floof.Footprints.Events;

/// <summary>
///     Raised on an entity to determine whether it can leave a footprint and when the next footprint attempt can occur.
/// </summary>
[ByRefEvent]
public struct FootprintAttemptEvent(float footprintDistance)
{
    public bool Cancelled = false;

    /// <summary>
    ///     How far an entity has to move before the next footprint attempt can occur.
    ///     Initially this is set to the default footprint distance.
    /// </summary>
    public float FootprintDistance = footprintDistance;

    public void Cancel() => Cancelled = true;

    public void ModifyDistance(float modifier) => FootprintDistance *= modifier;
}
