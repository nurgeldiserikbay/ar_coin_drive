using UnityEngine;

/// <summary>
/// A solid footprint on the floor plane (play-area metres, x and z): a circle (cone, tyre
/// stack, plant pot, table leg) or a rotated box (wall block, sofa, shelf).
/// Angle uses the car's heading convention: 0 faces +z, positive turns toward +x.
/// </summary>
public struct WorldObstacle
{
    public Vector2 Center;
    public Vector2 HalfSize;
    public float Angle;
    public float Radius;
    public bool IsCircle;

    public static WorldObstacle Circle(Vector2 center, float radius) =>
        new WorldObstacle { Center = center, Radius = radius, IsCircle = true };

    public static WorldObstacle Box(Vector2 center, Vector2 halfSize, float angle = 0f) =>
        new WorldObstacle { Center = center, HalfSize = halfSize, Angle = angle };

    /// <summary>True when a floor point is inside (used to pick a placed obstacle by tapping it).</summary>
    public bool Contains(Vector2 point, float margin)
    {
        var delta = point - Center;
        if (IsCircle) return delta.magnitude <= Radius + margin;
        var right = CarCollision.Right(Angle);
        var forward = CarCollision.Forward(Angle);
        return Mathf.Abs(Vector2.Dot(delta, right)) <= HalfSize.x + margin
            && Mathf.Abs(Vector2.Dot(delta, forward)) <= HalfSize.y + margin;
    }
}

/// <summary>Overlap tests between the car's rotated rectangle and world obstacles.</summary>
public static class CarCollision
{
    public static Vector2 Forward(float headingDegrees)
    {
        var radians = headingDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
    }

    public static Vector2 Right(float headingDegrees)
    {
        var radians = headingDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
    }

    /// <summary>
    /// Returns true on overlap; normal points from the obstacle toward the car and depth is
    /// how far the car must move along it to separate.
    /// </summary>
    public static bool Overlap(Vector2 carCenter, float carHeading, float halfWidth, float halfLength,
        in WorldObstacle obstacle, out Vector2 normal, out float depth)
    {
        var forward = Forward(carHeading);
        var right = Right(carHeading);
        return obstacle.IsCircle
            ? CircleOverlap(carCenter, right, forward, halfWidth, halfLength, obstacle.Center, obstacle.Radius, out normal, out depth)
            : BoxOverlap(carCenter, right, forward, halfWidth, halfLength, obstacle, out normal, out depth);
    }

    private static bool CircleOverlap(Vector2 carCenter, Vector2 right, Vector2 forward, float halfWidth,
        float halfLength, Vector2 circleCenter, float radius, out Vector2 normal, out float depth)
    {
        var delta = circleCenter - carCenter;
        var localX = Vector2.Dot(delta, right);
        var localZ = Vector2.Dot(delta, forward);
        var closestX = Mathf.Clamp(localX, -halfWidth, halfWidth);
        var closestZ = Mathf.Clamp(localZ, -halfLength, halfLength);
        var closest = carCenter + right * closestX + forward * closestZ;
        var away = closest - circleCenter;
        var distance = away.magnitude;

        // Circle centre inside the car body: push out along the shallowest side.
        if (Mathf.Abs(localX) <= halfWidth && Mathf.Abs(localZ) <= halfLength)
        {
            var toSide = halfWidth - Mathf.Abs(localX);
            var toEnd = halfLength - Mathf.Abs(localZ);
            normal = toSide < toEnd ? -right * Mathf.Sign(localX) : -forward * Mathf.Sign(localZ);
            depth = Mathf.Min(toSide, toEnd) + radius;
            return true;
        }

        if (distance >= radius)
        {
            normal = Vector2.zero;
            depth = 0f;
            return false;
        }

        normal = distance > 0.0001f ? away / distance : -delta.normalized;
        depth = radius - distance;
        return true;
    }

    private static bool BoxOverlap(Vector2 carCenter, Vector2 right, Vector2 forward, float halfWidth,
        float halfLength, in WorldObstacle box, out Vector2 normal, out float depth)
    {
        var boxRight = Right(box.Angle);
        var boxForward = Forward(box.Angle);
        var delta = carCenter - box.Center;
        normal = Vector2.zero;
        depth = float.MaxValue;

        for (var i = 0; i < 4; i++)
        {
            var axis = i == 0 ? right : i == 1 ? forward : i == 2 ? boxRight : boxForward;
            var carExtent = halfWidth * Mathf.Abs(Vector2.Dot(right, axis)) + halfLength * Mathf.Abs(Vector2.Dot(forward, axis));
            var boxExtent = box.HalfSize.x * Mathf.Abs(Vector2.Dot(boxRight, axis))
                + box.HalfSize.y * Mathf.Abs(Vector2.Dot(boxForward, axis));
            var distance = Vector2.Dot(delta, axis);
            var overlap = carExtent + boxExtent - Mathf.Abs(distance);
            if (overlap <= 0f)
            {
                normal = Vector2.zero;
                depth = 0f;
                return false;
            }

            if (overlap < depth)
            {
                depth = overlap;
                normal = distance >= 0f ? axis : -axis;
            }
        }

        return true;
    }
}
