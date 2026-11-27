using UnityEngine;

/// <summary>
/// Race-track stunts: jumps off ramps with spin tricks in the air, and loop-the-loops.
/// In the air the steering spins the car; land it straight (within CleanLanding degrees of a
/// full turn) to bank a coin and a boost per full spin, land it crooked and it wipes out.
/// Loops always go round (a car is carried at LoopMinSpeed at least) and pay a coin.
/// </summary>
public sealed partial class ARDemoController
{
    // Toy-scale gravity: real gravity would give 2 mm hops at RC car speeds.
    private const float StuntGravity = 2.4f;
    private const float JumpLift = 1.0f;          // upward m/s per m/s of speed at the lip
    private const float SpinRate = 620f;          // degrees per second at full steering
    private const float CleanLanding = 40f;       // degrees off a full turn that still lands
    private const float BigAirSeconds = 0.45f;
    private const float LoopMinSpeed = 0.45f;

    /// <summary>Runs the car while it flies or rides a loop. Returns true when it did.</summary>
    private bool StuntStep(Car car, float dt)
    {
        if (car.Loop != null)
        {
            RideLoop(car, dt);
            return true;
        }

        if (!car.Airborne) return false;
        Fly(car, dt);
        return true;
    }

    /// <summary>After a normal step on the track: ride ramps, take off at the lip, enter loops.</summary>
    private void StuntsAfterStep(Car car, Vector2 from)
    {
        var track = RaceTrack.Shared;
        if (track.Stunts.Count == 0) return;
        if (car.Speed > 0.02f)
        {
            var loop = track.LoopEntered(from, car.Position);
            if (loop != null)
            {
                EnterLoop(car, loop);
                return;
            }

            if (track.RampLaunched(from, car.Position) != null)
            {
                Launch(car);
                return;
            }
        }

        car.Height = track.RampSurface(car.Position, out var slope);
        car.Pitch = slope;
    }

    // ------------------------------------------------------------------ jumps

    private void Launch(Car car)
    {
        car.Airborne = true;
        car.Height = RaceTrack.RampHeight;
        car.VSpeed = car.Speed * JumpLift;
        car.AirTime = 0f;
        car.Spin = 0f;
        if (car.IsLocal && car.Speed > 0.35f) ShowHint("AIR!  Steer to spin");
    }

    private void Fly(Car car, float dt)
    {
        car.AirTime += dt;
        car.VSpeed -= StuntGravity * dt;
        car.Height += car.VSpeed * dt;
        car.Lateral *= Mathf.Exp(-1f * dt);

        // No grip in the air: the car keeps its line, the steering only spins the body.
        var position = car.Position + (CarCollision.Forward(car.Heading) * car.Speed + CarCollision.Right(car.Heading) * car.Lateral) * dt;
        if (RaceTrack.Shared.ConstrainToRails(ref position, car.Spec.HalfWidth))
        {
            car.Speed *= Mathf.Exp(-2f * dt);
            car.Lateral *= 0.5f;
        }

        car.Position = ClampInsideArena(car, position, car.Heading);
        if (!car.IsBot) car.Spin += car.Steer * SpinRate * dt;
        car.Pitch = Mathf.Clamp(Mathf.Atan2(car.VSpeed, Mathf.Max(0.2f, Mathf.Abs(car.Speed))) * Mathf.Rad2Deg * 0.7f, -25f, 25f);
        if (car.Height <= 0f && car.VSpeed < 0f) Land(car);
    }

    private void Land(Car car)
    {
        var impact = Mathf.Clamp01(-car.VSpeed / 1.5f);
        var offTurn = Mathf.DeltaAngle(0f, car.Spin);
        var turns = Mathf.RoundToInt(Mathf.Abs(car.Spin) / 360f);
        car.Airborne = false;
        car.Height = 0f;
        car.Pitch = 0f;
        car.VSpeed = 0f;
        car.Spin = 0f;
        if (car.Rig != null) car.Rig.Bump(0.25f + impact * 0.5f);

        if (Mathf.Abs(offTurn) > CleanLanding)
        {
            // Landed crooked: the car skids to a near stop, facing where it was spinning.
            car.Heading += offTurn;
            car.Speed *= 0.2f;
            car.Lateral = 0f;
            if (!car.IsLocal) return;
            ShowHint("WIPEOUT!");
            GameAudio.Instance.Play(GameAudio.Sfx.Crash, 0.8f);
            GameAudio.Haptic(90);
            return;
        }

        if (turns > 0)
        {
            car.Coins += turns;
            car.BoostUntil = Time.time + BoostSeconds;
            if (!car.IsLocal) return;
            ShowHint((turns * 360) + " SPIN!  +" + turns);
            GameAudio.Instance.Play(GameAudio.Sfx.Coin);
            GameAudio.Haptic(50);
            return;
        }

        if (car.AirTime >= BigAirSeconds)
        {
            car.BoostUntil = Time.time + BoostSeconds * 0.5f;
            if (car.IsLocal) ShowHint("BIG AIR!");
        }

        if (car.IsLocal) GameAudio.Instance.Play(GameAudio.Sfx.Bump, 0.3f + impact * 0.5f);
    }

    // ------------------------------------------------------------------ loops

    private void EnterLoop(Car car, TrackStunt loop)
    {
        var track = RaceTrack.Shared;
        car.Loop = loop;
        car.LoopAngle = 0f;
        car.LoopEntrySpeed = Mathf.Max(car.Speed, LoopMinSpeed);
        car.LoopLane = Mathf.Clamp(track.LateralOffset(car.Position), track.LoopEntryLane * 0.6f, track.LoopEntryLane * 1.4f);
        car.Lateral = 0f;
        car.Spin = 0f;
        car.Heading = loop.Heading;
        if (car.IsLocal) GameAudio.Haptic(30);
        PlaceOnLoop(car);
    }

    private void RideLoop(Car car, float dt)
    {
        // A little slower over the top, back to full speed at the bottom.
        var speed = car.LoopEntrySpeed * (1f - 0.15f * (1f - Mathf.Cos(car.LoopAngle)));
        car.Speed = speed;
        car.LoopAngle += speed / RaceTrack.LoopRadius * dt;
        if (car.LoopAngle >= Mathf.PI * 2f)
        {
            ExitLoop(car);
            return;
        }

        PlaceOnLoop(car);
    }

    private void PlaceOnLoop(Car car)
    {
        var track = RaceTrack.Shared;
        var lateral = car.LoopLane - track.LoopLaneShift * (car.LoopAngle / (Mathf.PI * 2f));
        var point = track.LoopPoint(car.Loop, car.LoopAngle, lateral);
        car.Position = new Vector2(point.x, point.z);
        car.Height = point.y;
        car.Pitch = car.LoopAngle * Mathf.Rad2Deg;
    }

    private void ExitLoop(Car car)
    {
        var track = RaceTrack.Shared;
        var loop = car.Loop;
        car.Loop = null;
        car.Height = 0f;
        car.Pitch = 0f;
        car.Heading = loop.Heading;
        car.Position = loop.Point + loop.Outward * (car.LoopLane - track.LoopLaneShift) + loop.Tangent * 0.01f;
        car.Speed = car.LoopEntrySpeed;
        car.Lateral = 0f;
        car.Coins++;
        car.BoostUntil = Time.time + BoostSeconds * 0.6f;
        if (!car.IsLocal) return;
        ShowHint("LOOP!  +1");
        GameAudio.Instance.Play(GameAudio.Sfx.Coin);
        GameAudio.Haptic(40);
    }

    /// <summary>Two cars at different heights (one flying or in a loop) pass each other.</summary>
    private static bool ApartInHeight(Car a, Car b) =>
        a.Loop != null || b.Loop != null || Mathf.Abs(a.Height - b.Height) > 0.035f;
}
