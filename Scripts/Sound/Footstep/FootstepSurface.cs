using UnityEngine;

/// <summary>
/// 바닥의 표면 종류를 지정한다. 콜라이더가 있는 바닥 오브젝트나 그 부모에 붙인다.
/// 부모에 붙이면 하위 바닥 조각 전부에 적용되고, 하위 조각에 직접 붙이면 그쪽이 우선한다.
/// (Synty 등 에셋 원본 프리팹 대신 씬의 부모 그룹이나 프리팹 Variant에 붙이는 것을 권장)
/// </summary>
public class FootstepSurface : MonoBehaviour
{
    [SerializeField] private SurfaceType surfaceType = SurfaceType.Default;

    /// <summary>
    /// 밟은 콜라이더의 표면 종류를 찾습니다. 자신과 부모에서 가장 가까운 FootstepSurface를 쓰고, 없으면 Default
    /// </summary>
    public static SurfaceType Resolve(Collider ground)
    {
        if (ground == null) return SurfaceType.Default;

        FootstepSurface surface = ground.GetComponentInParent<FootstepSurface>();
        return surface != null ? surface.surfaceType : SurfaceType.Default;
    }
}
