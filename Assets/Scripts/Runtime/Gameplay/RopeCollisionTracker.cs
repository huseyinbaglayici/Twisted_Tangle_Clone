using System.Collections.Generic;
using Obi;
using UnityEngine;

namespace Runtime.Gameplay
{
    // Which ropes are touching which, from Obi particle contacts.
    // A contact counts only when the ropes actually touch (distance <= maxContactDistance) and one lies on the other
    // (|normal . up| >= minContactNormalAbsDot) — ropes merely side by side don't count. Contacts are sampled every
    // updatePeriod and forgotten after resetPeriod without being seen again.
    public class RopeCollisionTracker : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private float updatePeriod = 0.125f;
        [SerializeField] private float resetPeriod = 0.2f;
        [SerializeField] private float minContactNormalAbsDot = 0.4f;
        [SerializeField] private float maxContactDistance = 0.01f;

        private readonly Dictionary<ObiActor, Dictionary<ObiActor, float>> _contacts = new();
        private readonly List<ObiActor> _expired = new();
        private ObiSolver _solver;
        private float _lastContactSample = float.MinValue;
        private float _lastCleanup = float.MinValue;

        private void Start()
        {
            _solver = level.Solver;
            if (_solver != null) _solver.OnParticleCollision += HandleParticleContacts;
        }

        private void OnDestroy()
        {
            if (_solver != null) _solver.OnParticleCollision -= HandleParticleContacts;
        }

        public void Clear() => _contacts.Clear();

        public bool IsColliding(ObiActor actor) =>
            actor != null && _contacts.TryGetValue(actor, out var with) && with.Count > 0;

        public bool IsCollidingWith(ObiActor a, ObiActor b) =>
            a != null && _contacts.TryGetValue(a, out var with) && with.ContainsKey(b);

        private void FixedUpdate()
        {
            if (Time.time - _lastCleanup < updatePeriod) return;
            _lastCleanup = Time.time;

            foreach (var kv in _contacts)
            {
                _expired.Clear();
                foreach (var other in kv.Value)
                    if (other.Key == null || Time.time - other.Value > resetPeriod)
                        _expired.Add(other.Key);
                foreach (var e in _expired) kv.Value.Remove(e);
            }
        }

        private void HandleParticleContacts(ObiSolver solver, ObiNativeContactList contacts)
        {
            if (Time.time - _lastContactSample < updatePeriod) return;
            _lastContactSample = Time.time;

            for (int i = 0; i < contacts.count; i++)
            {
                var c = contacts[i];
                if (c.distance > maxContactDistance) continue;
                if (Mathf.Abs(Vector3.Dot(c.normal, Vector3.up)) < minContactNormalAbsDot) continue;

                var a = ActorOf(solver, c.bodyA);
                var b = ActorOf(solver, c.bodyB);
                if (a == null || b == null || a == b) continue;
                Touch(a, b);
                Touch(b, a);
            }
        }

        private void Touch(ObiActor a, ObiActor b)
        {
            if (!_contacts.TryGetValue(a, out var with)) _contacts[a] = with = new Dictionary<ObiActor, float>();
            with[b] = Time.time;
        }

        private static ObiActor ActorOf(ObiSolver solver, int simplex)
        {
            int start = solver.simplexCounts.GetSimplexStartAndSize(simplex, out int size);
            if (size <= 0) return null;
            int particle = solver.simplices[start];
            var map = solver.particleToActor;
            return particle >= 0 && particle < map.Length ? map[particle]?.actor : null;
        }
    }
}
