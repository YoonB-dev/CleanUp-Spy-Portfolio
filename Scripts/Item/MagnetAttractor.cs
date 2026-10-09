using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 전방 원뿔(Cone) 범위를 대상으로 지정한 카테고리(attractTargets)의 아이템을 끌어당기고 노즐 앞에 고정시키는 흡입기 스크립트.
/// </summary>
[RequireComponent(typeof(PickupItem))]
public class MagnetAttractor : NetworkBehaviour, IUsableItem
{
    [Header("Cone Settings")]
    [Tooltip("흡입 노즐/주둥이 위치 (없으면 이 오브젝트의 Transform 사용)")]
    [SerializeField] private Transform nozzlePoint;

    [Tooltip("최대 흡입 사거리")]
    [SerializeField] private float maxDistance = 7.0f;

    [Tooltip("흡입 원뿔의 전방 개폐 각도")]
    [Range(10f, 180f)]
    [SerializeField] private float coneAngle = 60.0f;

    [Header("Magnet Force Settings")]
    [Tooltip("최대 사거리 근처에서 적용될 최소 자력")]
    [SerializeField] private float minForce = 15.0f;

    [Tooltip("노즐 근처로 올수록 증가하는 최대 자력")]
    [SerializeField] private float maxForce = 60.0f;

    [Tooltip("자력 감쇄 곡선 (가까울수록 급격히 강해짐)")]
    [SerializeField] private float forceExponent = 1.8f;

    [Header("Hold / Snap Settings")]
    [Tooltip("노즐 입구 기준 쓰레기가 붙어서 멈출 위치(거리)")]
    [SerializeField] private float holdDistance = 0.8f;
    [Tooltip("포획 위치 중심 기준, 딱 붙들어서 고정시킬 영역의 반지름 (인스펙터 조절)")]
    [SerializeField] private float holdRadius = 1.5f;

    [Tooltip("포획 위치 안으로 들어왔을 때 물체를 딱 붙들어 매는 추적 힘")]
    [SerializeField] private float holdSpringForce = 30.0f;

    [Tooltip("물체가 관성으로 지나치지 못하도록 감속시키는 감쇄력")]
    [SerializeField] private float dampening = 10.0f;

    [Header("Layer Settings")]
    [Tooltip("끌려올 아이템들이 포함된 레이어")]
    [SerializeField] private LayerMask itemLayerMask;

    [Header("Target Settings")]
    [Tooltip("끌어당길 아이템 카테고리와 카테고리별 힘 배율 (목록에 없는 카테고리는 끌지 않음)")]
    [SerializeField] private AttractTarget[] attractTargets =
    {
        new AttractTarget { category = PickupCategory.Trash, forceMultiplier = 1.0f },
        new AttractTarget { category = PickupCategory.Box, forceMultiplier = 0.5f },
    };

    [System.Serializable]
    private struct AttractTarget
    {
        public PickupCategory category;
        [Tooltip("당기는 힘/고정 힘에 곱해지는 배율 (무거운 물체일수록 낮게)")]
        public float forceMultiplier;
    }

    // ===== 내부 변수 =====
    private PickupItem _thisPickupItem;
    private Transform _actualNozzlePoint;

    private readonly NetworkVariable<bool> _isMagnetActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsMagnetActive => _isMagnetActive.Value;

    private void Awake()
    {
        _thisPickupItem = GetComponent<PickupItem>();
        _actualNozzlePoint = nozzlePoint != null ? nozzlePoint : transform;
    }
    public void OnUse(InputAction.CallbackContext context, Camera playerCamera)
    {
        if (context.performed)
        {
            SetMagnetStateServerRpc(true);
        }
        else if (context.canceled)
        {
            SetMagnetStateServerRpc(false);
        }
    }

    [ServerRpc]
    public void SetMagnetStateServerRpc(bool active)
    {
        if (!IsServer) return;
        _isMagnetActive.Value = active;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // 자석이 켜져있고, 이 아이템을 누군가가 들고 있을 때만 작동
        if (!_isMagnetActive.Value || !_thisPickupItem.IsHeld) return;

        ApplyConeMagneticForce();
    }

    private void ApplyConeMagneticForce()
    {
        Vector3 origin = _actualNozzlePoint.position;
        Vector3 forward = _actualNozzlePoint.forward;

        // 실제 쓰레기가 잡혀서 멈출 목표 위치 (노즐 전방 holdDistance 지점)
        Vector3 holdTargetPosition = origin + (forward * holdDistance);

        Collider[] hits = Physics.OverlapSphere(origin, maxDistance, itemLayerMask);

        foreach (var col in hits)
        {
            if (col.gameObject == gameObject) continue;

            if (col.TryGetComponent<PickupItem>(out var targetItem))
            {
                if (targetItem.IsHeld || !TryGetForceMultiplier(targetItem.Category, out float forceMultiplier)) continue;
                if (!col.TryGetComponent<Rigidbody>(out var targetRb) || targetRb.isKinematic) continue;

                Vector3 toTargetFromOrigin = targetRb.position - origin;
                float distFromOrigin = toTargetFromOrigin.magnitude;

                if (distFromOrigin <= 0.001f) continue;

                Vector3 dirToTarget = toTargetFromOrigin / distFromOrigin;

                // 목표 고정점(holdTargetPosition)과의 실제 거리 계산
                Vector3 toHoldTarget = holdTargetPosition - targetRb.position;
                float distToHoldTarget = toHoldTarget.magnitude;

                // 1. 포획 범위(holdRadius) 내부 진입 판정 (원뿔 각도 검사 예외 구역)
                if (distToHoldTarget <= holdRadius)
                {
                    // 목표 위치로 강하게 고정 + 감쇄력 + 중력 상쇄 (중력 상쇄는 배율 없이 그대로 적용해야 떨어지지 않음)
                    Vector3 springForce = toHoldTarget * (holdSpringForce * forceMultiplier);
                    Vector3 dampForce = -targetRb.linearVelocity * dampening;

                    targetRb.AddForce(springForce + dampForce, ForceMode.Acceleration);
                    targetRb.AddForce(-Physics.gravity, ForceMode.Acceleration);
                    continue;
                }

                // 2. 멀리 있는 물체는 원뿔(Cone) 각도 판정
                float angle = Vector3.Angle(forward, dirToTarget);
                if (angle > coneAngle * 0.5f) continue;

                // 3. 끌어당기는 힘 계산
                float normalizedDistance = Mathf.Clamp01(1.0f - (distFromOrigin / maxDistance));
                float curveFactor = Mathf.Pow(normalizedDistance, forceExponent);
                float currentForce = Mathf.Lerp(minForce, maxForce, curveFactor) * forceMultiplier;

                Vector3 pullDirection = toHoldTarget.normalized;
                Vector3 forceToApply = pullDirection * currentForce;
                Vector3 dampingToApply = -targetRb.linearVelocity * (dampening * 0.5f);

                targetRb.AddForce(forceToApply + dampingToApply, ForceMode.Acceleration);
            }
        }
    }

    /// <summary>
    /// 끌어당길 대상 카테고리인지 확인하고, 해당 카테고리의 힘 배율을 가져옵니다.
    /// </summary>
    private bool TryGetForceMultiplier(PickupCategory category, out float forceMultiplier)
    {
        foreach (var target in attractTargets)
        {
            if (target.category == category)
            {
                forceMultiplier = target.forceMultiplier;
                return true;
            }
        }
        forceMultiplier = 0f;
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Transform point = nozzlePoint != null ? nozzlePoint : transform;

        // 실제 쓰레기가 붙을 목표 지점 및 포획 범위(Hold Radius) 시각화
        Vector3 holdPos = point.position + (point.forward * holdDistance);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(holdPos, holdRadius); // 인스펙터 holdRadius 크기대로 구체가 그려집니다.

        // 원뿔 외곽선 및 사거리
        Gizmos.color = Color.yellow;
        Vector3 forward = point.forward;
        Quaternion leftRay = Quaternion.AngleAxis(-coneAngle * 0.5f, point.up);
        Quaternion rightRay = Quaternion.AngleAxis(coneAngle * 0.5f, point.up);
        Quaternion upRay = Quaternion.AngleAxis(-coneAngle * 0.5f, point.right);
        Quaternion downRay = Quaternion.AngleAxis(coneAngle * 0.5f, point.right);

        Gizmos.DrawRay(point.position, leftRay * forward * maxDistance);
        Gizmos.DrawRay(point.position, rightRay * forward * maxDistance);
        Gizmos.DrawRay(point.position, upRay * forward * maxDistance);
        Gizmos.DrawRay(point.position, downRay * forward * maxDistance);
    }
}