using System;
using Model;
using Model.Ops;
using Track;
using UnityEngine;

namespace CollegeEducatedAutoEngineer
{
    /// <summary>
    /// A virtual accelerometer on the locomotive body, for the diagnostic log (LogGForces).
    ///
    /// The game moves cars in one dimension along the track, so there is no 3D acceleration to read. This works it
    /// out every physics tick from the speed along the track and the track's own direction under the locomotive:
    /// world velocity V = v * forward, acceleration A = dV/dt, and the felt (specific) force f = A + g*up, as an
    /// accelerometer reads it. Standing level reads (0, 0, 1) g.
    ///
    /// Axes are the locomotive's own: X forward (toward its F end, positive when accelerating that way or nose-up on a
    /// grade), Y to the right (positive in a right-hand curve), Z up. The body direction is the mean of the track
    /// tangents at the two ends, taken from Graph.GetPositionRotation (exact geometry, so no position noise).
    /// A low-pass filter (GForceSmoothingSeconds) takes out tick-to-tick slack and coupler jitter.
    /// </summary>
    internal sealed class GForceMeter
    {
        private const float G = 9.80665f;
        private const float MaxGapSeconds = 0.25f;       // longer than this between ticks: start over
        private const float SignLearnMeters = 0.5f;      // movement needed to learn which way velocity points

        // Which way loco.velocity points along the F-R axis: +1 toward F, -1 toward R, 0 not known yet.
        // A property of the car, so it is learnt once from real movement and then kept.
        private int _velocitySign;
        private Vector3 _signRef;
        private bool _hasSignRef;

        private bool _hasLast;
        private float _lastTime;
        private Vector3 _lastVelocity;

        private bool _hasFiltered;
        private Vector3 _filtered;   // x forward, y right, z up, in g

        private float _nextAreaTime;

        public bool HasPosition { get; private set; }
        public Vector3 Position { get; private set; }     // game coordinates, m (y = elevation)
        public string TrackLocation { get; private set; } = "";
        public string Area { get; private set; } = "";

        /// <summary>The filtered reading in g (x forward, y right, z up), or NaN until there is one.</summary>
        public Vector3 Reading => _hasFiltered ? _filtered : new Vector3(float.NaN, float.NaN, float.NaN);

        public void Reset()
        {
            _hasLast = false;
            _hasFiltered = false;
        }

        public void Tick(Car loco, float now, float smoothingSeconds)
        {
            Graph graph = Graph.Shared;
            if (loco == null || graph == null) { Reset(); HasPosition = false; return; }

            Location front = loco.LocationF;
            Location rear = loco.LocationR;
            if (!front.IsValid || !rear.IsValid) { Reset(); HasPosition = false; return; }

            Graph.PositionRotation pf = graph.GetPositionRotation(front);
            Graph.PositionRotation pr = graph.GetPositionRotation(rear);
            Vector3 chord = pf.Position - pr.Position;
            if (chord.sqrMagnitude < 1f) { Reset(); return; }

            // Track tangents at both ends, turned to point from R to F, averaged: the body's direction.
            Vector3 fwdF = pf.Rotation * Vector3.forward;
            Vector3 fwdR = pr.Rotation * Vector3.forward;
            if (Vector3.Dot(fwdF, chord) < 0f) fwdF = -fwdF;
            if (Vector3.Dot(fwdR, chord) < 0f) fwdR = -fwdR;
            Vector3 forward = (fwdF + fwdR).normalized;
            Vector3 upRaw = pf.Rotation * Vector3.up + pr.Rotation * Vector3.up;
            if (Vector3.Dot(upRaw, Vector3.up) < 0f) upRaw = -upRaw;
            Vector3 up = (upRaw - Vector3.Dot(upRaw, forward) * forward).normalized;
            Vector3 right = Vector3.Cross(up, forward);   // Unity: Cross(up, forward) = right

            Vector3 centre = (pf.Position + pr.Position) * 0.5f;
            Position = centre;
            HasPosition = true;
            TrackLocation = Graph.LocationToString(front);
            if (now >= _nextAreaTime)
            {
                _nextAreaTime = now + 2f;
                try { Area = OpsController.Shared?.ClosestAreaForGamePosition(centre)?.name ?? ""; }
                catch (Exception) { Area = ""; }
            }

            float v = loco.velocity;
            LearnVelocitySign(centre, forward, v);
            if (_velocitySign == 0) { Reset(); return; }

            Vector3 velocity = forward * (v * _velocitySign);
            float dt = now - _lastTime;
            if (!_hasLast || dt <= 0f || dt > MaxGapSeconds)
            {
                _hasLast = true;
                _lastTime = now;
                _lastVelocity = velocity;
                return;
            }

            Vector3 accel = (velocity - _lastVelocity) / dt;
            _lastVelocity = velocity;
            _lastTime = now;

            Vector3 felt = accel + Vector3.up * G;
            var raw = new Vector3(Vector3.Dot(felt, forward), Vector3.Dot(felt, right), Vector3.Dot(felt, up)) / G;

            if (!_hasFiltered || smoothingSeconds <= 0.001f)
            {
                _filtered = raw;
                _hasFiltered = true;
            }
            else
            {
                float alpha = 1f - Mathf.Exp(-dt / smoothingSeconds);
                _filtered += (raw - _filtered) * alpha;
            }
        }

        private void LearnVelocitySign(Vector3 centre, Vector3 forward, float v)
        {
            if (_velocitySign != 0) return;
            if (!_hasSignRef || Mathf.Abs(v) < 0.05f)
            {
                _signRef = centre;
                _hasSignRef = true;
                return;
            }

            Vector3 moved = centre - _signRef;
            if (moved.sqrMagnitude < SignLearnMeters * SignLearnMeters) return;
            float along = Vector3.Dot(moved, forward);
            if (Mathf.Abs(along) < 0.5f * moved.magnitude) { _signRef = centre; return; } // jumped (rerail, teleport)
            _velocitySign = (along > 0f) == (v > 0f) ? 1 : -1;
        }
    }
}
