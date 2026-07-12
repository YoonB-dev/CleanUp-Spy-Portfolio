using UnityEngine;

/// <summary>
/// 붙잡을 때 팔 뻗는 동작을 만드는 스크립트. <br/>
/// PlayerGrab이 알려주는 상태를 보고 팔 뼈를 목표 방향으로 곧게 겨냥하고, 팔 길이 밖이면 길이 축만 늘여 닿게 한다.
/// (자연스러운 팔꿈치 굽힘은 2본 IK가 필요 — 캐릭터 모델 확정 후 별도 작업)
/// </summary>
[RequireComponent(typeof(PlayerGrab))]
public class PlayerArmReach : MonoBehaviour
{
    [Header("팔 본 이름")]
    [SerializeField] private string upperArmBoneName = "mixamorig:RightArm";
    [SerializeField] private string foreArmBoneName = "mixamorig:RightForeArm";
    [SerializeField] private string handBoneName = "mixamorig:RightHand";

    [Header("팔 뻗기 연출")]
    [Tooltip("시선 정면 방향으로 뻗는 정도")]
    [SerializeField] private float reachForward = 1f;
    [Tooltip("화면 우측으로 치우치는 정도(클수록 오른쪽)")]
    [SerializeField] private float reachRight = 0.4f;
    [Tooltip("화면 아래로 치우치는 정도(클수록 하단)")]
    [SerializeField] private float reachDown = 0.35f;
    [Tooltip("팔을 뻗고 접는 속도(클수록 빠릿함)")]
    [SerializeField] private float reachBlendSpeed = 12f;
    [Tooltip("흐느적거리는 흔들림 세기(도)")]
    [SerializeField] private float swayAngle = 5f;
    [Tooltip("흐느적거리는 흔들림 속도")]
    [SerializeField] private float swaySpeed = 5f;
    [Tooltip("손이 잡은 지점보다 얼마나 앞에서 멈출지. 손이 관통하면 키우고, 손이 안 닿으면 줄이기")]
    [SerializeField] private float handInset = 0.2f;

    private float maxGrabStretch = 3f;

    private PlayerGrab _grab;
    private Camera _playerCamera;

    private Transform _upperArmBone;
    private Transform _foreArmBone;
    private Transform _handBone;
    private Vector3 _upperArmForwardLocal;    // 윗팔 로컬 길이 방향(겨냥용)
    private Vector3 _foreArmForwardLocal;     // 아랫팔 로컬 길이 방향
    private Vector3 _upperArmBaseScale = Vector3.one;
    private int _upperArmStretchAxis = -1;    // 윗팔 길이 방향 축(길이 축만 늘려 얇게 유지)
    private float _naturalUpperLen;   // 어깨 ~ 팔꿈치 길이 원본
    private float _naturalLowerLen;   // 팔꿈치 ~ 손 길이 원본
    private float _reachWeight;       // 0 ~ 1, 팔이 뻗어진 정도

    /// <summary>늘이지 않은 팔의 어깨 ~ 손 길이(월드). PlayerGrab이 당겨오는 거리 계산에 사용.</summary>
    public float ArmReach => _naturalUpperLen + _naturalLowerLen;

    private void Awake()
    {
        _grab = GetComponent<PlayerGrab>();
        _playerCamera = GetComponentInChildren<Camera>(true);

        _upperArmBone = FindBoneByName(transform, upperArmBoneName);
        _foreArmBone = FindBoneByName(transform, foreArmBoneName);
        _handBone = FindBoneByName(transform, handBoneName);

        if (_upperArmBone != null && _foreArmBone != null)
        {
            _upperArmBaseScale = _upperArmBone.localScale;
            _upperArmForwardLocal = _foreArmBone.localPosition.normalized;
            _upperArmStretchAxis = DominantAxisIndex(_foreArmBone.localPosition);
        }

        if (_foreArmBone != null && _handBone != null)
        {
            _foreArmForwardLocal = _handBone.localPosition.normalized;
        }

        if (_upperArmBone != null && _foreArmBone != null && _handBone != null)
        {
            _naturalUpperLen = Vector3.Distance(_upperArmBone.position, _foreArmBone.position);
            _naturalLowerLen = Vector3.Distance(_foreArmBone.position, _handBone.position);
        }
    }

    // 애니메이터 포즈가 적용된 뒤(LateUpdate) 팔만 덮어쓴다. 모든 클라이언트에서 실행.
    private void LateUpdate()
    {
        bool active = _grab != null && (_grab.IsReaching || _grab.IsGrabbing);
        float targetWeight = active ? 1f : 0f;
        _reachWeight = Mathf.MoveTowards(_reachWeight, targetWeight, reachBlendSpeed * Time.deltaTime);

        if (_reachWeight <= 0.0001f || _upperArmBone == null || _foreArmBone == null)
        {
            RestoreArmScale();
            return;
        }

        Vector3 shoulderPos = _upperArmBone.position;
        Vector3 targetPoint = GetReachTargetPoint(shoulderPos);
        Vector3 toTarget = targetPoint - shoulderPos;
        Vector3 reachDir = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized : transform.forward;

        // 팔 길이 밖이면 길이 축만 늘여 닿게 함
        float naturalFull = _naturalUpperLen + _naturalLowerLen;
        float targetDist = toTarget.magnitude;
        if (_upperArmStretchAxis >= 0 && naturalFull > 0.001f && targetDist > naturalFull)
        {
            float stretch = Mathf.Min(targetDist / naturalFull, maxGrabStretch);
            Vector3 scale = _upperArmBaseScale;
            scale[_upperArmStretchAxis] = _upperArmBaseScale[_upperArmStretchAxis] * Mathf.Lerp(1f, stretch, _reachWeight);
            _upperArmBone.localScale = scale;
        }
        else
        {
            _upperArmBone.localScale = _upperArmBaseScale;
        }

        // 윗팔/아랫팔을 같은 방향으로 겨냥해 곧게 뻗은 팔.
        AimBoneAlong(_upperArmBone, reachDir, _upperArmForwardLocal);
        AimBoneAlong(_foreArmBone, reachDir, _foreArmForwardLocal);
    }

    // 겨냥할 목표 지점. 붙잡는 중이면 잡은 지점(손 두께만큼 앞), 아니면 시선 방향으로 뻗은 가상의 점.
    private Vector3 GetReachTargetPoint(Vector3 shoulderPos)
    {
        if (_grab.IsGrabbing && _grab.TryGetGrabWorldPoint(out Vector3 grabPoint))
        {
            Vector3 toGrab = grabPoint - shoulderPos;
            float dist = toGrab.magnitude;
            if (dist > 1e-4f)
            {
                return shoulderPos + toGrab / dist * Mathf.Max(0f, dist - handInset);
            }
            return grabPoint;
        }

        // 리치 제스처: 시선 방향으로 팔 길이보다 살짝 밖을 목표로
        Vector3 reachDir = GetReachWorldDirection();
        float reachLen = (_naturalUpperLen + _naturalLowerLen) * 1.05f;
        return shoulderPos + reachDir * reachLen;
    }

    // 리치 제스처가 향할 방향. 붙잡을 땐 안 쓰임.
    private Vector3 GetReachWorldDirection()
    {
        Vector3 forward;
        Vector3 right;
        Vector3 up;

        if (_grab.IsOwner && _playerCamera != null)
        {
            Transform camTransform = _playerCamera.transform;   // Owner: 실제 카메라 방향
            forward = camTransform.forward;
            right = camTransform.right;
            up = camTransform.up;
        }
        else
        {
            // 그 외: 몸통 방향 + 동기화된 상하 각도로 시선 복원
            right = transform.right;
            forward = Quaternion.AngleAxis(-_grab.LookElevation, right) * transform.forward;
            up = Vector3.Cross(right, forward);
        }

        Vector3 direction = forward * reachForward + right * reachRight - up * reachDown;

        // 흐느적 흔들림(잡는 중엔 이 경로를 안 타므로 자동으로 흔들림 없음).
        float sway = Mathf.Sin(Time.time * swaySpeed) * swayAngle;
        direction = Quaternion.AngleAxis(sway, transform.up) * direction;

        return direction.normalized;
    }

    // 뼈를 worldDir 쪽으로 돌린다. 한 번에 안 꺾고 가중치(_reachWeight)만큼만 섞어 부드럽게.
    private void AimBoneAlong(Transform bone, Vector3 worldDir, Vector3 boneForwardLocal)
    {
        if (bone == null || boneForwardLocal == Vector3.zero || worldDir.sqrMagnitude < 1e-6f)
        {
            return;
        }

        Vector3 currentForwardWorld = bone.rotation * boneForwardLocal;
        Quaternion delta = Quaternion.FromToRotation(currentForwardWorld, worldDir);
        Quaternion aimed = delta * bone.rotation;
        bone.rotation = Quaternion.Slerp(bone.rotation, aimed, _reachWeight);
    }

    // 원래 길이로 복원
    private void RestoreArmScale()
    {
        if (_upperArmBone != null)
        {
            _upperArmBone.localScale = _upperArmBaseScale;
        }
    }

    // 절댓값이 가장 큰 축 인덱스(0=x,1=y,2=z) = 자식 본이 놓인 방향 = 뼈의 길이 축
    private static int DominantAxisIndex(Vector3 v)
    {
        Vector3 abs = new(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        if (abs.x >= abs.y && abs.x >= abs.z)
        {
            return 0;
        }

        return abs.y >= abs.z ? 1 : 2;
    }

    // 이름으로 본을 재귀 탐색
    private static Transform FindBoneByName(Transform root, string boneName)
    {
        if (string.IsNullOrEmpty(boneName))
        {
            return null;
        }

        if (root.name == boneName)
        {
            return root;
        }

        for (int index = 0; index < root.childCount; index++)
        {
            Transform found = FindBoneByName(root.GetChild(index), boneName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
