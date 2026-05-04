using System.Collections;
using UnityEngine;

namespace BergischeDiakonie.Speech
{
    public class CharacterAnimator : MonoBehaviour
    {
        [Range(0.5f, 5f)] public float minPause = 0.5f;
        [Range(1f, 8f)]  public float maxPause = 3f;

        Animator _animator;
        AnimationClip[] _clips;

        void Start()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null) { enabled = false; return; }

            if (_animator.runtimeAnimatorController != null)
                _clips = _animator.runtimeAnimatorController.animationClips;

            if (_clips == null || _clips.Length == 0)
            {
                Debug.LogWarning("[CharacterAnimator] Keine AnimationClips gefunden.");
                enabled = false;
                return;
            }

            StartCoroutine(Cycle());
        }

        IEnumerator Cycle()
        {
            while (true)
            {
                var clip = _clips[Random.Range(0, _clips.Length)];
                _animator.Play(clip.name, 0, 0f);
                yield return new WaitForSeconds(clip.length + Random.Range(minPause, maxPause));
            }
        }
    }
}
