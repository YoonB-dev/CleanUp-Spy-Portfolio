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
    private const string LEFT_FOREARM_BONE_NAME = "Forearm.L";
    private const string RIGHT_FOREARM_BONE_NAME = "Forearm.R";
    private const string LEFT_SHOULDER_BONE_NAME = "Shoulder.L";
    private const string RIGHT_SHOULDER_BONE_NAME = "Shoulder.R";
    private const float MOVE_INPUT_THRESHOLD = 0.01f;

    // 팔 콜라이더와 몸통/머리 콜라이더의 자기 충돌을 명시적으로 무시
    private static readonly string[] ARM_BONE_NAMES =
    {
        "UpperArm.L", "UpperArm.R", "Forearm.L", "Forearm.R"
    };
    private static readonly string[] TORSO_BONE_NAMES =
    {
        SPINE_BONE_NAME, HEAD_BONE_NAME, HIPS_BONE_NAME
    };

    // hips가 앵커에서 이 거리 이상 벗어나면 되돌린다 (누적 드리프트 방지)
    private const float ANCHOR_LEASH_DISTANCE = 0.4f;

    // hips 아래 지면을 찾는 레이 (몸 관통 방지용 시작 높이 + 최대 탐색 거리).
    // 높은 곳에서 떨어지는 동안에도 지면을 찾아야 캡슐이 몸을 따라 내려간다. 짧으면 중간에 멈춘다
    private const float GROUND_RAY_START_HEIGHT = 0.6f;
    private const float GROUND_RAY_DISTANCE = 200f;

    // 펀치를 감을 때는 타격 반대쪽으로 이만큼 틀어둔다 (허리 회전각 대비 비율)
    private const float PUNCH_WINDUP_YAW_RATIO = 0.45f;

    // 오너 1인칭 카메라에서 숨길 레이어와 항상 숨기는 머리 렌더러. 몸통 등은 인스펙터 추가 목록으로
    public const int LOCAL_HIDDEN_LAYER = 30;
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

    [Header("펀치")]
    [Tooltip("펀치에 실리는 허리 회전 각도. 몸 전체가 스윙을 따라 돌아 휘청인다. 부호 반대면 반대로 돎")]
    [SerializeField]
    [Range(-60f, 60f)]
    private float _punchBodyYaw = 22f;

    [Tooltip("아랫팔 뼈에서 주먹까지의 거리. 타격 판정 위치. 반대로 뻗으면 부호를 뒤집는다")]
    [SerializeField]
    [Range(-0.6f, 0.6f)]
    private float _fistOffset = 0.22f;

    [Header("붙잡힘")]
    [Tooltip("붙잡혔을 때 상체 유지력 배수 (0=완전 흐물, 1=빳빳). 낮을수록 휘청")]
    [SerializeField]
    private float _grabbedUprightFactor = 0.3f;

    [Header("1인칭 숨김")]
    [Tooltip("오너 1인칭에서 머리 외에 추가로 숨길 렌더러 이름. 분리 모델의 몸통(Body, 벨트 등)")]
    [SerializeField]
    private string[] _ownerHiddenExtraRenderers;

    private Rigidbody _hipsRigidbody;
    private Rigidbody _anchorRigidbody;
    private ConfigurableJoint _anchorJoint;
    private Vector3 _anchorLocalPosition;
    private Quaternion _anchorLocalRotation = Quaternion.identity;
    private bool _isLimp;
    private bool _isDiving;
    private bool _isGrabbed;
    private float _yaw;
    private float _pitch;
    private Quaternion _bodyRotationOffset = Quaternion.identity;
    private Transform _playerTransform;
    private PlayerMovement _playerMovement;
    private FirstPersonLook _firstPersonLook;
    private PlayerGrab _playerGrab;
    private PlayerPunch _playerPunch;
    private Transform _leftForearm;
    private Transform _rightForearm;
    private Rigidbody[] _boneBodies;

    // 지면 탐색용 버퍼. 자기 뼈를 걸러내야 해서 다중 히트를 받는다
    private readonly RaycastHit[] _groundHits = new RaycastHit[16];

    private float _lastContactTime = float.NegativeInfinity;   // [서버] 뼈가 외부 물체에 마지막으로 닿은 시각
    private bool _wasMovementEnabled;

    /// <summary>
    /// 이 래그돌의 주인 Player. 래그돌은 월드로 분리돼 있어 계층으로는 거슬러 올라갈 수 없다.
    /// </summary>
    public Transform PlayerRoot => _playerTransform;

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

    /// <summary>다이빙으로 쓰러진 상태인지 (넉다운과 구분)</summary>
    public bool IsDiving => _isDiving;

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
    /// 잡기/뻗기 요청 상태 (오른팔 뻗기 포즈 트리거)
    /// </summary>
    public bool IsReachRequested =>
        _playerGrab != null && (_playerGrab.IsReaching || _playerGrab.IsGrabbing);


    /// <summary>
    /// 물건 집기용
    /// </summary>
    private CarryGripPoints _carryTarget;
    private float _carryGripHalfWidth; // 잡는 순간 한 번만 계산, 이후 고정값
    private float _hipsPitchRatio = 0.1f; // 0~1


    /// <summary>
    /// 현재 펀치 자세 상태. 펀치 중이 아니면 false.
    /// </summary>
    /// <param name="isLeftHand">휘두르는 손이 왼손인지</param>
    /// <param name="weight">펀치 자세 가중치 (0=평상 자세, 1=펀치 자세)</param>
    /// <param name="extension">팔 뻗음 정도 (0=당김, 1=최대로 뻗음)</param>
    public bool TryGetPunchPose(out bool isLeftHand, out float weight, out float extension)
    {
        if (_playerPunch != null)
        {
            return _playerPunch.TryGetPunchPose(out isLeftHand, out weight, out extension);
        }

        isLeftHand = false;
        weight = 0f;
        extension = 0f;
        return false;
    }

    /// <summary>
    /// 붙잡은 대상 지점을 향하도록 어깨(월드)에서 본 조준 yaw/pitch(도)를 계산.
    /// yaw는 몸통 정면 기준 좌우각, pitch는 수평 기준 상하각(양수=아래)으로 뻗기 각 규약과 동일.
    /// 붙잡고 있지 않으면 false.
    /// </summary>
    /// <param name="shoulderWorld">뻗는 팔 어깨의 월드 위치</param>
    /// <param name="yaw">몸통 정면 기준 좌우각(도)</param>
    /// <param name="pitch">수평 기준 상하각(도, 양수=아래)</param>
    public bool TryGetGrabReachAim(Vector3 shoulderWorld, out float yaw, out float pitch)
    {
        yaw = 0f;
        pitch = 0f;

        if (_playerGrab == null ||
            !_playerGrab.TryGetGrabWorldPoint(out Vector3 targetPoint))
        {
            return false;
        }

        // 플레이어는 yaw만 도므로 몸통 프레임에서 x=좌우, y=상하(월드 수직), z=정면이 된다
        Vector3 dir =
            Quaternion.Inverse(_playerTransform.rotation) * (targetPoint - shoulderWorld);
        if (dir.sqrMagnitude < 1e-6f)
        {
            return false;
        }

        dir.Normalize();
        yaw = Mathf.Atan2(dir.x, Mathf.Sqrt(dir.y * dir.y + dir.z * dir.z)) * Mathf.Rad2Deg;
        pitch = Mathf.Atan2(-dir.y, dir.z) * Mathf.Rad2Deg;
        return true;
    }

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
        _playerGrab = GetComponentInParent<PlayerGrab>();
        _playerPunch = GetComponentInParent<PlayerPunch>();

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

        _leftForearm = FindBone(LEFT_FOREARM_BONE_NAME);
        _rightForearm = FindBone(RIGHT_FOREARM_BONE_NAME);
        _boneBodies = GetComponentsInChildren<Rigidbody>(true);

        ComputeBodyFrame();
        BindToPlayerComponents();
    }

    /// <summary>
    /// 분리(Start의 SetParent) 전에 Player 쪽 컴포넌트들에 자신을 넘긴다.
    /// 분리 후에는 계층이 끊겨 서로 찾을 수 없다.
    /// </summary>
    private void BindToPlayerComponents()
    {
        RagdollNetworkSync networkSync = GetComponentInParent<RagdollNetworkSync>();
        if (networkSync != null)
        {
            networkSync.BindRagdoll(transform);
        }

        if (_playerPunch != null)
        {
            _playerPunch.BindRagdoll(this);
        }

        PlayerKnockdown knockdown = GetComponentInParent<PlayerKnockdown>();
        if (knockdown != null)
        {
            knockdown.BindRagdoll(this);
        }
    }

    /// <summary>
    /// 주먹(아랫팔 끝)의 월드 위치. 타격 판정 지점.
    /// </summary>
    /// <param name="isLeftHand">왼손인지</param>
    /// <param name="point">주먹 월드 위치</param>
    public bool TryGetFistPoint(bool isLeftHand, out Vector3 point)
    {
        Transform forearm = isLeftHand ? _leftForearm : _rightForearm;

        if (forearm == null)
        {
            point = Vector3.zero;
            return false;
        }

        // 팔 뼈는 로컬 up이 뼈를 따라 손 방향을 향한다
        point = forearm.position + forearm.up * _fistOffset;
        return true;
    }

    /// <summary>
    /// 래그돌 전체에 같은 속도 변화를 줘 몸이 통째로 날아가게 한다.
    /// 질량과 무관하게 같은 속도를 주므로 팔다리가 뜯겨나가듯 흩어지지 않는다.
    /// </summary>
    /// <param name="velocity">속도 변화량 (m/s)</param>
    public void ApplyKnockbackVelocity(Vector3 velocity)
    {
        if (_boneBodies == null)
        {
            return;
        }

        foreach (Rigidbody body in _boneBodies)
        {
            if (body == null || body.isKinematic)
            {
                continue;
            }

            body.AddForce(velocity, ForceMode.VelocityChange);
        }
    }

    /// <summary>
    /// 모든 본 속도를 지정값으로 덮어쓴다. 이전 이동 관성을 무시하고 일정하게 발사(다이빙).
    /// </summary>
    /// <param name="velocity">설정할 속도 (m/s)</param>
    public void SetBonesVelocity(Vector3 velocity)
    {
        if (_boneBodies == null)
        {
            return;
        }

        foreach (Rigidbody body in _boneBodies)
        {
            if (body == null || body.isKinematic)
            {
                continue;
            }

            body.linearVelocity = velocity;
        }
    }

    /// <summary>
    /// 넉백으로 날아가며 돌도록 모든 본에 각속도를 더한다.
    /// </summary>
    /// <param name="angularVelocity">추가할 각속도 (rad/s, 월드)</param>
    public void ApplyKnockbackSpin(Vector3 angularVelocity)
    {
        if (_boneBodies == null)
        {
            return;
        }

        foreach (Rigidbody body in _boneBodies)
        {
            if (body == null || body.isKinematic)
            {
                continue;
            }

            body.AddTorque(angularVelocity, ForceMode.VelocityChange);
        }
    }

    /// <summary>
    /// 초기 시점 및 퍼펫 앵커 설정
    /// </summary>
    private void Start()
    {
        _yaw = _firstPersonLook.Yaw;
        HideOwnRenderersFromOwnerCamera();

        // 본 관절 구동력은 RagdollPoser가 소유하므로 여기선 앵커만 세운다
        if (IsServerAuthoritative)
        {
            SetupAnchor();
            SetupGroundContactReporters();
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
    /// 오너 1인칭 카메라에서만 머리와 지정 렌더러를 숨김.
    /// 원본은 숨김 레이어로 메인캠에서 제외하고 그림자는 ShadowsOnly 프록시가 대신 드리운다.
    /// </summary>
    private void HideOwnRenderersFromOwnerCamera()
    {
        if (!_firstPersonLook.IsOwner)
        {
            return;
        }

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!IsOwnerHidden(renderer.name))
            {
                continue;
            }

            CreateShadowProxy(renderer);
            renderer.gameObject.layer = LOCAL_HIDDEN_LAYER;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        _firstPersonLook.ExcludeLayerFromCamera(LOCAL_HIDDEN_LAYER);
    }

    private bool IsOwnerHidden(string rendererName)
    {
        if (System.Array.IndexOf(OWNER_HIDDEN_RENDERER_NAMES, rendererName) >= 0)
        {
            return true;
        }

        return _ownerHiddenExtraRenderers != null &&
               System.Array.IndexOf(_ownerHiddenExtraRenderers, rendererName) >= 0;
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
    public void SetLimp(bool isLimp, bool diving = false)
    {
        if (_isLimp == isLimp)
        {
            return;
        }

        _isLimp = isLimp;
        _isDiving = isLimp && diving;

        if (isLimp)
        {
            _wasMovementEnabled = _playerMovement.enabled;
            _playerMovement.enabled = false;
        }
        else
        {
            SyncPlayerToRagdoll();

            // 흐물거리는 동안 앵커는 제자리에 남아 있었다. MovePosition으로 따라가면
            // 한 스텝에 몸이 끌려가 튀므로, 몸 위치로 순간이동시킨 뒤 구동을 재개한다
            SnapAnchorToPlayer();

            _playerMovement.enabled = _wasMovementEnabled;
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
        IgnoreArmTorsoCollision();
        ApplyAnchorDrives();
    }

    /// <summary>
    /// 팔(위/아래팔) 콜라이더와 몸통/머리 콜라이더의 자기 충돌을 무시
    /// </summary>
    private void IgnoreArmTorsoCollision()
    {
        Collider[] armColliders = CollectBoneColliders(ARM_BONE_NAMES);
        Collider[] torsoColliders = CollectBoneColliders(TORSO_BONE_NAMES);

        foreach (Collider arm in armColliders)
        {
            foreach (Collider torso in torsoColliders)
            {
                if (arm != null && torso != null)
                {
                    Physics.IgnoreCollision(arm, torso, true);
                }
            }
        }

        // 좌우 팔끼리도 무시 (뻗기 중 반대 팔과 겹칠 수 있음)
        for (int i = 0; i < armColliders.Length; i++)
        {
            for (int j = i + 1; j < armColliders.Length; j++)
            {
                if (armColliders[i] != null && armColliders[j] != null)
                {
                    Physics.IgnoreCollision(armColliders[i], armColliders[j], true);
                }
            }
        }
    }

    /// <summary>
    /// 주어진 골격 이름들에 붙은 모든 Collider 수집
    /// </summary>
    /// <param name="boneNames">대상 골격 이름 목록</param>
    /// <returns>수집된 Collider 배열</returns>
    private Collider[] CollectBoneColliders(string[] boneNames)
    {
        var colliders = new System.Collections.Generic.List<Collider>();
        foreach (string boneName in boneNames)
        {
            Transform bone = FindBone(boneName);
            if (bone != null)
            {
                colliders.AddRange(bone.GetComponents<Collider>());
            }
        }

        return colliders.ToArray();
    }

    /// <summary>
    /// 물리 본의 렌더 보간을 켜 50Hz 물리와 렌더 프레임 사이 떨림을 줄인다. <br/>
    /// 넉백으로 빠르게 날아갈 때 얇은 바닥을 지나치지 않도록 연속 충돌 판정도 함께 켠다.
    /// </summary>
    private void EnableBoneInterpolation()
    {
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
        {
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
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

        // 위치는 유지하되(계속 끌려옴) 붙잡히면 상체 유지력만 낮춰 휘청이게
        float uprightWeight = _isLimp ? 0f : (_isGrabbed ? _grabbedUprightFactor : 1f);
        _anchorJoint.slerpDrive = new JointDrive
        {
            positionSpring = _anchorUprightSpring * uprightWeight,
            positionDamper = _anchorUprightDamper * uprightWeight,
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

        UpdateGrabbedState();
        DriveAnchor();
    }

    /// <summary>
    /// 붙잡힘 상태가 바뀌면 앵커 상체 유지력을 갱신 (붙잡히면 휘청)
    /// </summary>
    private void UpdateGrabbedState()
    {
        bool grabbed = _playerGrab != null && _playerGrab.IsGrabbed;
        if (grabbed != _isGrabbed)
        {
            _isGrabbed = grabbed;
            ApplyAnchorDrives();
        }
    }

    /// <summary>
    /// 앵커를 Player 좌표계로 순간이동. MovePosition과 달리 속도가 실리지 않는다.
    /// </summary>
    private void SnapAnchorToPlayer()
    {
        if (_anchorRigidbody == null)
        {
            return;
        }

        _anchorRigidbody.position = _playerTransform.TransformPoint(_anchorLocalPosition);
        _anchorRigidbody.rotation = _playerTransform.rotation * _anchorLocalRotation;
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

        // 1. 카메라 Pitch(위/아래)에 따른 Hips 앵커의 상하 기울임 각도 계산
        float hipsPitch = _pitch * _hipsPitchRatio;

        // 2. 앵커 회전에 hipsPitch 적용 (X축 회전)
        Quaternion targetRotation =
            _playerTransform.rotation *
            Quaternion.Euler(hipsPitch, GetPunchBodyYaw(), 0f) *
            _anchorLocalRotation;

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
    /// 펀치에 실리는 허리 회전각. 감을 때 반대로 틀었다가 휘두르며 풀어
    /// 팔뿐 아니라 몸 전체가 스윙을 따라 돌게 한다.
    /// </summary>
    /// <returns>플레이어 정면 기준 허리 yaw(도)</returns>
    private float GetPunchBodyYaw()
    {
        if (!TryGetPunchPose(
                out bool isLeftHand, out float weight, out float extension))
        {
            return 0f;
        }

        float side = isLeftHand ? -1f : 1f;
        float unwind = Mathf.Lerp(-PUNCH_WINDUP_YAW_RATIO, 1f, extension);
        return -side * _punchBodyYaw * unwind * weight;
    }

    /// <summary>
    /// Player 좌표계를 현재 래그돌 몸체 위치에 정렬. 캡슐은 항상 세워둔 채 몸을 따라간다. <br/>
    /// 높이는 hips를 따라가되 hips 아래 실제 지면 위로 올려세운다. 누운 hips 높이를 그대로 쓰면
    /// 캡슐 아래 절반이 땅에 박히고, 지면에만 붙여두면 날아가는 동안 캡슐이 뒤에 남는다.
    /// </summary>
    private void SyncPlayerToRagdoll()
    {
        // 회전은 건드리지 않는다. 구르는 래그돌 yaw를 따라가면 다이빙/넉다운 중 카메라가 같이 돈다.
        // 위치만 따라가고, 회전은 쓰러질 때 방향 그대로 유지한다.
        Vector3 position = _hips.position;

        // 지면 아래로만 안 내려가게 올려세우고, 그보다 높으면 몸 높이를 그대로 따라간다.
        // 직전 높이를 유지하면 날아가는 동안 캡슐이 공중에 남아 기상 시 몸이 그리로 끌려 올라간다.
        if (TryGetGroundY(out float groundY))
        {
            position.y = Mathf.Max(position.y, groundY + _playerMovement.StandingGroundOffset);
        }
        else
        {
            // 지면을 못 찾으면 직전 높이 유지. 몸이 바닥을 파고든 순간 캡슐까지 끌고 내려가면 땅에 박힌다
            position.y = _playerTransform.position.y;
        }

        _playerTransform.position = position;
    }

    /// <summary>뼈가 외부 물체에 마지막으로 닿은 시각. 기상 타이밍(착지) 판정용.</summary>
    public float LastContactTime => _lastContactTime;

    /// <summary>뼈가 외부 물체에 닿았음을 기록. RagdollGroundContact가 호출한다.</summary>
    public void ReportGroundContact()
    {
        _lastContactTime = Time.time;
    }

    // 뼈마다 접촉 리포터를 붙인다. 물리가 도는 서버에서만 의미가 있다
    private void SetupGroundContactReporters()
    {
        foreach (Rigidbody body in _boneBodies)
        {
            // 가만히 누우면 뼈가 잠들면서 OnCollisionStay가 끊긴다.
            // 그러면 접지가 풀린 것으로 오인해 기상 카운트가 영영 안 찬다
            body.sleepThreshold = 0f;
            body.gameObject.AddComponent<RagdollGroundContact>().Bind(this, _playerTransform);
        }
    }

    /// <summary>
    /// hips 바로 아래 지면 높이를 찾는다. 자기 래그돌 뼈와 Player 캡슐만 걸러내고 나머지는 전부 지면으로 본다. <br/>
    /// 레이어 마스크에 기대지 않는 이유: 맵 바닥이 Ground로 세팅되지 않은 씬이 있어 마스크를 쓰면 지면을 영영 못 찾는다.
    /// </summary>
    private bool TryGetGroundY(out float groundY)
    {
        groundY = 0f;
        Vector3 origin = _hips.position + Vector3.up * GROUND_RAY_START_HEIGHT;
        int count = Physics.RaycastNonAlloc(
            origin, Vector3.down, _groundHits,
            GROUND_RAY_START_HEIGHT + GROUND_RAY_DISTANCE,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        // NonAlloc 결과는 정렬돼 있지 않으므로 가장 가까운(=가장 높은) 지면을 직접 고른다
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            Transform hit = _groundHits[i].transform;
            if (hit.IsChildOf(transform)
                || (_playerTransform != null && hit.IsChildOf(_playerTransform)))
            {
                continue;
            }

            if (_groundHits[i].distance < nearest)
            {
                nearest = _groundHits[i].distance;
                groundY = _groundHits[i].point.y;
            }
        }

        return nearest < float.PositiveInfinity;
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

        // 주먹 판정 위치. 팔 안쪽에 찍히면 _fistOffset 부호를 뒤집는다
        Gizmos.color = Color.yellow;
        if (TryGetFistPoint(true, out Vector3 leftFist))
        {
            Gizmos.DrawWireSphere(leftFist, 0.05f);
        }

        if (TryGetFistPoint(false, out Vector3 rightFist))
        {
            Gizmos.DrawWireSphere(rightFist, 0.05f);
        }
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

    #region 물건 잡기

    /// <summary>
    /// CarryGripPoints를 이용해서 그립의 좌우 폭을 결정한다
    /// </summary>
    /// <param name="target"></param>
    public void SetCarryTarget(CarryGripPoints target)
    {
        _carryTarget = target;

        if (target != null && target.LeftGripPoint != null && target.RightGripPoint != null)
        {
            _carryGripHalfWidth = Vector3.Distance(target.LeftGripPoint.position, target.RightGripPoint.position) * 0.5f;
        }
        else
        {
            _carryGripHalfWidth = 0f;
        }
    }

    /// <summary>
    /// 들기 시작 시점에 캐싱된 그립 폭의 절반(월드 단위). 그립 대상이 없으면 false.
    /// </summary>
    public bool TryGetCarryHalfWidth(out float halfWidth)
    {
        halfWidth = _carryGripHalfWidth;
        return _carryTarget != null;
    }

    #endregion
}
