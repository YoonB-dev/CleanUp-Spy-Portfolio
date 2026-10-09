// 소품과 래그돌 사이의 충돌 제외 설정

using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 소품 Collider와 래그돌 Collider 사이의 충돌 제외
/// </summary>
public class IgnoreRagdollCollision : MonoBehaviour
{
    [FormerlySerializedAs("ragdollRoot")]
    [SerializeField]
    [Tooltip("충돌 제외 대상 래그돌 루트")]
    private Transform _ragdollRoot;

    /// <summary>
    /// 에디터 도구용 래그돌 루트 설정 및 조회
    /// </summary>
    public Transform RagdollRoot
    {
        get => _ragdollRoot;
        set => _ragdollRoot = value;
    }

    /// <summary>
    /// 소품과 래그돌의 Collider 조합에 충돌 제외 적용
    /// </summary>
    private void Start()
    {
        if (_ragdollRoot == null)
        {
            Debug.LogWarning($"[{name}] 래그돌 루트가 없어 충돌 제외를 건너뜁니다.", this);
            return;
        }

        Collider[] itemColliders = GetComponentsInChildren<Collider>();
        Collider[] ragdollColliders = _ragdollRoot.GetComponentsInChildren<Collider>();

        foreach (Collider itemCollider in itemColliders)
        {
            foreach (Collider ragdollCollider in ragdollColliders)
            {
                if (itemCollider == ragdollCollider)
                {
                    continue;
                }

                Physics.IgnoreCollision(itemCollider, ragdollCollider, true);
            }
        }
    }
}
