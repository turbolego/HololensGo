using System.Numerics;
using HololensGo.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HololensGo.Tests;

[TestClass]
public class GameSessionTests
{
    private static readonly Vector3 WorldOrigin = Vector3.Zero;
    private static readonly Vector3 TargetPosition = new Vector3(0f, 1f, -2f);

    [TestMethod]
    public void SpawnTarget_MakesTargetVisibleAtRequestedPosition()
    {
        var session = new GameSession();

        session.SpawnTarget(TargetPosition);

        Assert.IsTrue(session.TargetVisible);
        Assert.AreEqual(TargetPosition, session.TargetPosition);
    }

    [TestMethod]
    public void TryThrow_AcceptsProjectileBelowBudget()
    {
        var session = new GameSession();

        bool accepted = session.TryThrow(CreatePotato());

        Assert.IsTrue(accepted);
        Assert.AreEqual(1, session.Potatoes.Count);
    }

    [TestMethod]
    public void TryThrow_RejectsNullProjectile()
    {
        var session = new GameSession();

        bool accepted = session.TryThrow(null!);

        Assert.IsFalse(accepted);
        Assert.AreEqual(0, session.Potatoes.Count);
    }

    [TestMethod]
    public void TryThrow_EnforcesActiveProjectileBudget()
    {
        var session = new GameSession();
        for (int i = 0; i < GameSession.MaxActivePotatoes; i++)
        {
            Assert.IsTrue(session.TryThrow(CreatePotato()));
        }

        bool accepted = session.TryThrow(CreatePotato());

        Assert.IsFalse(accepted);
        Assert.AreEqual(GameSession.MaxActivePotatoes, session.Potatoes.Count);
    }

    [TestMethod]
    public void Update_SweptCollisionRegistersHitForFastProjectile()
    {
        var session = new GameSession();
        session.SpawnTarget(new Vector3(0f, 0f, -1f));
        session.TryThrow(new Potato
        {
            Position = new Vector3(0f, 0f, 0f),
            Velocity = new Vector3(0f, 0f, -120f)
        });

        GameUpdateResult result = session.Update(1f / 60f, WorldOrigin, 0f);

        Assert.IsTrue(result.TargetHit);
        Assert.IsFalse(session.TargetVisible);
        Assert.AreEqual(1, session.Hits);
        Assert.AreEqual(100, session.Score);
        Assert.AreEqual(1, session.Combo);
        Assert.AreEqual(0, session.Potatoes.Count);
    }

    [TestMethod]
    public void Update_HitBeginsTargetRespawnCountdown()
    {
        var session = new GameSession { TargetRespawnDelaySeconds = 0.1f };
        session.SpawnTarget(new Vector3(0f, 0f, -0.1f));
        session.TryThrow(new Potato
        {
            Position = Vector3.Zero,
            Velocity = new Vector3(0f, 0f, -2f)
        });

        session.Update(0.1f, WorldOrigin, 0f);
        GameUpdateResult result = session.Update(0.1f, WorldOrigin, 0f);

        Assert.IsTrue(result.TargetRespawned);
        Assert.IsTrue(session.TargetVisible);
    }

    [TestMethod]
    public void Update_ConsecutiveHitsIncreaseComboAndScore()
    {
        var session = new GameSession { TargetRespawnDelaySeconds = 0.1f };
        RegisterDirectHit(session);
        session.Update(0.1f, WorldOrigin, 0f);
        RegisterDirectHit(session);

        Assert.AreEqual(2, session.Hits);
        Assert.AreEqual(2, session.Combo);
        Assert.AreEqual(2, session.BestCombo);
        Assert.AreEqual(225, session.Score);
    }

    [TestMethod]
    public void Update_ExpiredProjectileCountsAsMissAndResetsCombo()
    {
        var session = new GameSession();
        RegisterDirectHit(session);
        session.TryThrow(new Potato
        {
            // Keep the expired projectile outside the target radius because the
            // target is allowed to respawn during this update.
            Position = new Vector3(10f, 0f, 0f),
            Velocity = Vector3.Zero,
            Lifetime = GameSession.DefaultProjectileLifetimeSeconds
        });

        GameUpdateResult result = session.Update(0.1f, WorldOrigin, 0f);

        Assert.AreEqual(1, session.Misses);
        Assert.AreEqual(0, session.Combo);
        Assert.AreEqual(1, result.ProjectilesRemoved);
    }

    [TestMethod]
    public void Update_OutOfRangeProjectileCountsAsMiss()
    {
        var session = new GameSession { ProjectileRangeMeters = 1f };
        session.TryThrow(new Potato
        {
            Position = new Vector3(10f, 0f, 0f),
            Velocity = Vector3.Zero
        });

        session.Update(0.1f, WorldOrigin, 0f);

        Assert.AreEqual(1, session.Misses);
        Assert.AreEqual(0, session.Potatoes.Count);
    }

    [TestMethod]
    public void Update_BouncesProjectileAtFloorAndDampsHorizontalVelocity()
    {
        var session = new GameSession { BounceRestitution = 0.5f, GroundFriction = 0.5f };
        var potato = new Potato
        {
            Position = new Vector3(0f, -0.01f, 0f),
            Velocity = new Vector3(2f, -2f, 4f)
        };
        session.TryThrow(potato);

        session.Update(0.01f, WorldOrigin, 0f);

        Assert.AreEqual(0f, potato.Position.Y, 0.0001f);
        Assert.IsTrue(potato.Velocity.Y > 0f);
        Assert.AreEqual(1f, potato.Velocity.X, 0.0001f);
        Assert.AreEqual(2f, potato.Velocity.Z, 0.0001f);
        Assert.AreEqual(1, potato.BounceCount);
    }

    [TestMethod]
    public void Update_SettlesLowEnergyBounce()
    {
        var session = new GameSession { BounceRestitution = 0.1f };
        var potato = new Potato
        {
            Position = new Vector3(0f, -0.01f, 0f),
            Velocity = new Vector3(0f, -0.1f, 0f)
        };
        session.TryThrow(potato);

        session.Update(0.01f, WorldOrigin, 0f);

        Assert.AreEqual(0f, potato.Velocity.Y, 0.0001f);
    }

    [TestMethod]
    public void Update_IgnoresNonPositiveOrNonFiniteElapsedTime()
    {
        var session = new GameSession();
        session.TryThrow(CreatePotato());

        session.Update(0f, WorldOrigin, 0f);
        session.Update(float.NaN, WorldOrigin, 0f);
        session.Update(float.PositiveInfinity, WorldOrigin, 0f);

        Assert.AreEqual(1, session.Potatoes.Count);
        Assert.AreEqual(0f, session.ElapsedSeconds, 0.0001f);
    }

    [TestMethod]
    public void Update_ClampsLargeFramesForStableSimulation()
    {
        var session = new GameSession();
        session.TryThrow(CreatePotato());

        session.Update(5f, WorldOrigin, -100f);

        Assert.AreEqual(GameSession.MaximumFrameSeconds, session.ElapsedSeconds, 0.0001f);
        Assert.IsTrue(session.Potatoes[0].Lifetime <= GameSession.MaximumFrameSeconds + 0.0001f);
    }

    [TestMethod]
    public void Update_MovesVisibleTargetWithinConfiguredDriftBounds()
    {
        var session = new GameSession
        {
            TargetDriftRadiusMeters = 0.5f,
            TargetBobHeightMeters = 0.2f,
            TargetDriftRadiansPerSecond = 2f
        };
        session.SpawnTarget(TargetPosition);

        session.Update(0.25f, WorldOrigin, 0f);

        Vector3 displacement = session.TargetPosition - TargetPosition;
        Assert.IsTrue(displacement.Length() > 0.001f);
        Assert.IsTrue(Math.Abs(displacement.X) <= 0.5f);
        Assert.IsTrue(Math.Abs(displacement.Y) <= 0.2f);
        Assert.IsTrue(Math.Abs(displacement.Z) <= 0.275f);
    }

    [TestMethod]
    public void Reset_ClearsStatisticsProjectilesAndTarget()
    {
        var session = new GameSession();
        RegisterDirectHit(session);
        session.TryThrow(CreatePotato());

        session.Reset();

        Assert.IsFalse(session.TargetVisible);
        Assert.AreEqual(0, session.Potatoes.Count);
        Assert.AreEqual(0, session.Score);
        Assert.AreEqual(0, session.Hits);
        Assert.AreEqual(0, session.Misses);
        Assert.AreEqual(0, session.Combo);
        Assert.AreEqual(0, session.BestCombo);
        Assert.AreEqual(0f, session.ElapsedSeconds, 0.0001f);
    }

    [TestMethod]
    public void Update_RemainsStableAcrossOneHundredSimulationIterations()
    {
        var session = new GameSession { ProjectileRangeMeters = 100f };
        session.SpawnTarget(new Vector3(50f, 0f, 0f));
        session.TryThrow(new Potato
        {
            Position = new Vector3(0f, 1f, 0f),
            Velocity = new Vector3(1f, 2f, 0f)
        });

        for (int iteration = 0; iteration < 100; iteration++)
        {
            session.Update(GameSession.SimulationStepSeconds, WorldOrigin, 0f);
        }

        Assert.AreEqual(100 * GameSession.SimulationStepSeconds, session.ElapsedSeconds, 0.0001f);
        Assert.AreEqual(1, session.Potatoes.Count);
        Assert.IsFalse(float.IsNaN(session.Potatoes[0].Position.X));
        Assert.IsFalse(float.IsNaN(session.Potatoes[0].Position.Y));
        Assert.IsFalse(float.IsNaN(session.Potatoes[0].Position.Z));
    }

    private static Potato CreatePotato()
    {
        return new Potato
        {
            Position = new Vector3(0f, 1f, 0f),
            Velocity = new Vector3(0f, 1f, -1f)
        };
    }

    private static void RegisterDirectHit(GameSession session)
    {
        session.SpawnTarget(new Vector3(0f, 0f, -0.1f));
        session.TryThrow(new Potato
        {
            Position = Vector3.Zero,
            Velocity = new Vector3(0f, 0f, -2f)
        });
        session.Update(0.1f, WorldOrigin, 0f);
    }
}
