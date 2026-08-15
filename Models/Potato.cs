using System.Numerics;

namespace HololensGo.Models
{
    /// <summary>
    /// A lightweight potato projectile simulated in world-space metres.
    /// </summary>
    public class Potato
    {
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public float Lifetime { get; set; }
        public float SpinRadians { get; set; }
        public int BounceCount { get; set; }
        public bool HasCollided { get; set; }
        public bool HasExpired => Lifetime > GameSession.DefaultProjectileLifetimeSeconds;

        /// <summary>
        /// Advances ballistic motion using semi-implicit Euler integration.
        /// </summary>
        public void Tick(float elapsedSeconds)
        {
            if (elapsedSeconds <= 0f)
            {
                return;
            }

            // Vector3 is a struct; mutate via local copy, then assign back.
            var velocity = Velocity;
            velocity.Y -= 9.81f * elapsedSeconds;
            Velocity = velocity;
            Position += velocity * elapsedSeconds;
            SpinRadians += velocity.Length() * elapsedSeconds * 4f;
            if (SpinRadians >= 6.2831853f)
            {
                SpinRadians %= 6.2831853f;
            }

            Lifetime += elapsedSeconds;
        }
    }
}
