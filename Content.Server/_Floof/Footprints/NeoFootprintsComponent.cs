using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Floof.Footprints;

/// <summary>
///     A version of footprints inspired by EE but with less shitcode and licensed under MIT.
/// </summary>
[RegisterComponent]
public sealed partial class NeoFootprintsComponent : Component
{
    [DataField]
    public EntProtoId FootprintPrototype = new("Footstep");

    /// <summary>
    ///     ID of the solution in which reagents that are yet to be spread across footprints are stored.
    ///     The entity will leave footprints as long as this solution is not empty.
    /// </summary>
    [DataField]
    public string FootprintSolution = "footprints";

    /// <summary>
    ///     Distance that must be travelled from LastStepPos before another footprint can be left.
    /// </summary>
    [DataField]
    public float StepDistance = 1.2f;

    /// <summary>
    ///     How far away to the left or right from the entity footprints should appear.
    ///     This assumes the entity has 2 legs, but can be set to 0 to disable.
    /// </summary>
    [DataField]
    public float StepOffsetSide = 0.25f;

    [DataField]
    public float FootprintScale = 1.5f; // Humanoids normally have a radius of 0.45, so this equates to ~0.7 for them.

    /// <summary>
    ///     Whether StepDistance, StepOffsetSide, FootprintScale should scale with the size of the entity.
    ///     If true, then all of those will be multiplied by the radius of entity's first hard collision fixture.
    /// </summary>
    [DataField]
    public bool ScaleWithEntitySize = true;

    /// <summary>
    ///     Indicates whether the entity is currently stepping on its right leg; oscillates with each step.
    ///     See FootprintSpriteSpecifier for info on how it's used.
    /// </summary>
    public bool RightStep = false;

    /// <summary>
    ///     Position of the last step.
    /// </summary>
    public EntityCoordinates LastStepPos;

    /// <summary>
    ///     The computed value of StepDistance with various modifiers applied to it (like ScaleWithEntitySize).
    /// </summary>
    public float NextStepDistance = float.NegativeInfinity;

    /// <summary>
    ///     The base RSI path where all footprint sprites are stored.
    /// </summary>
    [DataField]
    public ResPath BaseRsiPath = new("/Textures/_EE/Effects/footprints.rsi");

    /// <summary>
    ///     Sprite state specifier used when walking barefoot.
    /// </summary>
    [DataField]
    public FootprintSpriteSpecifier BarefootSprite = new("footprint-*-bare-human", 0);

    /// <summary>
    ///     Sprite state specifiers used in various conditions.
    /// </summary>
    /// <see cref="FootprintSpriteSpecifier"/>
    [DataField]
    public FootprintSpriteSpecifier
        ShoesSprite = new("footprint-shoes", 0),
        HardsuitSprite = new("footprint-suit", 0),
        DraggingSprite = new("dragging", 5);
}

[DataDefinition]
public partial struct FootprintSpriteSpecifier
{
    /// <summary>
    ///     Sprite state prefix. If the value of this field is equal to 0, then sprite states have the format {SpritePrefix}.
    ///     If it is greater or equal than 1, then sprite states have the format {SpritePrefix}-{random number between 1 and Count}.
    ///
    ///     The prefix can contain a special character * (asterisk), which will be replaced
    ///     with the string "right" or "left" based on which foot the entity is currently using. If it's absent, there will
    ///     be no difference between left and right footprints.
    /// </summary>
    [DataField(required: true)]
    public string SpritePrefix;

    /// <see cref="SpritePrefix"/>
    [DataField(required: true)]
    public int Count;

    public FootprintSpriteSpecifier(string spritePrefix, int count)
    {
        SpritePrefix = spritePrefix;
        Count = count;
    }

    /// <summary>
    ///     Samples a random sprite from this specifier.
    /// </summary>
    public string SampleSprite(IRobustRandom random)
    {
        if (Count <= 0)
            return SpritePrefix;

        var idx = random.Next(0, Count) + 1;
        return $"{SpritePrefix}-{idx}";
    }
}
