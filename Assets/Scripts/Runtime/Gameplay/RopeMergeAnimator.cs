using System;
using DG.Tweening;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    public class RopeMergeAnimator : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private RopeCollisionTracker collisions;
        [SerializeField] private float hopHeight = 1f;
        [SerializeField] private float hopDuration = 0.3f;
        [SerializeField] private float landHeight = 0.5f;
        [SerializeField] private Ease hopEase = Ease.OutSine;
        [SerializeField] private float flipAngle = 180f;
        [SerializeField] private Vector3 popScale = new(0.5f, 0f, 0.5f);
        [SerializeField] private float popDuration = 0.1875f;
        [SerializeField] private float shrinkDuration = 0.1125f;

        public event Action<Vector3> MergeStarted;
        public event Action<Vector3, Color> MergePopped;
        public event Action<Vector3> MergeCompleted;

        public void Play(BoardModel.Rope rope)
        {
            if (!level.RopeViews.TryGetValue(rope.Id, out var ropeView)) return;
            var moverPin = BoardModel.MergeSource(rope);
            var anchorPin = rope.Other(moverPin);
            var source = level.PinViews[moverPin.Id].transform;
            var target = level.PinViews[anchorPin.Id].transform;
            bool targetGoes = BoardModel.IsLastRopeOf(anchorPin, rope);
            Vector3 meet = target.position;

            collisions.Ignore(ropeView.Obi);
            source.DOKill();
            if (targetGoes) target.DOKill();
            level.SetPinPlaced(moverPin.Id, false);
            MergeStarted?.Invoke(meet);

            // Targeting the rope view lets a level reset (rope despawn) kill the whole sequence.
            var seq = DOTween.Sequence().SetTarget(ropeView);
            seq.Append(source.DOJump(meet + Vector3.up * landHeight, hopHeight, 1, hopDuration).SetEase(hopEase));
            seq.Join(source.DORotateQuaternion(source.rotation * Quaternion.AngleAxis(flipAngle, Vector3.right), hopDuration));
            float length = ropeView.RestLength;
            seq.Join(DOTween.To(() => length, v => { ropeView.ChangeLength(v - length); length = v; }, 0f, hopDuration));
            seq.Append(source.DOPunchScale(popScale, popDuration, 1, 1f));
            if (targetGoes) seq.Join(target.DOPunchScale(popScale, popDuration, 1, 1f));
            var pop = meet + Vector3.up * landHeight;
            var color = ropeView.Color;
            seq.AppendCallback(() =>
            {
                ropeView.gameObject.SetActive(false);
                MergePopped?.Invoke(pop, color);
            });
            seq.Append(source.DOScale(0f, shrinkDuration));
            if (targetGoes) seq.Join(target.DOScale(0f, shrinkDuration));
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
