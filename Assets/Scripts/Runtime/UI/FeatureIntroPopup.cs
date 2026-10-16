using System;
using System.Collections.Generic;
using DG.Tweening;
using Runtime.Level;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Runtime.UI
{
    // Explains a level feature the first time it shows up. The parts pop in one after another; a tap pops the next
    // one straight away, and once everything is shown the next tap closes the popup.
    [RequireComponent(typeof(UiPanelAnimator))]
    public class FeatureIntroPopup : MonoBehaviour, IPointerClickHandler
    {
        [Serializable]
        private class Entry
        {
            public LevelFeature feature;
            public string title;
            [Tooltip("What the feature is.")]
            public string description;
            [Tooltip("What to do about it.")]
            public string instruction;
        }

        [SerializeField] private RectTransform titlePart;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text description;
        [SerializeField] private TMP_Text instruction;
        [SerializeField] private RectTransform tapToContinue;
        [SerializeField] private Entry[] entries;

        [Header("Reveal")]
        [SerializeField] private float revealInterval = 0.3f;
        [SerializeField] private float popDuration = 0.15f;

        private readonly List<Transform> _parts = new();
        private int _revealed;
        private Sequence _reveal;
        private UiPanelAnimator _animator;

        public event Action Closed;

        public bool IsVisible => Animator.IsVisible;

        private UiPanelAnimator Animator => _animator != null ? _animator : _animator = GetComponent<UiPanelAnimator>();

        public bool Has(LevelFeature feature) => Array.Exists(entries, e => e.feature == feature);

        public void Show(LevelFeature feature)
        {
            var entry = Array.Find(entries, e => e.feature == feature);
            if (entry == null) return;
            title.text = entry.title;
            description.text = entry.description;
            instruction.text = entry.instruction;
            HideParts();

            Animator.Show();
            _reveal?.Kill();
            RevealNext();
            _reveal = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            for (int i = 1; i < _parts.Count; i++)
                _reveal.AppendInterval(revealInterval).AppendCallback(RevealNext);
        }

        public void Hide(bool instant = false)
        {
            _reveal?.Kill();
            Animator.Hide(instant);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!IsVisible) return;
            if (_revealed < _parts.Count)
            {
                RevealNext();
                return;
            }

            Hide();
            Closed?.Invoke();
        }

        private void RevealNext()
        {
            if (_revealed >= _parts.Count) return;
            var part = _parts[_revealed++];
            part.DOKill();
            part.localScale = Vector3.zero;
            part.gameObject.SetActive(true);
            part.DOScale(1f, popDuration).SetEase(Ease.OutBack).SetUpdate(true).SetLink(part.gameObject);
        }

        private void HideParts()
        {
            if (_parts.Count == 0)
            {
                _parts.Add(titlePart);
                _parts.Add(description.transform);
                _parts.Add(instruction.transform);
                _parts.Add(tapToContinue);
            }

            _revealed = 0;
            foreach (var part in _parts) part.gameObject.SetActive(false);
        }
    }
}
