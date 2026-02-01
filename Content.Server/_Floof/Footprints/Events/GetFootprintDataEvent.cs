namespace Content.Server._Floof.Footprints.Events;

/// <summary>
///     Raised on an entity when it's certain it should make a step in order to collect footprint data.
/// </summary>
[ByRefEvent]
public struct GetFootprintDataEvent(Entity<NeoFootprintsComponent> subject, float initialScale)
{
    public readonly Entity<NeoFootprintsComponent> Subject = subject;

    /// <summary>
    ///     The sprite state to use for the footprint. Must be part of the base footprint RSI of the subject.
    /// </summary>
    public string? FootprintSpriteState;
    public float SpriteScale = initialScale;

    public bool Handled = false;
}
