using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 버튼에 소리 역할을 지정한다. Default가 아닌 소리가 필요한 버튼이나,
/// 실행 중에 생성되는 버튼(목록 항목 등) 프리팹에 붙인다.
/// 이 컴포넌트가 붙은 버튼은 UIButtonSoundBinder가 건너뛴다.
/// </summary>
[RequireComponent(typeof(Button))]
public class UIButtonSound : MonoBehaviour
{
    [SerializeField] private UISoundType soundType = UISoundType.Default;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(PlaySound);
    }

    private void PlaySound()
    {
        UISoundProfile.Play(soundType);
    }
}
