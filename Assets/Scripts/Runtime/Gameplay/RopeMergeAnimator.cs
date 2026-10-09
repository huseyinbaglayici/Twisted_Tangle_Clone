using DG.Tweening;
using Runtime.Level;
using UnityEngine;

namespace Runtime.Gameplay
{
    // A free rope merges: single-rope pins jump to the meeting point while the rope reels in, punch, then scale out
    // and everything returns to the pools. A pin still holding another rope stays put and becomes the target.
    public class RopeMergeAnimator : MonoBehaviour
    {
        [SerializeField] private LevelBuilder level;
        [SerializeField] private float jumpPower = 1f;
        [SerializeField] private float jumpDuration = 0.3f;
        [SerializeField] private float jumpTargetOffset = 0.5f;
        [SerializeField] private Ease jumpEase = Ease.OutSine;
        [SerializeField] private Vector3 punchScale = new(0.5f, 0f, 0.5f);
        [SerializeField] private float punchDuration = 0.1875f;
        [SerializeField] private float destroyScaleDuration = 0.1125f;

        public void Play(BoardModel.Rope rope)
        {
            if (!level.RopeViews.TryGetValue(rope.Id, out var ropeView)) return;
            var a = level.PinViews[rope.A.Id].transform;
            var b = level.PinViews[rope.B.Id].transform;
            bool aMoves = rope.A.ActiveRopeCount == 1, bMoves = rope.B.ActiveRopeCount == 1;

            Vector3 meet = aMoves && bMoves
                ? Vector3.Lerp(a.position, b.position, jumpTargetOffset)
                : aMoves ? b.position : a.position;

            if (aMoves) { a.DOKill(); level.SetPinPlaced(rope.A.Id, false); }
            if (bMoves) { b.DOKill(); level.SetPinPlaced(rope.B.Id, false); }

            // Target = rope view, so a level reset (rope despawn) kills the whole sequence.
            var seq = DOTween.Sequence().SetTarget(ropeView);
            if (aMoves) seq.Join(a.DOJump(meet, jumpPower, 1, jumpDuration).SetEase(jumpEase));
            if (bMoves) seq.Join(b.DOJump(meet, jumpPower, 1, jumpDuration).SetEase(jumpEase));
            // Rope reels in (rest length -> 0) while the pins jump together.
            float length = ropeView.RestLength;
            seq.Join(DOTween.To(() => length, v => { ropeView.ChangeLength(v - length); length = v; }, 0f, jumpDuration).SetEase(jumpEase));
            seq.AppendCallback(() =>
            {
                if (aMoves) a.DOPunchScale(punchScale, punchDuration, 1, 0f);
                if (bMoves) b.DOPunchScale(punchScale, punchDuration, 1, 0f);
            });
            seq.AppendInterval(punchDuration);
            if (aMoves) seq.Append(a.DOScale(0f, destroyScaleDuration));
            if (bMoves) seq.Join(b.DOScale(0f, destroyScaleDuration));
            seq.OnComplete(() =>
            {
                level.Board.CompleteMerge(rope, out bool removeA, out bool removeB);
                level.ReleaseRope(rope.Id);
                if (removeA) level.ReleasePin(rope.A.Id);
                if (removeB) level.ReleasePin(rope.B.Id);
            });
        }
    }
}
