// 액티브 래그돌의 실험용 이동 및 카메라 제어

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// 앵커 기반 액티브 래그돌 이동 및 시점 제어
/// </summary>
public class RagdollDriver : MonoBehaviour
{
    private const string HIPS_BONE_NAME = "Hips";
    private const string HEAD_BONE_NAME = "Head";
    private const string LEFT_SHOULDER_BONE_NAME = "Shoulder.L";
    private const string RIGHT_SHOULDER_BONE_NAME = "Shoulder.R";
    private const float MOVE_INPUT_THRESHOLD = 0.01f;
    private const float ROTATION_INPUT_THRESHOLD = 0.001f;

    [Header("연결")]
    [FormerlySerializedAs("hips")]
    [SerializeField]
    private Transform _hips;

    [FormerlySerializedAs("head")]
    [SerializeField]
    private Transform _head;

    [FormerlySerializedAs("cam")]
    [SerializeField]
    private Camera _camera;

    [Header("퍼펫 모드")]
    [FormerlySerializedAs("springyPuppet")]
    [SerializeField]
    private bool _springyPuppet = true;

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

    [FormerlySerializedAs("maxPuppetSpeed")]
    [SerializeField]
    [Tooltip("Hips의 최대 수평 속도. 0이면 제한 없음")]
    private float _maxPuppetSpeed = 3.3f;

    [FormerlySerializedAs("anchorMaxForce")]
    [SerializeField]
    private float _anchorMaxForce = 3000f;

    [Header("이동")]
    [FormerlySerializedAs("puppetSpeed")]
    [SerializeField]
    private float _puppetSpeed = 2.7f;

    [FormerlySerializedAs("physicsForce")]
    [SerializeField]
    private float _physicsForce = 400f;

    [FormerlySerializedAs("turnSpeed")]
    [SerializeField]
    private float _turnSpeed = 360f;

    [FormerlySerializedAs("flipBodyForward")]
    [SerializeField]
    private bool _flipBodyForward;

    [Header("시점")]
    [FormerlySerializedAs("mouseSensitivity")]
    [SerializeField]
    private float _mouseSensitivity = 0.12f;

    [FormerlySerializedAs("thirdDistance")]
    [SerializeField]
    private float _thirdPersonDistance = 4f;

    [FormerlySerializedAs("thirdHeight")]
    [SerializeField]
    private float _thirdPersonHeight = 1.2f;

    [FormerlySerializedAs("firstPersonOffset")]
    [SerializeField]
    private Vector3 _firstPersonOffset = new Vector3(0f, 0.05f, 0.2f);

    private Rigidbody _hipsRigidbody;
    private Rigidbody _anchorRigidbody;
    private ConfigurableJoint _anchorJoint;
    private bool _isFirstPerson;
    private bool _isPuppetEnabled = true;
    private float _yaw;
    private float _pitch;
    private Quaternion _bodyRotationOffset = Quaternion.identity;

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
    /// 에디터 도구용 제어 카메라 설정 및 조회
    /// </summary>
    public Camera DriverCamera
    {
        get => _camera;
        set => _camera = value;
    }

    /// <summary>
    /// 현재 프레임의 월드 이동 방향
    /// </summary>
    public Vector3 MoveDirection { get; private set; }

    /// <summary>
    /// 현재 이동 입력 여부
    /// </summary>
    public bool IsMoving => MoveDirection.sqrMagnitude > MOVE_INPUT_THRESHOLD;

    /// <summary>
    /// 현재 조준 회전
    /// </summary>
    public Quaternion AimRotation => Quaternion.Euler(_pitch, _yaw, 0f);

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
        if (_hips == null)
        {
            _hips = FindBone(HIPS_BONE_NAME);
        }

        if (_head == null)
        {
            _head = FindBone(HEAD_BONE_NAME);
        }

        if (_camera == null)
        {
            _camera = Camera.main;
        }

        if (_hips != null)
        {
            _hipsRigidbody = _hips.GetComponent<Rigidbody>();
        }

        if (_hipsRigidbody == null)
        {
            Debug.LogError("[RagdollDriver] Hips의 Rigidbody를 찾지 못했습니다.", this);
        }

        if (_camera == null)
        {
            Debug.LogError("[RagdollDriver] Main Camera를 찾지 못했습니다.", this);
        }

        ComputeBodyFrame();
    }

    /// <summary>
    /// 초기 시점 및 퍼펫 앵커 설정
    /// </summary>
    private void Start()
    {
        _yaw = _hips != null ? _hips.eulerAngles.y : 0f;
        SetCursorLock(true);

        if (_springyPuppet)
        {
            SetupAnchor();
        }
    }

    /// <summary>
    /// 골격 배치 기준 몸체 회전 계산
    /// </summary>
    private void ComputeBodyFrame()
    {
        Transform leftShoulder = FindBone(LEFT_SHOULDER_BONE_NAME);
        Transform rightShoulder = FindBone(RIGHT_SHOULDER_BONE_NAME);

        if (_hips == null || _head == null || leftShoulder == null || rightShoulder == null)
        {
            Debug.LogWarning("[RagdollDriver] 몸체 회전 계산에 필요한 골격을 찾지 못했습니다.", this);
            return;
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
        if (_hipsRigidbody == null)
        {
            return;
        }

        _hipsRigidbody.isKinematic = false;

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

        ApplyAnchorDrives();
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

        float driveWeight = _isPuppetEnabled ? 1f : 0f;
        JointDrive positionDrive = new JointDrive
        {
            positionSpring = _anchorSpring * driveWeight,
            positionDamper = _anchorDamper * driveWeight,
            maximumForce = _anchorMaxForce
        };

        _anchorJoint.xDrive = positionDrive;
        _anchorJoint.yDrive = positionDrive;
        _anchorJoint.zDrive = positionDrive;
        _anchorJoint.slerpDrive = new JointDrive
        {
            positionSpring = _anchorUprightSpring * driveWeight,
            positionDamper = _anchorUprightDamper * driveWeight,
            maximumForce = _anchorMaxForce
        };
    }

    /// <summary>
    /// 키보드 입력 및 시점 회전 처리
    /// </summary>
    private void Update()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null)
        {
            return;
        }

        if (keyboard.tabKey.wasPressedThisFrame)
        {
            _isFirstPerson = !_isFirstPerson;
        }

        if (keyboard.spaceKey.wasPressedThisFrame && _hipsRigidbody != null)
        {
            TogglePuppet();
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            SetCursorLock(false);
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            SetCursorLock(true);
        }

        UpdateLookInput();
        UpdateMoveInput(keyboard);
    }

    /// <summary>
    /// 퍼펫 활성 상태 전환
    /// </summary>
    private void TogglePuppet()
    {
        _isPuppetEnabled = !_isPuppetEnabled;

        if (_springyPuppet && _anchorJoint != null)
        {
            ApplyAnchorDrives();
            return;
        }

        _hipsRigidbody.isKinematic = _isPuppetEnabled;
    }

    /// <summary>
    /// 마우스 입력을 시점 회전에 반영
    /// </summary>
    private void UpdateLookInput()
    {
        if (Cursor.lockState != CursorLockMode.Locked || Mouse.current == null)
        {
            return;
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * _mouseSensitivity;
        _yaw += mouseDelta.x;
        _pitch = Mathf.Clamp(_pitch - mouseDelta.y, -70f, 70f);
    }

    /// <summary>
    /// 키보드 입력을 월드 이동 방향으로 변환
    /// </summary>
    /// <param name="keyboard">현재 키보드 장치</param>
    private void UpdateMoveInput(Keyboard keyboard)
    {
        Vector2 input = new Vector2(
            (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
            (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));

        Quaternion flatYaw = Quaternion.Euler(0f, _yaw, 0f);
        Vector3 direction = flatYaw * new Vector3(input.x, 0f, input.y);

        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        MoveDirection = direction;
    }

    /// <summary>
    /// 현재 이동 방향을 물리 앵커 또는 Hips에 적용
    /// </summary>
    private void FixedUpdate()
    {
        if (_hipsRigidbody == null)
        {
            return;
        }

        Vector3 direction = MoveDirection;
        float deltaTime = Time.fixedDeltaTime;

        if (_springyPuppet && _anchorRigidbody != null)
        {
            MoveAnchor(direction, deltaTime);
            return;
        }

        MoveHips(direction, deltaTime);
    }

    /// <summary>
    /// 물리 앵커를 현재 이동 방향으로 이동
    /// </summary>
    /// <param name="direction">월드 이동 방향</param>
    /// <param name="deltaTime">고정 프레임 간격</param>
    private void MoveAnchor(Vector3 direction, float deltaTime)
    {
        if (!_isPuppetEnabled)
        {
            return;
        }

        _anchorRigidbody.MovePosition(
            _anchorRigidbody.position + direction * _puppetSpeed * deltaTime);

        RotateRigidbody(_anchorRigidbody, direction, deltaTime);
        ClampPuppetSpeed();
    }

    /// <summary>
    /// 퍼펫 상태에 따른 Hips 이동
    /// </summary>
    /// <param name="direction">월드 이동 방향</param>
    /// <param name="deltaTime">고정 프레임 간격</param>
    private void MoveHips(Vector3 direction, float deltaTime)
    {
        if (_hipsRigidbody.isKinematic)
        {
            _hipsRigidbody.MovePosition(
                _hipsRigidbody.position + direction * _puppetSpeed * deltaTime);
            RotateRigidbody(_hipsRigidbody, direction, deltaTime);
            return;
        }

        _hipsRigidbody.AddForce(
            direction * _physicsForce * deltaTime,
            ForceMode.VelocityChange);
    }

    /// <summary>
    /// Rigidbody를 이동 방향으로 회전
    /// </summary>
    /// <param name="rigidbody">회전 대상 Rigidbody</param>
    /// <param name="direction">목표 방향</param>
    /// <param name="deltaTime">고정 프레임 간격</param>
    private void RotateRigidbody(
        Rigidbody rigidbody,
        Vector3 direction,
        float deltaTime)
    {
        if (direction.sqrMagnitude <= ROTATION_INPUT_THRESHOLD)
        {
            return;
        }

        Quaternion targetRotation =
            Quaternion.LookRotation(direction, Vector3.up) *
            Quaternion.Inverse(_bodyRotationOffset);
        Quaternion nextRotation = Quaternion.RotateTowards(
            rigidbody.rotation,
            targetRotation,
            _turnSpeed * deltaTime);

        rigidbody.MoveRotation(nextRotation);
    }

    /// <summary>
    /// Hips의 수평 속도를 설정된 최댓값으로 제한
    /// </summary>
    private void ClampPuppetSpeed()
    {
        if (_maxPuppetSpeed <= 0f || _hipsRigidbody == null)
        {
            return;
        }

        Vector3 velocity = _hipsRigidbody.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);

        if (horizontalVelocity.sqrMagnitude <= _maxPuppetSpeed * _maxPuppetSpeed)
        {
            return;
        }

        horizontalVelocity = horizontalVelocity.normalized * _maxPuppetSpeed;
        _hipsRigidbody.linearVelocity =
            new Vector3(horizontalVelocity.x, velocity.y, horizontalVelocity.z);
    }

    /// <summary>
    /// 시점 모드에 따른 카메라 위치 및 회전 갱신
    /// </summary>
    private void LateUpdate()
    {
        if (_camera == null)
        {
            return;
        }

        Quaternion viewRotation = Quaternion.Euler(_pitch, _yaw, 0f);

        if (_isFirstPerson && _head != null)
        {
            _camera.transform.position =
                _head.position + viewRotation * _firstPersonOffset;
            _camera.transform.rotation = viewRotation;
            return;
        }

        if (_hips == null)
        {
            return;
        }

        Vector3 focusPosition = _hips.position + Vector3.up * _thirdPersonHeight;
        _camera.transform.position =
            focusPosition + viewRotation * new Vector3(0f, 0f, -_thirdPersonDistance);
        _camera.transform.rotation = viewRotation;
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
    /// 커서 잠금 상태 설정
    /// </summary>
    /// <param name="isLocked">커서 잠금 여부</param>
    private static void SetCursorLock(bool isLocked)
    {
        Cursor.lockState =
            isLocked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !isLocked;
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
