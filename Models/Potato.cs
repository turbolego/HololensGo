using System.Numerics;

namespace HololensGo.Models
{
    public class Potato
    {
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public float Lifetime { get; set; }
        public bool HasCollided { get; set; }
        public bool HasExpired => Lifetime > 5f;

        public void Tick(float dt)
        {
            // Vector3 is a struct; mutate via local copy, then assign back.
            var velocity = Velocity;
            velocity.Y -= 9.81f * dt;
            Velocity = velocity;
            Position += velocity * dt;
            Lifetime += dt;
        }
    }
}
