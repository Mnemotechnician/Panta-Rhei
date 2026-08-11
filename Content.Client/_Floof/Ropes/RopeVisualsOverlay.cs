using System.Linq;
using System.Numerics;
using Content.Shared._Floof.Paint;
using Content.Shared._Floof.Rope.Components;
using Content.Shared._Floof.Rope.Prototypes;
using Content.Shared._Floof.Util;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Floof.Ropes;

public sealed class RopeVisualsOverlay : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    private readonly IEntityManager _entMan;
    private readonly IGameTiming _timing;
    private readonly SpriteSystem _sprites;
    private readonly SharedTransformSystem _xform;
    private readonly IPrototypeManager _prototypeManager;
    private readonly IEyeManager _eyeManager;

    private readonly EntityQuery<TransformComponent> _xformQuery;
    private readonly EntityQuery<SpriteComponent> _spriteQuery;
    private readonly EntityQuery<ColorPaintedComponent> _paintQuery;

    private ISawmill Log => Logger.GetSawmill("rope-visuals");
    private Ticker _logTicker = new(TimeSpan.FromSeconds(3));

    public RopeVisualsOverlay(IEntityManager entMan)
    {
        _entMan = entMan;
        _timing = IoCManager.Resolve<IGameTiming>();
        _sprites = _entMan.System<SpriteSystem>();
        _xform = _entMan.System<SharedTransformSystem>();
        _prototypeManager = IoCManager.Resolve<IPrototypeManager>();
        _eyeManager = IoCManager.Resolve<IEyeManager>();

        _xformQuery = _entMan.GetEntityQuery<TransformComponent>();
        _spriteQuery = _entMan.GetEntityQuery<SpriteComponent>();
        _paintQuery = _entMan.GetEntityQuery<ColorPaintedComponent>();
        _entMan.GetEntityQuery<RopeComponent>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var worldHandle = args.WorldHandle;
        worldHandle.SetTransform(Vector2.Zero, Angle.Zero);

        var query = _entMan.EntityQueryEnumerator<RopeComponent>();
        while (query.MoveNext(out var ropeUid, out var ropeComp))
        {
            if (ropeComp.IsTemporarilyNullspaced)
                continue;

            // Get configuration and sprite
            if (!_prototypeManager.TryIndex(ropeComp.Configuration, out var config))
            {
                if (_logTicker.TryUpdate(_timing))
                    Log.Warning($"Rope {ropeUid} has invalid configuration prototype {ropeComp.Configuration}");
                continue;
            }

            var texture = _sprites.Frame0(config.Sprite);

            // Collect link positions (as map coords) and validate map id
            var linkCount = ropeComp.Links.Count;
            var positions = new Vector2[linkCount + 2];
            var canRender = true;

            for (var i = 0; i < linkCount; i++)
            {
                var linkEntity = ropeComp.Links[i].LinkEntity;
                if (!_xformQuery.TryGetComponent(linkEntity, out var xform) || xform.MapID != args.MapId)
                {
                    canRender = false;
                    break;
                }

                // Note: this sets array indices 1..(linkCount+1) while the array has 0..(linkCount+2)
                positions[i + 1] = _xform.ToMapCoordinates(xform.Coordinates, false).Position;
            }

            // Set first and last array elements to rope anchors
            if (ropeComp.ConnectedStart is {} start)
                positions[0] = GetAnchorPosition(start.Item1, args);

            if (ropeComp.ConnectedEnd is {} end)
                positions[^1] = GetAnchorPosition(end.Item1, args);

            if (!canRender)
                continue;

            // Depending on whether or not rope ends are connected, some array elements may need to be skipped
            var startIdx = ropeComp.ConnectedStart.HasValue ? 0 : 1;
            var endIdx = positions.Length - (ropeComp.ConnectedEnd.HasValue ? 1 : 2);
            var segmentCount = endIdx - startIdx;

            if (segmentCount < 1 || segmentCount > 64)
            {
                if (_logTicker.TryUpdate(_timing))
                    Log.Warning($"Rope {ropeUid} has an unsupported number of segments ({segmentCount}), skipping.");
                continue;
            }

            var color = _paintQuery.CompOrNull(ropeUid)?.Color ?? Color.White; // TODO: replace wiith a property
            DrawPolyline(positions, startIdx, endIdx, worldHandle, texture, color);
        }
    }

    private void DrawPolyline(
        Vector2[] vertices,
        int startIdx,
        int endIdx,
        DrawingHandleWorld worldHandle,
        Texture texture,
        Color color)
    {
        if (vertices.Length < 2)
            return;

        // Compute lengths of each segment
        // ith element contains the length of the segment between the vertices i and i+1
        var lengths = new float[vertices.Length];
        lengths[^1] = 0; // fallback
        for (var i = 0; i < lengths.Length - 1; i++)
            lengths[i] = (vertices[i + 1] - vertices[i]).Length();

        // Draw tiles along the polyline formed by the elements of positions from startIdx to endIdx
        var totalLength = lengths.Sum();
        if (totalLength < 0.01f)
            return;

        var textureWidth = texture.Width / (float) EyeManager.PixelsPerMeter;
        var textureHeightTiling = texture.Height / (float) EyeManager.PixelsPerMeter;

        // Due to texture tiling this can cause the texture to be drawn 100+ times, so uh...
        if (totalLength > textureHeightTiling * 64)
        {
            if (_logTicker.TryUpdate(_timing))
                Log.Warning($"Polyline has an absurd length ({totalLength}), skipping.");
            return;
        }

        // How much length of the polyline has been drawn
        var currentDist = 0f;
        // Position from which the next line will be drawn
        var currentPos = vertices[startIdx];

        while (currentDist < totalLength)
        {
            // In each iteration we do one of the following:
            // 1. If this segment is short enough to be drawn with one instance of the rope texture (however much is left on it from the last segment), we do it and go to the next one
            // 2. If this segment is too long, we draw a portion of it and then draw it again on the next iteration
            var nextDist = Math.Min(currentDist + textureHeightTiling, totalLength);
            var nextPos = GetPolylinePositionAtDistance(nextDist, vertices, lengths, startIdx, endIdx);
            var segment = nextPos - currentPos;
            // Skip tiny segments
            var segLen = segment.Length();
            if (segLen < 0.001f)
            {
                currentDist = nextDist;
                currentPos = nextPos;
                continue;
            }

            var angle = segment.ToWorldAngle();
            var midPoint = (currentPos + nextPos) / 2f;

            // Determine UV clipping for the last partial tile
            var uv = new UIBox2(0, 0, 1, 1);
            var isPartial = nextDist < totalLength && (nextDist - currentDist) < textureHeightTiling * 0.99f;
            if (isPartial)
            {
                var fraction = (nextDist - currentDist) / textureHeightTiling;
                uv = new UIBox2(0, 0, 1, fraction);
            }

            var box = new Box2(
                -textureWidth / 2f,
                -segLen / 2f,
                textureWidth / 2f,
                segLen / 2f);
            var rotatedBox = new Box2Rotated(box.Translated(midPoint), angle, midPoint);

            worldHandle.DrawTextureRectRegion(texture, rotatedBox, color, uv);

            currentDist = nextDist;
            currentPos = nextPos;
        }
    }

    /// <summary>
    ///     Calculates the world position of the anchor point for a connected entity.
    /// </summary>
    private Vector2 GetAnchorPosition(EntityUid entity, OverlayDrawArgs args)
    {
        if (!_xformQuery.TryGetComponent(entity, out var xform))
            return Vector2.Zero;

        var pos = _xform.ToMapCoordinates(xform.Coordinates, false).Position;
        var rot = _xform.GetWorldRotation(xform);

        var offset = Vector2.Zero;
        if (_spriteQuery.TryGetComponent(entity, out var sprite))
            offset = sprite.Offset * sprite.Scale;

        // TODO: might need to take joints into account since they can be created with offsets

        return pos + rot.RotateVec(offset);
    }

    /// <summary>
    ///     Finds a point belonging to the polyline such that its distance from the start of the polyline is equal to the distance arg.
    /// </summary>
    private Vector2 GetPolylinePositionAtDistance(float distance, Vector2[] vertices, float[] lengths, int startIdx, int endIdx)
    {
        float accumulated = 0;
        for (var i = startIdx; i <= endIdx - 1; i++)
        {
            var segment = vertices[i + 1] - vertices[i];
            var segLen = lengths[i];
            if (distance <= accumulated + segLen)
            {
                var t = (distance - accumulated) / segLen;
                return vertices[i] + segment * t;
            }
            accumulated += segLen;
        }
        return vertices[endIdx];
    }
}
