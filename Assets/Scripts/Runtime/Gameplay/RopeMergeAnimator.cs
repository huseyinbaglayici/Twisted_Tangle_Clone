using System;
using DG.Tweening;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    // A free rope merges: the source pin (first end holding only this rope) jumps onto the target pin, flipping
    // over, while the rope reels in. Then both punch, the rope disappears, the pins scale out and everything returns
    // to the pools. A target pin still held by another rope stays on the board untouched.
    public class RopeMergeAnimator : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private RopeCollisionTracker collisions;
        [SerializeField] private float jumpPower = 1f;
        [SerializeField] private float jumpDuration = 0.3f;
        [SerializeField] private float jumpTargetOffset = 0.5f;   // lands this high above the target pin
        [SerializeField] private Ease jumpEase = Ease.OutSine;
        [SerializeField] private float flipAngle = 180f;
        [SerializeField] private Vector3 punchScale = new(0.5f, 0f, 0.5f);
        [SerializeField] private float punchDuration = 0.1875f;
        [SerializeField] private float destroyScaleDuration = 0.1125f;

        // World position where the pins meet (the target pin).
        public event Action<Vector3> MergeStarted;
        // The rope vanishes: where the pins landed and the rope's colour.
        public event Action<Vector3, Color> MergePopped;
        public event Action<Vector3> MergeCompleted;

        public void Play(BoardModel.Rope rope)
        {
            if (!level.RopeViews.TryGetValue(rope.Id, out var ropeView)) return;
            var sourcePin = BoardModel.MergeSource(rope);
            var targetPin = rope.Other(sourcePin);
            var source = level.PinViews[sourcePin.Id].transform;
            var target = level.PinViews[targetPin.Id].transform;
            bool targetGoes = BoardModel.IsLastRopeOf(targetPin, rope);
            Vector3 meet = target.position;

            collisions.Ignore(ropeView.Obi);
            source.DOKill();
            if (targetGoes) target.DOKill();
            level.SetPinPlaced(sourcePin.Id, false);
            MergeStarted?.Invoke(meet);

            // Target = rope view, so a level reset (rope despawn) kills the whole sequence.
            var seq = DOTween.Sequence().SetTarget(ropeView);
            seq.Append(source.DOJump(meet + Vector3.up * jumpTargetOffset, jumpPower, 1, jumpDuration).SetEase(jumpEase));
            seq.Join(source.DORotateQuaternion(source.rotation * Quaternion.AngleAxis(flipAngle, Vector3.right), jumpDuration));
            // Rope reels in (rest length -> 0) while the pin is in the air.
            float length = ropeView.RestLength;
            seq.Join(DOTween.To(() => length, v => { ropeView.ChangeLength(v - length); length = v; }, 0f, jumpDuration));
            seq.Append(source.DOPunchScale(punchScale, punchDuration, 1, 1f));
            if (targetGoes) seq.Join(target.DOPunchScale(punchScale, punchDuration, 1, 1f));
            var pop = meet + Vector3.up * jumpTargetOffset;
            var color = ropeView.Color;
            seq.AppendCallback(() =>
            {
                ropeView.gameObject.SetActive(false);
                MergePopped?.Invoke(pop, color);
            });
            seq.Append(source.DOScale(0f, destroyScaleDuration));
            if (targetGoes) seq.Join(target.DOScale(0f, destroyScaleDuration));
            seq.OnComplete(() =>
            {
                level.Board.CompleteMerge(rope, out bool removeA, out bool removeB);
                collisions.Unignore(ropeView.Obi);
                level.ReleaseRope(rope.Id);
                if (removeA) level.ReleasePin(rope.A.Id);
                if (removeB) level.ReleasePin(rope.B.Id);
                MergeCompleted?.Invoke(meet);
            });
        }
    }
}
