using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 테이저 탄이 플레이어 몸에 박혀 있는 연출.
/// 서버가 맞은 지점에서 가장 가까운 래그돌 본을 골라 본 기준 pose를 계산하고,
/// 각 클라는 네트워크 없는 시각용 탄을 해당 본의 자식으로 붙여 몸을 따라다니게 한다.
/// </summary>
[RequireComponent(typeof(RagdollNetworkSync))]
public class PlayerTaserStuck : NetworkBehaviour
{
    // 본 콜라이더 표면을 찾을 때 탄 진행 방향 반대로 물러나는 거리
    private const float SURFACE_PROBE_DISTANCE = 1f;

    /// <summary>
    /// 래그돌 콜라이더가 실제 메시보다 안쪽에 있는 본을 보정하는 값
    /// </summary>
    [System.Serializable]
    private struct BoneSurfaceOffset
    {
        public string boneName;
        public float offset; // 콜라이더 표면에서 바깥(법선 방향)으로 밀어낼 거리
    }

    [SerializeField] private GameObject stuckVisualPrefab; // 메시만 있는 프리팹 (NetworkObject 없음)
    [SerializeField] private float embedDepth = 0.05f;     // 표면에서 몸 안쪽으로 파고드는 깊이
    [SerializeField] private bool showToOwner = false;     // 맞은 본인 1인칭 화면에도 보일지

    [Header("본별 표면 보정")]
    [SerializeField] private float defaultSurfaceOffset = 0f; // 목록에 없는 본에 적용
    [SerializeField] private BoneSurfaceOffset[] boneSurfaceOffsets =
    {
        new() { boneName = "Hips", offset = 0.06f },
        new() { boneName = "Spine", offset = 0.08f },
        new() { boneName = "Head", offset = 0.05f },
    };

    private RagdollNetworkSync _ragdollSync;
    private readonly List<GameObject> _stuckVisuals = new();

    private void Awake()
    {
        _ragdollSync = GetComponent<RagdollNetworkSync>();
    }

    public override void OnNetworkDespawn()
    {
        ClearVisuals();
    }

    /// <summary>
    /// [서버 전용] 맞은 지점에서 가장 가까운 본 표면에 탄을 박는다
    /// </summary>
    /// <param name="hitPoint">탄이 플레이어와 닿은 월드 위치</param>
    /// <param name="bulletRotation">탄의 진행 회전</param>
    public void AttachServer(Vector3 hitPoint, Quaternion bulletRotation)
    {
        if (!IsServer)
        {
            return;
        }

        Vector3 forward = bulletRotation * Vector3.forward;
        if (!TryFindClosestBone(hitPoint, forward, out int boneIndex, out Vector3 surfacePoint, out Vector3 surfaceNormal))
        {
            return;
        }

        Transform bone = _ragdollSync.GetBone(boneIndex);

        // 콜라이더 표면을 메시 표면 근처까지 바깥으로 밀어낸 뒤, 진행 방향으로 살짝 파고들게 한다
        Vector3 meshSurfacePoint = surfacePoint + surfaceNormal * GetSurfaceOffset(bone.name);
        Vector3 embeddedPoint = meshSurfacePoint + forward * embedDepth;

        AttachClientRpc(
            boneIndex,
            bone.InverseTransformPoint(embeddedPoint),
            Quaternion.Inverse(bone.rotation) * bulletRotation);
    }

    /// <summary>
    /// [서버 전용] 박혀 있는 탄을 모든 클라에서 제거
    /// </summary>
    public void ClearServer()
    {
        if (!IsServer)
        {
            return;
        }

        ClearClientRpc();
    }

    /// <summary>
    /// 본 콜라이더 표면까지 거리가 가장 짧은 본을 찾는다. 콜라이더가 없는 본은 본 원점으로 대체
    /// </summary>
    private bool TryFindClosestBone(
        Vector3 hitPoint, Vector3 forward,
        out int boneIndex, out Vector3 surfacePoint, out Vector3 surfaceNormal)
    {
        boneIndex = -1;
        surfacePoint = hitPoint;
        surfaceNormal = -forward;
        float bestSqr = float.MaxValue;
        Collider bestCollider = null;

        for (int i = 0; i < _ragdollSync.BoneCount; i++)
        {
            Transform bone = _ragdollSync.GetBone(i);
            if (bone == null)
            {
                continue;
            }

            Vector3 candidate = bone.position;
            if (bone.TryGetComponent(out Collider boneCollider) && boneCollider.enabled)
            {
                candidate = boneCollider.ClosestPoint(hitPoint);
            }
            else
            {
                boneCollider = null;
            }

            float sqr = (candidate - hitPoint).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                boneIndex = i;
                surfacePoint = candidate;
                bestCollider = boneCollider;
            }
        }

        if (boneIndex < 0)
        {
            return false;
        }

        // 탄이 날아온 방향으로 본 표면에 닿는 지점을 다시 잡아, 실제로 그 방향에서 꽂힌 것처럼 보이게 한다
        if (bestCollider != null)
        {
            Ray ray = new Ray(surfacePoint - forward * SURFACE_PROBE_DISTANCE, forward);
            if (bestCollider.Raycast(ray, out RaycastHit hit, SURFACE_PROBE_DISTANCE * 2f))
            {
                surfacePoint = hit.point;
                surfaceNormal = hit.normal;
            }
        }

        return true;
    }

    private float GetSurfaceOffset(string boneName)
    {
        foreach (BoneSurfaceOffset entry in boneSurfaceOffsets)
        {
            if (entry.boneName == boneName)
            {
                return entry.offset;
            }
        }

        return defaultSurfaceOffset;
    }

    [ClientRpc]
    private void AttachClientRpc(int boneIndex, Vector3 localPosition, Quaternion localRotation)
    {
        if (stuckVisualPrefab == null)
        {
            Debug.LogWarning("[PlayerTaserStuck] stuckVisualPrefab이 할당되지 않았습니다.", this);
            return;
        }

        Transform bone = _ragdollSync.GetBone(boneIndex);
        if (bone == null)
        {
            return;
        }

        GameObject visual = Instantiate(stuckVisualPrefab, bone);
        visual.transform.SetLocalPositionAndRotation(localPosition, localRotation);

        // 시각용이므로 래그돌 물리와 부딪히지 않게 콜라이더를 끈다
        foreach (Collider visualCollider in visual.GetComponentsInChildren<Collider>(true))
        {
            visualCollider.enabled = false;
        }

        if (IsOwner && !showToOwner)
        {
            foreach (Transform child in visual.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = RagdollDriver.LOCAL_HIDDEN_LAYER;
            }
        }

        _stuckVisuals.Add(visual);
    }

    [ClientRpc]
    private void ClearClientRpc()
    {
        ClearVisuals();
    }

    private void ClearVisuals()
    {
        foreach (GameObject visual in _stuckVisuals)
        {
            if (visual != null)
            {
                Destroy(visual);
            }
        }

        _stuckVisuals.Clear();
    }
}
