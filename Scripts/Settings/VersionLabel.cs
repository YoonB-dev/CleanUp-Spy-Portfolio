using TMPro;
using UnityEngine;

/// <summary>
/// 빌드 버전을 표시한다. 값은 Player Settings의 Version을 그대로 쓴다.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class VersionLabel : MonoBehaviour
{
    [SerializeField] private string prefix = "v";

    private void OnEnable() => GetComponent<TMP_Text>().text = prefix + Application.version;
}
