using UnityEngine;

/// <summary>
/// 붙잡을 때 팔 뻗는 동작을 만드는 스크립트. <br/>
/// PlayerGrab이 알려주는 상태(뻗는 중/잡는 중/보는 방향/잡은 위치)를 보고,
/// 캐릭터 팔 뼈를 코드로 돌리고 늘려서 손이 상대에게 가도록 한다.
/// </summary>
[RequireComponent(typeof(PlayerGrab))]
public class PlayerArmReach : MonoBehaviour
{
    [Header("팔 본 이름")]
    [SerializeField] private string upperArmBoneName = "mixamorig:RightArm";
    [SerializeField] private string foreArmBoneName = "mixamorig:RightForeArm";
    [SerializeField] private string handBoneName = "mixamorig:RightHand";

    [Header("팔 뻗기 연출")]
    [Tooltip("팔을 뻗었을 때 길이 배율(1=원본, 클수록 길어짐)")]
    [SerializeField] private float armStretch = 1.6f;
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

    private const float GRAB_HAND_INSET = 0.2f;   // 손이 잡은 지점을 뚫지 않게 앞에서 멈추는 여유 수치
    private const float GRAB_MAX_STRETCH = 6f;    // 팔 늘이기 배율 MAX 값

    private PlayerGrab _grab;
    private Camera _playerCamera;

    private Transform _upperArmBone;
    private Transform _foreArmBone;
    private Transform _handBone;
    private Vector3 _upperArmForwardLocal;     // 윗팔 로컬 길이 방향
    private Vector3 _foreArmForwardLocal;      // 아랫팔 로컬 길이 방향
    private Vector3 _upperArmBaseScale = Vector3.one;
    private Vector3 _foreArmBaseScale = Vector3.one;
    private int _upperArmStretchAxis = -1;     // 윗팔 길이 방향 축(0=x,1=y,2=z)
    private float _naturalArmLength;           // 늘이지 않은 어깨~손 길이(늘이기 배율 계산용)
    private float _reachWeight;                // 0~1, 팔이 뻗어진 정도

    private void Awake()
    {
        _grab = GetComponent<PlayerGrab>();
        _playerCamera = GetComponentInChildren<Camera>(true);

        _upperArmBone = FindBoneByName(transform, upperArmBoneName);
        _foreArmBone = FindBoneByName(transform, foreArmBoneName);
        _handBone = FindBoneByName(transform, handBoneName);

        // 본의 길이 방향/스케일 축을 자식 본 위치로 미리 계산
        if (_upperArmBone != null)
        {
            _upperArmBaseScale = _upperArmBone.localScale;
            if (_foreArmBone != null)
            {
                _upperArmForwardLocal = _foreArmBone.localPosition.normalized;
                _upperArmStretchAxis = DominantAxisIndex(_foreArmBone.localPosition);
            }
        }

        if (_foreArmBone != null)
        {
            _foreArmBaseScale = _foreArmBone.localScale;
            if (_handBone != null)
            {
                _foreArmForwardLocal = _handBone.localPosition.normalized;
            }
        }

        if (_upperArmBone != null && _foreArmBone != null && _handBone != null)
        {
            _naturalArmLength = Vector3.Distance(_upperArmBone.position, _foreArmBone.position)
                              + Vector3.Distance(_foreArmBone.position, _handBone.position);
        }
    }

    // 애니메이터 포즈 위에 팔을 겨냥해 뻗음. 애니메이터 포즈 위에 팔만 덮어써야 하기 때문에 LateUpdate 사용
    private void LateUpdate()
    {
        bool active = _grab != null && (_grab.IsReaching || _grab.IsGrabbing);
        float targetWeight = active ? 1f : 0f;
        _reachWeight = Mathf.MoveTowards(_reachWeight, targetWeight, reachBlendSpeed * Time.deltaTime);

        if (_reachWeight <= 0.0001f)
        {
            RestoreArmScale();
            return;
        }

        Vector3 reachDir = GetReachWorldDirection();

        // 붙잡는 중엔 손이 대상에 고정되도록 흔들지 않음
        if (!_grab.IsGrabbing)
        {
            float sway = Mathf.Sin(Time.time * swaySpeed) * swayAngle;
            reachDir = Quaternion.AngleAxis(sway, transform.up) * reachDir;
        }

        // 윗팔/아랫팔을 같은 방향으로 겨냥
        AimBoneAlong(_upperArmBone, reachDir, _upperArmForwardLocal);
        AimBoneAlong(_foreArmBone, reachDir, _foreArmForwardLocal);

        // 윗팔만 늘리기(아랫팔은 상속으로 함께 늘어남)
        float stretch = ComputeCurrentStretch();
        ApplyStretch(_upperArmBone, _upperArmBaseScale, _upperArmStretchAxis, stretch);
    }

    // 잡은 지점에 손이 닿도록 목표거리/기본팔길이 배율 계산
    private float ComputeCurrentStretch()
    {
        if (_grab.IsGrabbing && _naturalArmLength > 0.001f && _grab.TryGetGrabWorldPoint(out Vector3 targetPoint))
        {
            Vector3 shoulderPos = _upperArmBone != null ? _upperArmBone.position : transform.position;
            float reachDistance = Mathf.Max(0f, Vector3.Distance(shoulderPos, targetPoint) - GRAB_HAND_INSET);
            return Mathf.Clamp(reachDistance / _naturalArmLength, 1f, GRAB_MAX_STRETCH);
        }

        return armStretch;
    }

    // 팔이 향할 방향. 붙잡는 중이면 잡은 지점, 아니면 시선 따라가도록 설정
    private Vector3 GetReachWorldDirection()
    {
        if (_grab.IsGrabbing && _grab.TryGetGrabWorldPoint(out Vector3 aimPoint))
        {
            Vector3 shoulderPos = _upperArmBone != null ? _upperArmBone.position : transform.position;
            Vector3 toTarget = aimPoint - shoulderPos;
            if (toTarget.sqrMagnitude > 1e-6f)
            {
                return toTarget.normalized;
            }
        }

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

    // 길이 방향 축만 스케일해 팔 늘이기
    private void ApplyStretch(Transform bone, Vector3 baseScale, int stretchAxis, float stretch)
    {
        if (bone == null || stretchAxis < 0)
        {
            return;
        }

        float factor = Mathf.Lerp(1f, stretch, _reachWeight);
        Vector3 scale = baseScale;
        scale[stretchAxis] = baseScale[stretchAxis] * factor;
        bone.localScale = scale;
    }

    // 원래 길이로 복원
    private void RestoreArmScale()
    {
        if (_upperArmBone != null)
        {
            _upperArmBone.localScale = _upperArmBaseScale;
        }

        if (_foreArmBone != null)
        {
            _foreArmBone.localScale = _foreArmBaseScale;
        }
    }

    // 절댓값이 가장 큰 축 인덱스(0=x,1=y,2=z)
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
