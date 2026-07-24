using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

/// <summary>
/// 앵커 기반 액티브 래그돌 이동 및 시점 제어
/// </summary>
public class RagdollDriver : MonoBehaviour
{
    private const string HIPS_BONE_NAME = "Hips";
    private const string HEAD_BONE_NAME = "Head";
    private const string SPINE_BONE_NAME = "Spine";
    private const string CHEST_BONE_NAME = "Chest";
    private const string LEFT_SHOULDER_BONE_NAME = "Shoulder.L";
    private const string RIGHT_SHOULDER_BONE_NAME = "Shoulder.R";
    private const float MOVE_INPUT_THRESHOLD = 0.01f;

    // hips가 앵커에서 이 거리 이상 벗어나면 되돌린다 (누적 드리프트 방지)
    private const float ANCHOR_LEASH_DISTANCE = 0.4f;

    // 1인칭 메인 카메라에서 자기 머리를 숨길 레이어 + 대상 렌더러. Body(팔 손 몸통)는 유지.
    private const int LOCAL_HIDDEN_LAYER = 30;
    private static readonly string[] OWNER_HIDDEN_RENDERER_NAMES =
    {
        "Head_Face", "Cap", "Ear_Cap", "Head_Robe_1", "Face_Glass", "Brooch"
    };

    [Header("연결")]
    [FormerlySerializedAs("hips")]
    [SerializeField]
    private Transform _hips;

    [FormerlySerializedAs("head")]
    [SerializeField]
    private Transform _head;

    [Header("퍼펫 모드")]
    [FormerlySerializedAs("anchorSpring")]
    [SerializeField]
    private float _anchorSpring = 200f;

    [FormerlySerializedAs("anchorDamper")]
    [SerializeField]
    private float _anchorDamper = 30f;

    [FormerlySerializedAs("anchorUprightSpring")]
    [SerializeField]
    private float _anchorUprightSpring = 2000f;

    [FormerlySerializedAs("anchorUprightDamper")]
    [SerializeField]
    private float _anchorUprightDamper = 30f;

    [FormerlySerializedAs("anchorMaxForce")]
    [SerializeField]
    private float _anchorMaxForce = 3000f;

    [Header("이동")]
    [FormerlySerializedAs("flipBodyForward")]
    [SerializeField]
    private bool _flipBodyForward;

    [Header("상체 안정화")]
    [Tooltip("가속 시 척추/가슴/머리가 쏠리지 않게 상체 관절을 굳히는 스프링")]
    [SerializeField]
    private float _torsoSpring = 4000f;

    [Tooltip("상체 관절의 감쇠")]
    [SerializeField]
    private float _torsoDamper = 200f;

    private Rigidbody _hipsRigidbody;
    private Rigidbody _anchorRigidbody;
    private ConfigurableJoint _anchorJoint;
    private Vector3 _anchorLocalPosition;
    private Quaternion _anchorLocalRotation = Quaternion.identity;
    private bool _isLimp;
    private float _yaw;
    private float _pitch;
    private Quaternion _bodyRotationOffset = Quaternion.identity;
    private Vector3 _playerToHipsOffset;
    private Transform _playerTransform;
    private PlayerMovement _playerMovement;
    private FirstPersonLook _firstPersonLook;
    private bool _wasMovementEnabled;
    private bool _wasLookEnabled;

    /// <summary>
    /// 에디터 도구용 Hips Transform 설정 및 조회
    /// </summary>
    public Transform Hips
    {
        get => _hips;
        set => _hips = value;
    }

    /// <summary>
    /// 에디터 도구용 Head Transform 설정 및 조회
    /// </summary>
    public Transform Head
    {
        get => _head;
        set => _head = value;
    }

    /// <summary>
    /// 현재 이동 입력 여부
    /// </summary>
    public bool IsMoving =>
        _playerMovement.MoveInput.sqrMagnitude > MOVE_INPUT_THRESHOLD;

    /// <summary>
    /// 현재 죽은 척 상태 여부
    /// </summary>
    public bool IsLimp => _isLimp;

    /// <summary>
    /// 물리를 이 인스턴스에서 시뮬할지 여부 (서버 또는 비네트워크). 클라는 네트워크 수신만.
    /// </summary>
    public bool IsServerAuthoritative =>
        NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;

    /// <summary>
    /// 현재 조준 회전
    /// </summary>
    public Quaternion AimRotation => Quaternion.Euler(_pitch, _yaw, 0f);

    /// <summary>
    /// 현재 시선 상하 각도 (양수=아래, 음수=위)
    /// </summary>
    public float Pitch => _pitch;

    /// <summary>
    /// 골격 기준 몸체 회전
    /// </summary>
    public Quaternion BodyRotation =>
        _hips != null ? _hips.rotation * _bodyRotationOffset : transform.rotation;

    /// <summary>
    /// Transform 및 Rigidbody 참조 초기화
    /// </summary>
    private void Awake()
    {
        _playerMovement = GetComponentInParent<PlayerMovement>();
        _firstPersonLook = GetComponentInParent<FirstPersonLook>();

        if (_playerMovement == null || _firstPersonLook == null)
        {
            throw new MissingComponentException(
                "[RagdollDriver] PlayerMovement와 FirstPersonLook이 있는 플레이어 아래에 배치해야 합니다.");
        }

        _playerTransform = _playerMovement.transform;

        if (transform.parent != _playerTransform)
        {
            throw new MissingComponentException(
                "[RagdollDriver] 액티브 래그돌은 Player의 직계 자식이어야 합니다.");
        }

        // 래그돌의 저작된 로컬 오프셋(발=원점을 캡슐 바닥으로 내리는 -Y)을 유지한다.
        // (0,0,0)으로 리셋하면 발이 캡슐 중심으로 떠올라 공중에 뜬다.

        if (_hips == null)
        {
            _hips = FindBone(HIPS_BONE_NAME);
        }

        if (_head == null)
        {
            _head = FindBone(HEAD_BONE_NAME);
        }

        if (_hips == null || _head == null)
        {
            throw new MissingComponentException(
                "[RagdollDriver] Hips와 Head 골격이 필요합니다.");
        }

        _hipsRigidbody = _hips.GetComponent<Rigidbody>();

        if (_hipsRigidbody == null)
        {
            throw new MissingComponentException(
                "[RagdollDriver] Hips에 Rigidbody가 필요합니다.");
        }

        ComputeBodyFrame();
        _playerToHipsOffset =
            Quaternion.Inverse(BodyRotation) *
            (_hips.position - _playerTransform.position);

        // 분리(Start의 SetParent) 전에 동기화 컴포넌트에 본을 넘긴다
        RagdollNetworkSync networkSync = GetComponentInParent<RagdollNetworkSync>();
        if (networkSync != null)
        {
            networkSync.BindRagdoll(transform);
        }
    }

    /// <summary>
    /// 초기 시점 및 퍼펫 앵커 설정
    /// </summary>
    private void Start()
    {
        _yaw = _firstPersonLook.Yaw;
        HideOwnHeadFromOwnerCamera();

        if (IsServerAuthoritative)
        {
            SetupAnchor();
            StiffenTorso();
        }
        else
        {
            SetupClientKinematic();
        }

        // 동적 래그돌을 매 프레임 이동/회전하는 Player에서 분리해 월드 공간에 둔다.
        // 부모로 두면 물리 스텝마다 부모 이동량만큼 본이 텔레포트되어 물리 적분과 충돌 → 휘청거림.
        transform.SetParent(null, true);

        // 분리 후엔 계층상 고아이므로 Player가 파괴 시 함께 정리하도록 소유권을 넘긴다.
        _playerMovement.RegisterOwnedRagdoll(gameObject);
    }

    /// <summary>
    /// 척추/가슴/머리 관절을 굳혀 가속 시 상체가 채찍처럼 쏠리는 것을 억제.
    /// 상체를 하나로 움직이게 해 머리 쏠림을 줄인다.
    /// </summary>
    private void StiffenTorso()
    {
        foreach (string boneName in
                 new[] { SPINE_BONE_NAME, CHEST_BONE_NAME, HEAD_BONE_NAME })
        {
            Transform bone = FindBone(boneName);
            ConfigurableJoint joint =
                bone != null ? bone.GetComponent<ConfigurableJoint>() : null;

            if (joint == null)
            {
                continue;
            }

            JointDrive drive = joint.slerpDrive;
            drive.positionSpring = _torsoSpring;
            drive.positionDamper = _torsoDamper;
            joint.slerpDrive = drive;
        }
    }

    /// <summary>
    /// 클라 전용: 물리 시뮬 없이 본을 kinematic으로 두고 네트워크 포즈 수신 대기
    /// </summary>
    private void SetupClientKinematic()
    {
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
        }

        // 클라 래그돌은 시각용이므로 콜라이더를 꺼 로컬 물리(동적 오브젝트)를 밀지 않게 한다
        foreach (Collider bodyCollider in GetComponentsInChildren<Collider>(true))
        {
            bodyCollider.enabled = false;
        }
    }

    /// <summary>
    /// 1인칭 메인 카메라에서만 자기 머리를 숨김
    /// 원본은 숨김 레이어로 메인캠에서 제외하고, 그림자는 ShadowsOnly 프록시가 대신 드리운다. 오너 클라 한정.
    /// </summary>
    private void HideOwnHeadFromOwnerCamera()
    {
        if (!_firstPersonLook.IsOwner)
        {
            return;
        }

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (System.Array.IndexOf(OWNER_HIDDEN_RENDERER_NAMES, renderer.name) < 0)
            {
                continue;
            }

            CreateShadowProxy(renderer);

            // 원본: 메인캠에서만 숨김. 그림자는 프록시가 담당하므로 원본 그림자는 끔
            renderer.gameObject.layer = LOCAL_HIDDEN_LAYER;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        _firstPersonLook.ExcludeLayerFromCamera(LOCAL_HIDDEN_LAYER);
    }

    /// <summary>
    /// 머리 렌더러를 복제해 보이는 레이어에서 그림자만 드리우는 프록시 생성.
    /// SkinnedMesh 본은 외부 골격을 참조하므로 Instantiate 후에도 원본 골격에 따라 움직인다.
    /// </summary>
    private void CreateShadowProxy(Renderer source)
    {
        // source가 숨김 레이어로 바뀌기 전에 복제해 프록시는 보이는 레이어를 유지
        GameObject proxy = Instantiate(source.gameObject, source.transform.parent);
        proxy.name = source.name + "_ShadowProxy";
        proxy.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
    }

    /// <summary>
    /// 죽은 척 상태를 전환하고 좌표계 주도권을 변경
    /// </summary>
    /// <param name="isLimp">죽은 척 상태 여부</param>
    public void SetLimp(bool isLimp)
    {
        if (_isLimp == isLimp)
        {
            return;
        }

        _isLimp = isLimp;

        if (isLimp)
        {
            _wasMovementEnabled = _playerMovement.enabled;
            _wasLookEnabled = _firstPersonLook.enabled;
            _playerMovement.enabled = false;
            _firstPersonLook.enabled = false;
        }
        else
        {
            SyncPlayerToRagdoll();
            _playerMovement.enabled = _wasMovementEnabled;
            _firstPersonLook.enabled = _wasLookEnabled;
        }

        ApplyAnchorDrives();
    }

    /// <summary>
    /// 골격 배치 기준 몸체 회전 계산
    /// </summary>
    private void ComputeBodyFrame()
    {
        Transform leftShoulder = FindBone(LEFT_SHOULDER_BONE_NAME);
        Transform rightShoulder = FindBone(RIGHT_SHOULDER_BONE_NAME);

        if (leftShoulder == null || rightShoulder == null)
        {
            throw new MissingComponentException(
                "[RagdollDriver] Shoulder.L과 Shoulder.R 골격이 필요합니다.");
        }

        Vector3 up = (_head.position - _hips.position).normalized;
        Vector3 right = (rightShoulder.position - leftShoulder.position).normalized;
        Vector3 forward = Vector3.Cross(right, up).normalized;

        if (_flipBodyForward)
        {
            forward = -forward;
        }

        up = Vector3.Cross(forward, right).normalized;
        _bodyRotationOffset =
            Quaternion.Inverse(_hips.rotation) * Quaternion.LookRotation(forward, up);
    }

    /// <summary>
    /// Hips 추적용 물리 앵커 및 관절 생성
    /// </summary>
    private void SetupAnchor()
    {
        _hipsRigidbody.isKinematic = false;

        // 앵커를 플레이어 자식으로 매달지 않는다. 부모 추종은 PhysX 속도를 0으로 만들어
        // 조인트 댐퍼가 이동 속도를 제동 -> 이동 중 휘청거림. FixedUpdate에서 MovePosition으로 몬다.
        _anchorLocalPosition = _playerTransform.InverseTransformPoint(_hips.position);
        // 수평 오프셋 제거: yaw 회전 시 앵커가 피벗을 공전해 hips가 밀려나는 것 방지
        _anchorLocalPosition.x = 0f;
        _anchorLocalPosition.z = 0f;
        _anchorLocalRotation = Quaternion.Inverse(_playerTransform.rotation) * _hips.rotation;

        GameObject anchorObject = new GameObject("Puppet_Anchor");
        anchorObject.transform.SetPositionAndRotation(_hips.position, _hips.rotation);

        _anchorRigidbody = anchorObject.AddComponent<Rigidbody>();
        _anchorRigidbody.isKinematic = true;
        _anchorRigidbody.useGravity = false;

        _anchorJoint = _hips.gameObject.AddComponent<ConfigurableJoint>();
        _anchorJoint.connectedBody = _anchorRigidbody;
        _anchorJoint.autoConfigureConnectedAnchor = false;
        _anchorJoint.anchor = Vector3.zero;
        _anchorJoint.connectedAnchor = Vector3.zero;
        _anchorJoint.xMotion = ConfigurableJointMotion.Free;
        _anchorJoint.yMotion = ConfigurableJointMotion.Free;
        _anchorJoint.zMotion = ConfigurableJointMotion.Free;
        _anchorJoint.angularXMotion = ConfigurableJointMotion.Free;
        _anchorJoint.angularYMotion = ConfigurableJointMotion.Free;
        _anchorJoint.angularZMotion = ConfigurableJointMotion.Free;
        _anchorJoint.rotationDriveMode = RotationDriveMode.Slerp;
        _anchorJoint.enablePreprocessing = false;

        EnableBoneInterpolation();
        IgnorePlayerCollision();
        ApplyAnchorDrives();
    }

    /// <summary>
    /// 물리 본의 렌더 보간을 켜 50Hz 물리와 렌더 프레임 사이 떨림을 줄인다
    /// </summary>
    private void EnableBoneInterpolation()
    {
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
        {
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    /// <summary>
    /// 월드 공간 래그돌이 플레이어 캡슐(CharacterController 등)에 밀리지 않도록 충돌 제외.
    /// 바닥·벽 등 월드와의 충돌은 그대로 유지된다.
    /// </summary>
    private void IgnorePlayerCollision()
    {
        Collider[] playerColliders = _playerTransform.GetComponents<Collider>();
        Collider[] ragdollColliders = GetComponentsInChildren<Collider>(true);

        foreach (Collider playerCollider in playerColliders)
        {
            foreach (Collider ragdollCollider in ragdollColliders)
            {
                Physics.IgnoreCollision(playerCollider, ragdollCollider, true);
            }
        }
    }

    /// <summary>
    /// 런타임 생성 앵커 제거
    /// </summary>
    private void OnDestroy()
    {
        if (_anchorRigidbody != null)
        {
            Destroy(_anchorRigidbody.gameObject);
        }
    }

    /// <summary>
    /// 퍼펫 상태에 따른 앵커 위치 및 회전 Drive 갱신
    /// </summary>
    private void ApplyAnchorDrives()
    {
        if (_anchorJoint == null)
        {
            return;
        }

        float weight = _isLimp ? 0f : 1f;
        JointDrive positionDrive = new JointDrive
        {
            positionSpring = _anchorSpring * weight,
            positionDamper = _anchorDamper * weight,
            maximumForce = _anchorMaxForce
        };

        _anchorJoint.xDrive = positionDrive;
        _anchorJoint.yDrive = positionDrive;
        _anchorJoint.zDrive = positionDrive;
        _anchorJoint.slerpDrive = new JointDrive
        {
            positionSpring = _anchorUprightSpring * weight,
            positionDamper = _anchorUprightDamper * weight,
            maximumForce = _anchorMaxForce
        };
    }

    /// <summary>
    /// 플레이어 이동 및 시점 상태 갱신
    /// </summary>
    private void Update()
    {
        if (_firstPersonLook == null)
        {
            return;
        }

        _yaw = _firstPersonLook.Yaw;
        _pitch = _firstPersonLook.Pitch;
    }

    /// <summary>
    /// 죽은 척 상태에서 Player 좌표계를 래그돌 몸체에 동기화
    /// </summary>
    private void FixedUpdate()
    {
        if (!IsServerAuthoritative || _playerTransform == null)
        {
            return;
        }

        if (_isLimp)
        {
            SyncPlayerToRagdoll();
            return;
        }

        DriveAnchor();
    }

    /// <summary>
    /// Player 현재 좌표계로 앵커를 물리 이동시켜 조인트에 목표 속도를 전달
    /// </summary>
    private void DriveAnchor()
    {
        if (_anchorRigidbody == null)
        {
            return;
        }

        Vector3 targetPosition = _playerTransform.TransformPoint(_anchorLocalPosition);
        Quaternion targetRotation = _playerTransform.rotation * _anchorLocalRotation;
        _anchorRigidbody.MovePosition(targetPosition);
        _anchorRigidbody.MoveRotation(targetRotation);

        // 하드 리쉬: 스프링이 못 따라가 hips가 크게 벗어나면 되돌려 누적 드리프트 차단
        Vector3 stray = _hipsRigidbody.position - targetPosition;
        if (stray.sqrMagnitude > ANCHOR_LEASH_DISTANCE * ANCHOR_LEASH_DISTANCE)
        {
            _hipsRigidbody.position =
                targetPosition + stray.normalized * ANCHOR_LEASH_DISTANCE;
        }
    }

    /// <summary>
    /// Player 좌표계를 현재 래그돌 몸체 좌표계에 정렬
    /// </summary>
    private void SyncPlayerToRagdoll()
    {
        Quaternion rotation = BodyRotation;
        Vector3 position = _hips.position - rotation * _playerToHipsOffset;
        _playerTransform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>
    /// Play 모드의 Inspector 값 변경을 앵커 Drive에 반영
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyAnchorDrives();
        }
    }

    /// <summary>
    /// 앵커와 Hips 사이의 거리를 Scene 뷰에 표시
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || _anchorRigidbody == null || _hips == null)
        {
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(_anchorRigidbody.position, 0.08f);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(_hips.position, 0.06f);
        Gizmos.color = Color.white;
        Gizmos.DrawLine(_anchorRigidbody.position, _hips.position);
    }

    /// <summary>
    /// 이름으로 하위 골격 Transform 검색
    /// </summary>
    /// <param name="boneName">검색할 골격 이름</param>
    /// <returns>골격 Transform. 없으면 null</returns>
    private Transform FindBone(string boneName)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name == boneName)
            {
                return child;
            }
        }

        return null;
    }
}
