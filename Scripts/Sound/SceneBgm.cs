using UnityEngine;

/// <summary>
/// 씬에 두면 씬이 시작될 때 지정한 BGM을 튼다. 같은 곡이 이미 나오고 있으면 끊기지 않고 이어진다.
/// </summary>
public class SceneBgm : MonoBehaviour
{
    [Tooltip("이 씬에서 틀 배경음악. 비워두면 아래 옵션에 따라 기존 곡을 멈추거나 그대로 둔다")]
    [SerializeField] private SoundData bgm;
    [SerializeField] private float fadeDuration = 1f;
    [Tooltip("bgm이 비어 있을 때 이전 씬의 곡을 멈출지")]
    [SerializeField] private bool stopIfEmpty = true;

    private void Start()
    {
        if (bgm != null)
        {
            SoundManager.Instance?.PlayBGM(bgm, fadeDuration);
        }
        else if (stopIfEmpty)
        {
            SoundManager.Instance?.StopBGM(fadeDuration);
        }
    }
}
