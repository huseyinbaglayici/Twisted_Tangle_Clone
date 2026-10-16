using System.Collections.Generic;
using Obi;
using UnityEngine;

namespace Runtime.Gameplay
{
    // Rope-on-rope contacts from Obi particle collisions. A contact counts only when the ropes really touch
    // (distance <= maxContactDistance) and one lies on the other (|normal . up| >= minContactNormalAbsDot), so ropes
    // merely side by side don't count. Contacts are sampled every sampleInterval and forgotten after contactLifetime.
    // A rope touching a collider of blockingColliderCategory (the funnel posts) counts as colliding too: it can't
    // free itself while it leans on a post.
    public class RopeCollisionTracker : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private float sampleInterval = 0.125f;
        [SerializeField] private float contactLifetime = 0.2f;
        [SerializeField] private float minContactNormalAbsDot = 0.4f;
        [SerializeField] private float maxContactDistance = 0.01f;
        [SerializeField] private int blockingColliderCategory = 10;

        private readonly Dictionary<ObiActor, Dictionary<ObiActor, float>> _contacts = new();
        private readonly Dictionary<ObiActor, float> _postContacts = new();
        private readonly List<ObiActor> _expired = new();
        private readonly HashSet<ObiActor> _ignored = new();
        private ObiSolver _solver;
        private float _lastContactSample = float.MinValue;
        private float _lastPostSample = float.MinValue;
        private float _lastCleanup = float.MinValue;

        private void Start()
        {
            _solver = level.Solver;
            if (_solver == null) return;
            _solver.OnParticleCollision += HandleParticleContacts;
            _solver.OnCollision += HandleColliderContacts;
        }

        private void OnDestroy()
        {
            if (_solver == null) return;
            _solver.OnParticleCollision -= HandleParticleContacts;
            _solver.OnCollision -= HandleColliderContacts;
        }

        public void Clear()
        {
            _contacts.Clear();
            _postContacts.Clear();
            _ignored.Clear();
        }

        // A merging rope reels in across the board; its contacts must not keep other ropes from freeing.
        public void Ignore(ObiActor actor)
        {
            if (actor == null) return;
            _ignored.Add(actor);
            _contacts.Remove(actor);
            _postContacts.Remove(actor);
            foreach (var with in _contacts.Values) with.Remove(actor);
        }

        public void Unignore(ObiActor actor) => _ignored.Remove(actor);

        public bool IsColliding(ObiActor actor) =>
            actor != null && ((_contacts.TryGetValue(actor, out var with) && with.Count > 0) || _postContacts.ContainsKey(actor));

        public bool IsCollidingWith(ObiActor a, ObiActor b) =>
            a != null && _contacts.TryGetValue(a, out var with) && with.ContainsKey(b);

        private void FixedUpdate()
        {
            if (Time.time - _lastCleanup < sampleInterval) return;
            _lastCleanup = Time.time;

            foreach (var kv in _contacts)
            {
                _expired.Clear();
                foreach (var other in kv.Value)
                    if (other.Key == null || Time.time - other.Value > contactLifetime)
                        _expired.Add(other.Key);
                foreach (var e in _expired) kv.Value.Remove(e);
            }

            _expired.Clear();
            foreach (var kv in _postContacts)
                if (kv.Key == null || Time.time - kv.Value > contactLifetime)
                    _expired.Add(kv.Key);
            foreach (var e in _expired) _postContacts.Remove(e);
        }

        private void HandleParticleContacts(ObiSolver solver, ObiNativeContactList contacts)
        {
            if (Time.time - _lastContactSample < sampleInterval) return;
            _lastContactSample = Time.time;

            for (int i = 0; i < contacts.count; i++)
            {
                var c = contacts[i];
                if (c.distance > maxContactDistance) continue;
                if (Mathf.Abs(Vector3.Dot(c.normal, Vector3.up)) < minContactNormalAbsDot) continue;

                var a = ActorOf(solver, c.bodyA);
                var b = ActorOf(solver, c.bodyB);
                if (a == null || b == null || a == b || _ignored.Contains(a) || _ignored.Contains(b)) continue;
                Touch(a, b);
                Touch(b, a);
            }
        }

        private void HandleColliderContacts(ObiSolver solver, ObiNativeContactList contacts)
        {
            if (Time.time - _lastPostSample < sampleInterval) return;
            _lastPostSample = Time.time;

            var shapes = ObiColliderWorld.GetInstance().colliderShapes;
            int category = 1 << blockingColliderCategory;
            for (int i = 0; i < contacts.count; i++)
            {
                var c = contacts[i];
                if (c.distance > maxContactDistance || c.bodyB < 0 || c.bodyB >= shapes.count) continue;
                if ((shapes[c.bodyB].filter & category) == 0) continue;

                var rope = ActorOf(solver, c.bodyA);
                if (rope == null || _ignored.Contains(rope)) continue;
                _postContacts[rope] = Time.time;
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
