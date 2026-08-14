using System;
using System.Collections.Generic;
using System.Numerics;

namespace HololensGo.Models
{
    /// <summary>
    /// Owns the renderer-independent rules for a single HololensGo session.
    /// It keeps projectile simulation deterministic and exposes discrete events
    /// so the holographic layer can trigger animation and audio feedback.
    /// </summary>
    public sealed class GameSession
    {
        public const int MaxActivePotatoes = 10;
        public const float DefaultProjectileLifetimeSeconds = 5f;
        public const float DefaultProjectileRangeMeters = 20f;
        public const float DefaultTargetRadiusMeters = 0.15f;
        public const float DefaultTargetRespawnDelaySeconds = 1.25f;
        public const float SimulationStepSeconds = 1f / 60f;
        public const float MaximumFrameSeconds = 0.25f;

        private readonly List<Potato> potatoes = new List<Potato>();
        private Vector3 targetSpawnPosition;
        private float targetRespawnRemaining;
        private float targetMotionTime;

        public IReadOnlyList<Potato> Potatoes => potatoes;
        public Vector3 TargetPosition { get; private set; }
        public bool TargetVisible { get; private set; }
        public int Score { get; private set; }
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public float TargetRadiusMeters { get; set; } = DefaultTargetRadiusMeters;
        public float ProjectileRangeMeters { get; set; } = DefaultProjectileRangeMeters;
        public float TargetRespawnDelaySeconds { get; set; } = DefaultTargetRespawnDelaySeconds;
        public float TargetDriftRadiusMeters { get; set; }
        public float TargetBobHeightMeters { get; set; }
        public float TargetDriftRadiansPerSecond { get; set; } = 1.2f;
        public float BounceRestitution { get; set; } = 0.45f;
        public float GroundFriction { get; set; } = 0.72f;

        /// <summary>
        /// Makes the target available at a known world-space position.
        /// </summary>
        public void SpawnTarget(Vector3 position)
        {
            targetSpawnPosition = position;
            TargetPosition = position;
            TargetVisible = true;
            targetRespawnRemaining = 0f;
            targetMotionTime = 0f;
        }

        /// <summary>
        /// Resets transient session state while preserving configurable difficulty values.
        /// </summary>
        public void Reset()
        {
            potatoes.Clear();
            TargetPosition = Vector3.Zero;
            targetSpawnPosition = Vector3.Zero;
            TargetVisible = false;
            targetRespawnRemaining = 0f;
            targetMotionTime = 0f;
            Score = 0;
            Hits = 0;
            Misses = 0;
            Combo = 0;
            BestCombo = 0;
            ElapsedSeconds = 0f;
        }

        /// <summary>
        /// Adds a projectile if the active-projectile budget allows it.
        /// </summary>
        public bool TryThrow(Potato potato)
        {
            if (potato == null || potatoes.Count >= MaxActivePotatoes)
            {
                return false;
            }

            potatoes.Add(potato);
            return true;
        }

        /// <summary>
        /// Advances simulation using fixed substeps. The sweep test avoids missed hits when
        /// a projectile crosses the target between rendered frames.
        /// </summary>
        public GameUpdateResult Update(float elapsedSeconds, Vector3 worldOrigin, float floorHeight)
        {
            var result = new GameUpdateResult();
            if (!IsFinite(elapsedSeconds) || elapsedSeconds <= 0f)
            {
                return result;
            }

            float remaining = Math.Min(elapsedSeconds, MaximumFrameSeconds);
            while (remaining > 0f)
            {
                float step = Math.Min(remaining, SimulationStepSeconds);
                SimulateStep(step, worldOrigin, floorHeight, result);
                remaining -= step;
            }

            ElapsedSeconds += Math.Min(elapsedSeconds, MaximumFrameSeconds);
            return result;
        }

        private void SimulateStep(float elapsedSeconds, Vector3 worldOrigin, float floorHeight, GameUpdateResult result)
        {
            AdvanceTargetRespawn(elapsedSeconds, result);
            UpdateTargetMotion(elapsedSeconds);

            float targetRadius = Math.Max(0.01f, TargetRadiusMeters);
            float projectileRange = Math.Max(1f, ProjectileRangeMeters);

            for (int i = potatoes.Count - 1; i >= 0; i--)
            {
                Potato potato = potatoes[i];
                Vector3 previousPosition = potato.Position;
                potato.Tick(elapsedSeconds);

                if (TargetVisible && !potato.HasCollided &&
                    SegmentIntersectsSphere(previousPosition, potato.Position, TargetPosition, targetRadius))
                {
                    potato.HasCollided = true;
                    potatoes.RemoveAt(i);
                    RegisterHit(result);
                    continue;
                }

                BounceOffFloor(potato, floorHeight);

                bool isOutOfRange = Vector3.DistanceSquared(potato.Position, worldOrigin) > projectileRange * projectileRange;
                if (potato.HasExpired || isOutOfRange)
                {
                    potatoes.RemoveAt(i);
                    RegisterMiss(result);
                }
            }
        }

        private void AdvanceTargetRespawn(float elapsedSeconds, GameUpdateResult result)
        {
            if (TargetVisible || targetRespawnRemaining <= 0f)
            {
                return;
            }

            targetRespawnRemaining = Math.Max(0f, targetRespawnRemaining - elapsedSeconds);
            if (targetRespawnRemaining <= 0f)
            {
                TargetVisible = true;
                targetMotionTime = 0f;
                TargetPosition = targetSpawnPosition;
                result.TargetRespawned = true;
            }
        }

        private void UpdateTargetMotion(float elapsedSeconds)
        {
            if (!TargetVisible)
            {
                return;
            }

            targetMotionTime += elapsedSeconds;
            float radius = Math.Max(0f, TargetDriftRadiusMeters);
            float bobHeight = Math.Max(0f, TargetBobHeightMeters);
            float angularSpeed = Math.Max(0f, TargetDriftRadiansPerSecond);
            float phase = targetMotionTime * angularSpeed;

            TargetPosition = targetSpawnPosition + new Vector3(
                Cos(phase) * radius,
                Sin(phase * 2f) * bobHeight,
                Sin(phase) * radius * 0.55f);
        }

        private void RegisterHit(GameUpdateResult result)
        {
            TargetVisible = false;
            targetRespawnRemaining = Math.Max(0.1f, TargetRespawnDelaySeconds);
            Hits++;
            Combo++;
            BestCombo = Math.Max(BestCombo, Combo);

            // Consecutive throws increase the reward, capped to keep the score readable.
            int comboBonus = Math.Min(Combo - 1, 4);
            Score += 100 + (comboBonus * 25);
            result.TargetHit = true;
            result.ProjectilesRemoved++;
        }

        private void RegisterMiss(GameUpdateResult result)
        {
            Misses++;
            Combo = 0;
            result.ProjectilesRemoved++;
        }

        private void BounceOffFloor(Potato potato, float floorHeight)
        {
            if (potato.Position.Y > floorHeight || potato.Velocity.Y >= 0f)
            {
                return;
            }

            Vector3 position = potato.Position;
            position.Y = floorHeight;
            potato.Position = position;

            Vector3 velocity = potato.Velocity;
            velocity.Y = -velocity.Y * Clamp(BounceRestitution, 0f, 1f);
            velocity.X *= Clamp(GroundFriction, 0f, 1f);
            velocity.Z *= Clamp(GroundFriction, 0f, 1f);

            // Avoid visually noisy micro-bounces once the projectile has lost its energy.
            if (velocity.Y < 0.35f)
            {
                velocity.Y = 0f;
            }

            potato.Velocity = velocity;
            potato.BounceCount++;
        }

        private static float Sin(float value)
        {
            return (float)Math.Sin(value);
        }

        private static float Cos(float value)
        {
            return (float)Math.Cos(value);
        }

        private static bool SegmentIntersectsSphere(Vector3 start, Vector3 end, Vector3 center, float radius)
        {
            Vector3 segment = end - start;
            float segmentLengthSquared = segment.LengthSquared();
            if (segmentLengthSquared < 0.000001f)
            {
                return Vector3.DistanceSquared(start, center) <= radius * radius;
            }

            float t = Vector3.Dot(center - start, segment) / segmentLengthSquared;
            t = Clamp(t, 0f, 1f);
            Vector3 nearestPoint = start + (segment * t);
            return Vector3.DistanceSquared(nearestPoint, center) <= radius * radius;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    /// <summary>
    /// Reports meaningful session events from one update pass without coupling rules to rendering.
    /// </summary>
    public sealed class GameUpdateResult
    {
        public bool TargetHit { get; internal set; }
        public bool TargetRespawned { get; internal set; }
        public int ProjectilesRemoved { get; internal set; }
    }
}
