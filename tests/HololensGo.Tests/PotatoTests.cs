using HololensGo.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Numerics;

namespace HololensGo.Tests;

[TestClass]
public class PotatoTests
{
    [TestMethod]
    public void Tick_AppliesGravityToVelocityY()
    {
        var potato = new Potato
        {
            Velocity = new Vector3(0f, 10f, 0f)
        };

        potato.Tick(0.25f);

        Assert.AreEqual(10f - 9.81f * 0.25f, potato.Velocity.Y, 0.0001f);
    }

    [TestMethod]
    public void Tick_UpdatesPositionUsingUpdatedVelocity()
    {
        var potato = new Potato
        {
            Position = new Vector3(1f, 2f, 3f),
            Velocity = new Vector3(4f, 5f, 6f)
        };

        const float dt = 0.5f;
        potato.Tick(dt);

        var expectedVelocity = new Vector3(4f, 5f - 9.81f * dt, 6f);
        var expectedPosition = new Vector3(1f, 2f, 3f) + expectedVelocity * dt;

        Assert.AreEqual(expectedPosition.X, potato.Position.X, 0.0001f);
        Assert.AreEqual(expectedPosition.Y, potato.Position.Y, 0.0001f);
        Assert.AreEqual(expectedPosition.Z, potato.Position.Z, 0.0001f);
    }

    [TestMethod]
    public void Tick_IncrementsLifetime()
    {
        var potato = new Potato { Lifetime = 1.2f };

        potato.Tick(0.3f);

        Assert.AreEqual(1.5f, potato.Lifetime, 0.0001f);
    }

    [TestMethod]
    public void Tick_AdvancesProjectileSpinUsingTravelSpeed()
    {
        var potato = new Potato
        {
            Velocity = new Vector3(0f, 10f, 0f)
        };

        potato.Tick(0.25f);

        Assert.IsTrue(potato.SpinRadians > 0f);
        Assert.IsTrue(potato.SpinRadians < 6.2831853f);
    }

    [TestMethod]
    public void Tick_WrapsSpinAtTheExactFullTurnBoundary()
    {
        const float FullTurn = 6.2831853f;
        const float dt = 0.1f;
        var potato = new Potato
        {
            // This exactly cancels Tick's gravity update, producing zero additional spin.
            Velocity = new Vector3(0f, 9.81f * dt, 0f),
            SpinRadians = FullTurn
        };

        potato.Tick(dt);

        Assert.AreEqual(0f, potato.SpinRadians, 0.0001f);
    }

    [TestMethod]
    public void HasExpired_IsTrueOnlyWhenLifetimeGreaterThanFive()
    {
        var potato = new Potato { Lifetime = 5.0f };
        Assert.IsFalse(potato.HasExpired);

        potato.Lifetime = 5.0001f;
        Assert.IsTrue(potato.HasExpired);
    }
}
