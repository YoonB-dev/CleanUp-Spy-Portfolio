using UnityEngine;

/// <summary>
/// 물건이 맞았을 때(펀치 등) 낼 소리를 지정한다. 오브젝트 루트(NetworkObject가 있는 곳)에 붙인다.
/// 아이템은 ItemData.hitSound로 종류별 소리를 정할 수 있으므로, 이 컴포넌트는
/// ItemData가 없는 물건(파쇄기 등)이나 특정 프리팹만 소리를 바꾸고 싶을 때 사용한다.
/// </summary>
public class ImpactSound : MonoBehaviour
{
    [SerializeField] private SoundData hitSound;

    /// <summary>
    /// 오브젝트가 맞았을 때 낼 소리를 찾습니다.
    /// 순서: ImpactSound 컴포넌트 → PickupItem의 ItemData.hitSound → fallback
    /// </summary>
    /// <param name="target">맞은 오브젝트의 루트</param>
    /// <param name="fallback">어디에도 설정되지 않았을 때 쓸 기본 소리</param>
    public static SoundData Resolve(GameObject target, SoundData fallback)
    {
        if (target == null) return fallback;

        if (target.TryGetComponent(out ImpactSound impact) && impact.hitSound != null)
        {
            return impact.hitSound;
        }

        // 쓰레기는 종류(TrashData)마다 다른 소리가 나도록 ItemData에서 가져옴
        if (target.TryGetComponent(out PickupItem item) && item.ItemData != null && item.ItemData.hitSound != null)
        {
            return item.ItemData.hitSound;
        }

        return fallback;
    }
}
