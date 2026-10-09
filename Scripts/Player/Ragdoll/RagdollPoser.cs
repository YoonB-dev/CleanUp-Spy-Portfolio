using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ConfigurableJoint에 보행, 시선, 잡기, 펀치 목표 회전을 적용 (서버 권위). <br/>
/// 본 관절의 구동력(스프링/감쇠)도 이 컴포넌트가 전부 소유한다. RagdollDriver는 앵커만 담당.
/// </summary>
[RequireComponent(typeof(RagdollDriver))]
public class RagdollPoser : MonoBehaviour
{
    private const string SPINE_BONE_NAME = "Spine";
    private const string CHEST_BONE_NAME = "Chest";
    private const string HEAD_BONE_NAME = "Head";
    private const string RIGHT_UPPER_ARM_BONE_NAME = "UpperArm.R";
    private const string LEFT_UPPER_ARM_BONE_NAME = "UpperArm.L";
    private const string RIGHT_FOREARM_BONE_NAME = "Forearm.R";
    private const string LEFT_FOREARM_BONE_NAME = "Forearm.L";
    private const string LEFT_THIGH_BONE_NAME = "Thigh.L";
    private const string RIGHT_THIGH_BONE_NAME = "Thigh.R";
    private const string LEFT_SHIN_BONE_NAME = "Shin.L";
    private const string RIGHT_SHIN_BONE_NAME = "Shin.R";
    private const string LEFT_HAND_BONE_NAME = "Hand.L";
    private const string RIGHT_HAND_BONE_NAME = "Hand.R";
    private static readonly Vector3 LOOK_PITCH_AXIS = new Vector3(1f, 0f, 0f);

    private static readonly string[] LEG_BONE_NAMES =
    {
        LEFT_THIGH_BONE_NAME, RIGHT_THIGH_BONE_NAME,
        LEFT_SHIN_BONE_NAME, RIGHT_SHIN_BONE_NAME
    };

    private static readonly string[] ARM_BONE_NAMES =
    {
        LEFT_UPPER_ARM_BONE_NAME, RIGHT_UPPER_ARM_BONE_NAME,
        LEFT_FOREARM_BONE_NAME, RIGHT_FOREARM_BONE_NAME
    };

    private static readonly string[] FOOT_BONE_NAMES = { "Foot.L", "Foot.R" };

    private static readonly string[] TORSO_BONE_NAMES =
    {
        SPINE_BONE_NAME, CHEST_BONE_NAME, HEAD_BONE_NAME
    };

    [Header("관절")]
    [SerializeField]
    private bool _freeJointLimitsOnPlay = true;

    [Tooltip("애니메이션 자세를 또렷이 따라가도록 팔다리 관절을 구동하는 스프링")]
    [SerializeField]
    private float _limbPoseSpring = 2500f;

    [Tooltip("팔다리 관절 구동 감쇠")]
    [SerializeField]
    private float _limbPoseDamper = 80f;

    [Tooltip("가속 시 척추/가슴/머리가 쏠리지 않게 상체 관절을 굳히는 스프링")]
    [SerializeField]
    private float _torsoSpring = 4000f;

    [Tooltip("상체 관절의 감쇠")]
    [SerializeField]
    private float _torsoDamper = 200f;

    [Tooltip("쓰러졌을 때 관절 구동력 배수. 0에 가까울수록 완전히 흐물거린다")]
    [SerializeField]
    [Range(0f, 0.5f)]
    private float _limpDriveScale = 0.05f;

    [Tooltip("다이빙 시 팔을 뻗는 각도(도). 몸이 눕는 걸 감안해 크게. 90=머리 너머로 쭉")]
    [SerializeField]
    private float _diveArmPitch = 80f;

    [Header("시선 (상하)")]
    [SerializeField]
    private bool _isLookEnabled = true;

    [Tooltip("시선 각도 중 척추가 따라가는 비율. 부호 반대면 뒤집힘")]
    [SerializeField]
    private float _spinePitchShare = 0.4f;

    [Tooltip("시선 각도 중 머리가 따라가는 비율")]
    [SerializeField]
    private float _headPitchShare = 0.6f;
    [Tooltip("시선 각도 중 가슴이 따라가는 비율")]
    [SerializeField]
    private float _chestPitchShare = 0.25f;

    [Tooltip("위를 볼 때 상체가 따라 젖혀지는 최대 각도")]
    [SerializeField] private float _maxLookUpAngle = 35f;

    [Tooltip("아래를 볼 때 상체가 따라 숙이는 최대 각도")]
    [SerializeField] private float _maxLookDownAngle = 45f;
    [Header("애니메이션")]
    [Tooltip("관절이 따라갈 자세를 재생할 모델. 렌더러를 끈 채 서버에만 만든다")]
    [SerializeField] private GameObject _animationModel;
    [SerializeField] private AnimationClip _idleClip;
    [SerializeField] private AnimationClip _walkForwardClip;
    [SerializeField] private AnimationClip _walkLeftClip;
    [SerializeField] private AnimationClip _walkRightClip;

    [Tooltip("걷기 클립 한 바퀴에 나아가는 거리. 발이 미끄러지면 줄이고 종종거리면 늘린다")]
    [SerializeField] private float _strideLength = 2f;

    [Tooltip("허벅지 회전을 애니메이션보다 키우는 배율. 보폭이 넓어져 덜 종종거린다. 올리면 걸음 거리도 같이 올린다")]
    [SerializeField] private float _strideScale = 1.4f;

    [Tooltip("디버그용. 관절이 따라가는 애니메이션 자세를 원점에 보이게 한다")]
    [SerializeField] private bool _showAnimationRig;

    [Header("잡기 뻗기")]
    [Tooltip("잡기 시 오른팔이 향할 몸통 기준 좌우각. 양수면 바깥쪽, 몸통을 벗어나게 살짝")]
    [SerializeField]
    [Range(-80f, 80f)]
    private float _reachYaw = 25f;

    [Tooltip("잡기 시 오른팔이 향할 상하각. 양수면 아래. 시선연동이 켜지면 시선각에 가산")]
    [SerializeField]
    [Range(-70f, 70f)]
    private float _reachPitch;

    [Tooltip("뻗기 상하를 카메라 시선에 연동")]
    [SerializeField]
    private bool _reachFollowViewPitch = true;

    [Tooltip("뻗을 때 아랫팔을 장축 둘레로 비트는 각도. 손바닥 방향 조정")]
    [SerializeField]
    [Range(-180f, 180f)]
    private float _reachRoll;

    // 뻗기 회전량이 반바퀴 특이점 근처에서 튀지 않도록 제한하는 안전각
    private const float REACH_MAX_YAW = 80f;
    private const float REACH_MAX_PITCH = 70f;
    private const float ARM_TWIST_LIMIT = 30f;

    // BuildArmPoses가 왼팔 다음 오른팔을 넣으므로 오른팔은 인덱스 1
    private const int RIGHT_ARM_INDEX = 1;

    [Tooltip("뻗기 자세 전환 속도")]
    [SerializeField]
    private float _reachRampSpeed = 10f;

    [Header("펀치")]
    [Tooltip("감을 때 팔이 향할 좌우각. 클수록 옆으로 크게 젖힌다")]
    [SerializeField]
    [Range(0f, 80f)]
    private float _punchWindupYaw = 75f;

    [Tooltip("감을 때 팔이 향할 상하각. 음수면 위로 치켜든다")]
    [SerializeField]
    [Range(-70f, 70f)]
    private float _punchWindupPitch = -40f;

    [Tooltip("휘두른 끝의 좌우각. 음수면 몸 중심선을 넘어 가로지른다")]
    [SerializeField]
    [Range(-80f, 80f)]
    private float _punchStrikeYaw = -15f;

    [Tooltip("휘두른 끝의 상하각. 시선연동이 켜지면 휘두를수록 시선각이 가산")]
    [SerializeField]
    [Range(-70f, 70f)]
    private float _punchStrikePitch = 12f;

    [Tooltip("펀치 상하를 카메라 시선에 연동")]
    [SerializeField]
    private bool _punchFollowViewPitch = true;

    [Tooltip("감을 때 팔꿈치를 접는 각도. 크게 주면 훅이 아니라 지르기가 된다")]
    [SerializeField]
    [Range(-140f, 140f)]
    private float _punchElbowBend = 25f;

    [Tooltip("펀치에 실리는 상체 비틀기 각도. 부호 반대면 반대로 비틀림")]
    [SerializeField]
    [Range(-60f, 60f)]
    private float _punchSpineTwist = 32f;

    [Tooltip("펀치 중 아랫팔을 장축 둘레로 비트는 각도")]
    [SerializeField]
    [Range(-180f, 180f)]
    private float _punchRoll;

    [Tooltip("휘두르는 동안 팔 관절 스프링 배수. 팔을 던지는 힘")]
    [SerializeField]
    [Range(0.1f, 4f)]
    private float _punchSwingSpringScale = 1.3f;

    [Tooltip("휘두르는 동안 팔 관절 감쇠 배수. 작을수록 목표를 지나쳐 흔들리며 따라나간다")]
    [SerializeField]
    [Range(0.05f, 2f)]
    private float _punchSwingDamperScale = 0.25f;

    // 감을 때는 타격 반대쪽으로 이만큼 비틀어둔다 (상체 비틀기 각 대비 비율)
    private const float PUNCH_WINDUP_TWIST_RATIO = 0.45f;

    private readonly Dictionary<string, BoneData> _bones =
        new Dictionary<string, BoneData>();

    private readonly List<ArmPose> _armPoses = new List<ArmPose>();

    private RagdollDriver _driver;
    private RagdollAnimationRig _animationRig;
    private PlayerKnockdown _knockdown;
    private readonly Transform[] _feet = new Transform[FOOT_BONE_NAMES.Length];
    private readonly Quaternion[] _footRestRotations = new Quaternion[FOOT_BONE_NAMES.Length];
    private float _reachWeight;
    private bool _wasLimp;
    private bool _isPunching;
    private bool _isLeftPunch;
    private float _punchWeight;
    private float _punchExtension;

    [Header("양손 들기 (Carry)")]
    [Tooltip("두 손 중간 지점 기준 CarryAnchor의 오프셋 (X:좌우, Y:위아래, Z:앞뒤)")]
    [SerializeField] private Vector3 _carryAnchorOffset = new Vector3(0f, 0.1f, 0.3f); // 예: 위로 0.1, 앞쪽으로 0.3
    private Vector3 _activeCarryAnchorOffset; // 실제 프레임 계산에 사용되는 활성 오프셋
    [Tooltip("들기 중 팔 관절 스프링. 물건을 거의 즉각 따라가게 하려면 크게")]
    [SerializeField]
    private float _carrySpring = 9000f;

    [Tooltip("들기 중 팔 관절 감쇠. 스프링과 비례해서 크게 줘야 떨림 없이 딱 붙음")]
    [SerializeField]
    private float _carryDamper = 450f;
    [Tooltip("양손을 몸 안쪽(중앙)으로 모으는 각도. 값이 클수록 손이 중앙에 가깝게 모임")]
    [SerializeField]
    [Range(0f, 80f)]
    private float _carryYaw = 30f;

    [Tooltip("양손을 앞으로 뻗을 때의 상하각. 양수면 아래")]
    [SerializeField]
    [Range(-70f, 70f)]
    private float _carryPitch = 10f;

    [Tooltip("들기 상하를 카메라 시선에 연동")]
    [SerializeField]
    private bool _carryFollowViewPitch = true;

    [Tooltip("들 때 아랫팔을 장축 둘레로 비트는 각도")]
    [SerializeField]
    [Range(-180f, 180f)]
    private float _carryRoll;

    [Tooltip("들기 자세 전환 속도")]
    [SerializeField]
    private float _carryRampSpeed = 8f;
    private bool _isCarryRequested;
    private float _carryWeight;
    private Transform _leftHandTransform;
    private Transform _rightHandTransform;
    private Transform _carryAnchor;
    /// <summary>
    /// 양손 중간 지점에 위치/회전을 매 프레임 맞추는 앵커.
    /// PickupItem 등 외부에서 이 트랜스폼에 아이템을 SetParent하면 됨.
    /// 손 뼈를 못 찾았으면 null.
    /// </summary>
    [Tooltip("양손을 기본 _carryYaw 각도로 들었을 때 두 손 사이의 기준 너비(m). 물건 너비가 이보다 작으면 손을 모으고, 크면 벌립니다.")]
    [SerializeField]
    private float _defaultCarryWidth = 0.4f;
    public Transform CarryAnchor => _carryAnchor;
    private void Awake()
    {
        _driver = GetComponent<RagdollDriver>();
        _activeCarryAnchorOffset = _carryAnchorOffset; // 초기값은 기본 오프셋
        _leftHandTransform = FindBoneTransform(LEFT_HAND_BONE_NAME);
        _rightHandTransform = FindBoneTransform(RIGHT_HAND_BONE_NAME);

        if (_leftHandTransform != null && _rightHandTransform != null)
        {
            GameObject anchorObject = new GameObject($"{name}_CarryAnchor123123");
            _carryAnchor = anchorObject.transform;
            _carryAnchor.SetParent(transform, false);

            _carryAnchor.localPosition = new Vector3(0f, 0.2f, 0.5f);
        }

        if (!_driver.IsServerAuthoritative)
        {
            return;
        }

        foreach (ConfigurableJoint joint in
                 GetComponentsInChildren<ConfigurableJoint>(true))
        {
            _bones[joint.name] =
                new BoneData(joint, joint.transform.localRotation);
        }

        if (_bones.Count == 0)
        {
            Debug.LogError(
                "[RagdollPoser] ConfigurableJoint를 찾지 못했습니다.",
                this);
        }

        if (_freeJointLimitsOnPlay)
        {
            ReleaseJointLimits();
        }

        ApplyBaseDrives();
        BuildArmPoses();

        _animationRig = new RagdollAnimationRig(
            _animationModel, _showAnimationRig,
            _idleClip, _walkForwardClip, _walkLeftClip, _walkRightClip);
    }

    private void OnDestroy()
    {
        _animationRig?.Destroy();

        if (_carryAnchor != null)
        {
            Destroy(_carryAnchor.gameObject);
        }
    }

    private Transform FindBoneTransform(string boneName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == boneName)
            {
                return t;
            }
        }
        return null;
    }

    private void BuildArmPoses()
    {
        _armPoses.Clear();

        Vector3 bodyUp = transform.rotation * Vector3.up;
        AddArmPose(LEFT_UPPER_ARM_BONE_NAME, LEFT_FOREARM_BONE_NAME, -1f, bodyUp);
        AddArmPose(RIGHT_UPPER_ARM_BONE_NAME, RIGHT_FOREARM_BONE_NAME, 1f, bodyUp);
    }

    private void AddArmPose(
        string boneName,
        string forearmBoneName,
        float sideSign,
        Vector3 bodyUp)
    {
        if (!_bones.TryGetValue(boneName, out BoneData bone) ||
            bone.Joint == null)
        {
            return;
        }

        Quaternion boneWorld = bone.Joint.transform.rotation;

        // 뻗기 계산용으로 팔 rest 회전을 몸통 프레임으로 환산해 저장
        Quaternion reachBodyRest = Quaternion.Inverse(transform.rotation) * boneWorld;

        _bones.TryGetValue(forearmBoneName, out BoneData forearm);
        ConfigurableJoint forearmJoint = forearm?.Joint;
        Vector3 elbowLocalAxis = forearmJoint != null
            ? Quaternion.Inverse(forearmJoint.transform.rotation) * bodyUp
            : Vector3.up;

        _armPoses.Add(new ArmPose(
            bone.Joint, bone.RestRotation, sideSign, reachBodyRest,
            forearmJoint, forearm?.RestRotation ?? Quaternion.identity,
            elbowLocalAxis));
    }

    /// <summary>
    /// 매 스텝 바뀌지 않는 관절 구동력을 설정.
    /// 팔은 펀치에 따라 값이 계속 변하므로 ApplyArmDrive가 유일한 기록자이고 여기서 건드리지 않는다.
    /// </summary>
    private void ApplyBaseDrives()
    {
        // 걷기 스윙을 따라가도록 다리를 굳힘 (기본 slerp는 약해 흐물거림)
        foreach (string boneName in LEG_BONE_NAMES)
        {
            SetJointDrive(FindJoint(boneName), _limbPoseSpring, _limbPoseDamper);
        }

        // 가속 시 상체가 채찍처럼 쏠리지 않게 굳힘. 상체를 하나로 움직여 머리 쏠림을 줄인다
        foreach (string boneName in TORSO_BONE_NAMES)
        {
            SetJointDrive(FindJoint(boneName), _torsoSpring, _torsoDamper);
        }
    }

    /// <summary>
    /// 쓰러졌을 때 모든 관절을 풀어 흐물거리게 한다. 팔도 여기서 같이 푼다
    /// (자세 구동이 멈추면 ApplyArmDrive가 호출되지 않으므로).
    /// </summary>
    private void ApplyLimpDrives()
    {
        float spring = _limbPoseSpring * _limpDriveScale;
        float damper = _limbPoseDamper * _limpDriveScale;

        foreach (ArmPose arm in _armPoses)
        {
            SetJointDrive(arm.Joint, spring, damper);
            SetJointDrive(arm.ForearmJoint, spring, damper);
        }

        foreach (string boneName in LEG_BONE_NAMES)
        {
            SetJointDrive(FindJoint(boneName), spring, damper);
        }

        foreach (string boneName in TORSO_BONE_NAMES)
        {
            SetJointDrive(FindJoint(boneName), _torsoSpring * _limpDriveScale,
                _torsoDamper * _limpDriveScale);
        }
    }

    // 다이빙: 두 팔을 위로 뻗어 firm하게 고정하고 다리/상체는 흐물하게 푼다
    private void ApplyDiveDrives()
    {
        float limpSpring = _limbPoseSpring * _limpDriveScale;
        float limpDamper = _limbPoseDamper * _limpDriveScale;

        foreach (ArmPose arm in _armPoses)
        {
            Quaternion offset =
                BuildReachLocalOffset(arm.ReachBodyRest, 0f, -_diveArmPitch);
            arm.Joint.SetTargetRotationLocal(arm.RestRotation * offset, arm.RestRotation);
            SetJointDrive(arm.Joint, _limbPoseSpring, _limbPoseDamper);

            if (arm.ForearmJoint != null)
            {
                arm.ForearmJoint.SetTargetRotationLocal(arm.ForearmRest, arm.ForearmRest);
                SetJointDrive(arm.ForearmJoint, _limbPoseSpring, _limbPoseDamper);
            }
        }

        foreach (string boneName in LEG_BONE_NAMES)
        {
            SetJointDrive(FindJoint(boneName), limpSpring, limpDamper);
        }

        foreach (string boneName in TORSO_BONE_NAMES)
        {
            SetJointDrive(FindJoint(boneName), _torsoSpring * _limpDriveScale,
                _torsoDamper * _limpDriveScale);
        }
    }

    /// <summary>
    /// 본 관절 구동력을 쓰는 유일한 경로. maximumForce는 프리팹 값을 유지한다.
    /// </summary>
    /// <param name="joint">대상 관절. null이면 무시</param>
    /// <param name="spring">목표 회전을 따라가는 힘</param>
    /// <param name="damper">구동 감쇠</param>
    private static void SetJointDrive(
        ConfigurableJoint joint, float spring, float damper, float maxForce = -1f)
    {
        if (joint == null)
        {
            return;
        }

        joint.rotationDriveMode = RotationDriveMode.Slerp;

        JointDrive drive = joint.slerpDrive;
        drive.positionSpring = spring;
        drive.positionDamper = damper;
        joint.slerpDrive = drive;
        if(maxForce >= 0)
        {
            drive.maximumForce = maxForce;
        }
        joint.slerpDrive = drive;
    }

    private ConfigurableJoint FindJoint(string boneName)
    {
        return _bones.TryGetValue(boneName, out BoneData bone) ? bone.Joint : null;
    }

    private void OnValidate()
    {
        if (Application.isPlaying && _bones.Count > 0)
        {
            ApplyBaseDrives();
        }
    }

    private void ReleaseJointLimits()
    {
        foreach (BoneData bone in _bones.Values)
        {
            bone.Joint.angularXMotion = ConfigurableJointMotion.Free;
            bone.Joint.angularYMotion = ConfigurableJointMotion.Free;
            bone.Joint.angularZMotion = ConfigurableJointMotion.Free;
        }

        foreach (string boneName in ARM_BONE_NAMES)
        {
            LimitTwist(FindJoint(boneName));
        }
    }

    // 이 래그돌 관절은 Z축이 뼈 장축이다. 풀어 두면 맞거나 들 때 팔이 비틀려 어깨 메시가 뭉개진다
    private static void LimitTwist(ConfigurableJoint joint)
    {
        if (joint == null)
        {
            return;
        }

        joint.angularZMotion = ConfigurableJointMotion.Limited;
        joint.angularZLimit = new SoftJointLimit { limit = ARM_TWIST_LIMIT };
    }

    private void FixedUpdate()
    {
        if (_driver == null || !_driver.IsServerAuthoritative)
        {
            return;
        }

        // 쓰러진 동안엔 관절을 풀고 자세 구동을 멈춘다. 안 그러면 자세를 유지한 채 마네킹처럼 넘어진다
        bool isLimp = _driver.IsLimp;
        if (isLimp != _wasLimp)
        {
            _wasLimp = isLimp;

            if (isLimp)
            {
                if (_driver.IsDiving)
                {
                    ApplyDiveDrives();
                }
                else
                {
                    ApplyLimpDrives();
                }
            }
            else
            {
                ApplyBaseDrives();
            }
        }

        if (isLimp)
        {
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        _animationRig.Evaluate(_driver.LocalVelocity, _strideLength, _strideScale, deltaTime);
        _driver.LegReachScale = _animationRig.LegReachRatio;

        _reachWeight = Mathf.MoveTowards(
            _reachWeight,
            _driver.IsReachRequested ? 1f : 0f,
            _reachRampSpeed * deltaTime);

        _isPunching = _driver.TryGetPunchPose(
            out _isLeftPunch, out _punchWeight, out _punchExtension);

        _carryWeight = Mathf.MoveTowards(
            _carryWeight,
            _isCarryRequested ? 1f : 0f,
            _carryRampSpeed * deltaTime);

        ApplyLegPose();
        ApplyArmPose();
        ApplyTorsoPose();
    }

    private void Start()
    {
        _knockdown = _driver.PlayerRoot.GetComponent<PlayerKnockdown>();

        for (int i = 0; i < FOOT_BONE_NAMES.Length; i++)
        {
            _feet[i] = FindBoneTransform(FOOT_BONE_NAMES[i]);
            if (_feet[i] != null)
            {
                _footRestRotations[i] = Quaternion.Inverse(_driver.PlayerRoot.rotation) * _feet[i].rotation;
            }
        }
    }

    // 발에는 관절이 없어 정강이가 기울면 까치발이 된다. 서 있는 동안엔 발바닥을 바닥과 평행하게 둔다
    private void LevelFeet()
    {
        if (_knockdown == null || _knockdown.IsDown)
        {
            return;
        }

        for (int i = 0; i < _feet.Length; i++)
        {
            if (_feet[i] != null)
            {
                _feet[i].rotation = _driver.PlayerRoot.rotation * _footRestRotations[i];
            }
        }
    }

    private void LateUpdate()
    {
        LevelFeet();

        // 손 위치는 물리 결과라 서버/클라이언트 구분 없이 항상 갱신 (읽기 전용 작업)
        if (_carryAnchor == null || _leftHandTransform == null || _rightHandTransform == null)
        {
            return;
        }

        Vector3 leftPos = _leftHandTransform.position;
        Vector3 rightPos = _rightHandTransform.position;

        Vector3 midpoint = (leftPos + rightPos) * 0.5f;

        // 두 손을 잇는 축에 수직이면서 위를 향하는 방향으로 "정면"을 근사한다.
        Vector3 handAxis = rightPos - leftPos;
        Vector3 forward = Vector3.Cross(handAxis, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f)
        {
            forward = transform.forward;
        }

        // 1. 기본 수평 회전
        Quaternion baseRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        // 2. 시선 상하 각도(Pitch) 반영
        // carryFollowViewPitch 옵션이 켜져있고 _driver가 있다면 Pitch 각도를 회전에 적용
        Quaternion pitchRotation = Quaternion.identity;
        if (_carryFollowViewPitch && _driver != null)
        {
            pitchRotation = Quaternion.Euler(_driver.Pitch, 0f, 0f);
        }

        // 최종 앵커 회전 = 수평 정면 회전 * 시선 Pitch 회전
        Quaternion anchorRotation = baseRotation * pitchRotation;

        // 회전값(anchorRotation)을 적용하여 '앞쪽/위쪽' 오프셋이 더해진 월드 좌표 계산
        Vector3 finalPosition = midpoint + (anchorRotation * _activeCarryAnchorOffset);

        // 오프셋이 반영된 위치로 매 프레임 갱신
        _carryAnchor.SetPositionAndRotation(finalPosition, anchorRotation);

    }

    // 오른팔 뻗기 목표 오프셋. 팔 장축을 몸통 정면 방향에 정렬하고 롤은 자동 고정, yaw/pitch는 정면 반구로 클램프
    private Quaternion BuildReachOffset(ArmPose arm)
    {
        // 붙잡은 동안엔 실제 잡은 지점을 겨냥해 손이 대상에 닿게 한다.
        // 아니면 고정 yaw + 시선 pitch로 일반 뻗기 자세를 잡는다.
        if (!_driver.TryGetGrabReachAim(
                arm.Joint.transform.position, out float yaw, out float pitch))
        {
            yaw = _reachYaw;
            pitch = _reachPitch;
            if (_reachFollowViewPitch)
            {
                pitch += _driver.Pitch;
            }
        }

        pitch = Mathf.Clamp(pitch, -REACH_MAX_PITCH, REACH_MAX_PITCH);
        yaw = Mathf.Clamp(yaw, -REACH_MAX_YAW, REACH_MAX_YAW);

        return BuildReachLocalOffset(arm.ReachBodyRest, yaw, pitch);
    }

    // 펀치 위팔 목표. 팔꿈치를 내려 당긴 자세에서 몸통 정면으로 곧게 지른다
    private Quaternion BuildPunchOffset(ArmPose arm, float extension)
    {
        float yaw =
            Mathf.Lerp(_punchWindupYaw, _punchStrikeYaw, extension) * arm.SideSign;
        float pitch = Mathf.Lerp(_punchWindupPitch, _punchStrikePitch, extension);

        // 시선 연동은 뻗을수록 강하게. 당긴 자세는 겨드랑이에 붙여둔다
        if (_punchFollowViewPitch)
        {
            pitch += _driver.Pitch * extension;
        }

        pitch = Mathf.Clamp(pitch, -REACH_MAX_PITCH, REACH_MAX_PITCH);
        return BuildReachLocalOffset(arm.ReachBodyRest, yaw, pitch);
    }

    // 팔꿈치는 곧게 펴두되, 펀치 때만 접었다가 지르면서 편다. 손바닥 비틀기는 아랫팔이 맡는다
    private void ApplyElbowPose(ArmPose arm, bool isPunchArm, float roll)
    {
        if (arm.ForearmJoint == null)
        {
            return;
        }

        float bend = isPunchArm
            ? _punchElbowBend * _punchWeight * (1f - _punchExtension)
            : 0f;

        arm.ForearmJoint.SetTargetRotationLocal(
            arm.ForearmRest *
            Quaternion.AngleAxis(-arm.SideSign * bend, arm.ElbowLocalAxis) *
            Quaternion.AngleAxis(roll, Vector3.up),
            arm.ForearmRest);
    }

    // 펀치에 실리는 상체 비틀기. 당길 때 반대로 감았다가 지르면서 풀어 어깨가 따라 나간다
    private float GetPunchSpineTwist()
    {
        if (!_isPunching)
        {
            return 0f;
        }

        float side = _isLeftPunch ? -1f : 1f;
        float unwind =
            Mathf.Lerp(-PUNCH_WINDUP_TWIST_RATIO, 1f, _punchExtension);
        return -side * _punchSpineTwist * unwind * _punchWeight;
    }

    /// <summary>
    /// 팔 관절 구동력의 유일한 기록자. 매 물리 스텝 호출된다.
    /// 휘두르는 동안 팔은 세게 던지되(스프링) 잡아주지는 않는다(감쇠).
    /// 감쇠를 낮춰야 손이 목표를 지나쳐 흔들리며 따라나가는 무거운 스윙이 된다.
    /// </summary>
    /// <param name="joint">위팔 또는 아랫팔 관절</param>
    /// <param name="punchWeight">휘두르는 정도 (0=평상, 1=스윙 절정)</param>
    private void ApplyArmDrive(ConfigurableJoint joint, float punchWeight)
    {
        SetJointDrive(
            joint,
            _limbPoseSpring * Mathf.Lerp(1f, _punchSwingSpringScale, punchWeight),
            _limbPoseDamper * Mathf.Lerp(1f, _punchSwingDamperScale, punchWeight));
    }

    // 팔 장축을 몸통 기준 목표 방향으로 돌리는 rest 대비 로컬 회전 델타.
    // 최단 회전만 써서 위팔이 장축 둘레로 비틀리지 않게 한다
    private static Quaternion BuildReachLocalOffset(
        Quaternion reachBodyRest, float yaw, float pitch)
    {
        Vector3 restAxis = reachBodyRest * Vector3.up;
        Vector3 targetAxis = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        Quaternion targetBody = Quaternion.FromToRotation(restAxis, targetAxis) * reachBodyRest;
        return Quaternion.Inverse(reachBodyRest) * targetBody;
    }

    private AnimationCurve _chestArchCurve = AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f);
    private void ApplyTorsoPose()
    {
        float rawPitch = _isLookEnabled
            ? Mathf.Clamp(_driver.Pitch, -_maxLookUpAngle, _maxLookDownAngle)
            : 0f;

        // 위를 볼수록(0~1) Chest 가중치가 곡선을 따라 커짐
        float upProgress = _maxLookUpAngle > 0f
            ? Mathf.Clamp01(Mathf.Max(0f, rawPitch) / _maxLookUpAngle)
            : 0f;
        float chestExtra = _chestArchCurve.Evaluate(upProgress);

        ApplyRotation(
            SPINE_BONE_NAME,
            new Vector3(rawPitch * _spinePitchShare, GetPunchSpineTwist(), 0f));
        ApplyRotation(CHEST_BONE_NAME, LOOK_PITCH_AXIS * (rawPitch * _chestPitchShare * chestExtra));
        ApplyRotation(HEAD_BONE_NAME, LOOK_PITCH_AXIS * (rawPitch * _headPitchShare));
    }

    private void ApplyLegPose()
    {
        foreach (string boneName in LEG_BONE_NAMES)
        {
            ApplyRotation(boneName, Vector3.zero);
        }
    }

    private void ApplyArmPose()
    {
        for (int i = 0; i < _armPoses.Count; i++)
        {
            ArmPose arm = _armPoses[i];
            // 평소 팔은 애니메이션을 따르지 않고 기본 자세인 T자로 벌려 둔다
            Quaternion offset = Quaternion.identity;

            // 오른팔은 뻗기 목표와 가중치로 블렌드해 T자에서 뻗기로 매끄럽게 전환
            if (i == RIGHT_ARM_INDEX && _reachWeight > 0.001f)
            {
                offset = Quaternion.Slerp(offset, BuildReachOffset(arm), _reachWeight);
            }

            if (_carryWeight > 0.001f)
            {
                offset = Quaternion.Slerp(offset, BuildCarryOffset(arm), _carryWeight);
            }

            // 이번에 휘두르는 손만 펀치 자세로. 뻗기보다 뒤에 섞어 펀치를 우선
            bool isLeftArm = i != RIGHT_ARM_INDEX;
            bool isPunchArm = _isPunching && isLeftArm == _isLeftPunch;

            float roll = isLeftArm ? 0f : _reachRoll * _reachWeight;
            roll = Mathf.Lerp(roll, _carryRoll * arm.SideSign, _carryWeight);
            if (isPunchArm)
            {
                roll = Mathf.Lerp(roll, _punchRoll * arm.SideSign, _punchWeight);
            }
            if (isPunchArm)
            {
                offset = Quaternion.Slerp(
                    offset, BuildPunchOffset(arm, _punchExtension), _punchWeight);
            }
            else if (_carryWeight > 0.001f)
            {
                // carry 중엔 물건을 거의 즉각 따라가도록 전용 스프링으로 교체
                SetJointDrive(arm.Joint,
                    Mathf.Lerp(_limbPoseSpring, _carrySpring, _carryWeight),
                    Mathf.Lerp(_limbPoseDamper, _carryDamper, _carryWeight), Mathf.Infinity);
                SetJointDrive(arm.ForearmJoint,
                    Mathf.Lerp(_limbPoseSpring, _carrySpring, _carryWeight),
                    Mathf.Lerp(_limbPoseDamper, _carryDamper, _carryWeight), Mathf.Infinity);
            }
            else
            {
                ApplyArmDrive(arm.Joint, 0f);
                ApplyArmDrive(arm.ForearmJoint, 0f);
            }

            ApplyElbowPose(arm, isPunchArm, roll);

            arm.Joint.SetTargetRotationLocal(
                arm.RestRotation * offset, arm.RestRotation);
        }
    }

    private void ApplyRotation(string boneName, Vector3 eulerOffset)
    {
        if (!_bones.TryGetValue(boneName, out BoneData bone) || bone.Joint == null)
        {
            return;
        }

        Quaternion targetRotation = AnimatedRotation(boneName, bone.RestRotation) * Quaternion.Euler(eulerOffset);
        bone.Joint.SetTargetRotationLocal(targetRotation, bone.RestRotation);
    }

    private Quaternion AnimatedRotation(string boneName, Quaternion restRotation)
    {
        return _animationRig.TryGetLocalRotation(boneName, out Quaternion rotation) ? rotation : restRotation;
    }

    private sealed class BoneData
    {
        public BoneData(
            ConfigurableJoint joint,
            Quaternion restRotation)
        {
            Joint = joint;
            RestRotation = restRotation;
        }

        public ConfigurableJoint Joint { get; }

        public Quaternion RestRotation { get; }
    }

    private sealed class ArmPose
    {
        public ArmPose(
            ConfigurableJoint joint,
            Quaternion restRotation,
            float sideSign,
            Quaternion reachBodyRest,
            ConfigurableJoint forearmJoint,
            Quaternion forearmRest,
            Vector3 elbowLocalAxis)
        {
            Joint = joint;
            RestRotation = restRotation;
            SideSign = sideSign;
            ReachBodyRest = reachBodyRest;
            ForearmJoint = forearmJoint;
            ForearmRest = forearmRest;
            ElbowLocalAxis = elbowLocalAxis;
        }

        public ConfigurableJoint Joint { get; }

        public Quaternion RestRotation { get; }

        // 몸통 기준 팔의 좌우 방향. 왼팔 -1, 오른팔 1
        public float SideSign { get; }

        public ConfigurableJoint ForearmJoint { get; }

        public Quaternion ForearmRest { get; }

        // 위팔 기준 팔꿈치 접힘 축
        public Vector3 ElbowLocalAxis { get; }

        public Quaternion ReachBodyRest { get; }
    }

    #region  양손 잡기 관련

    /// <summary>
    /// 외부(PickupItem 등)에서 아이템별 CarryAnchor 오프셋을 지정할 때 호출.
    /// 모든 클라이언트 로컬 인스턴스에서 각자 호출해야 함 (LateUpdate는 서버/클라 구분 없이 돌아가므로).
    /// </summary>
    public void SetCarryAnchorOffset(Vector3 offset)
    {
        _activeCarryAnchorOffset = offset;
    }

    /// <summary>
    /// 아이템을 내려놓을 때 기본 오프셋으로 복원.
    /// </summary>
    public void ResetCarryAnchorOffset()
    {
        _activeCarryAnchorOffset = _carryAnchorOffset;
    }
    
    /// <summary>
    /// 외부(PickupItem 등)에서 양손 들기 자세를 요청/해제할 때 호출.
    /// 서버(권위) 인스턴스에서만 실제 팔 구동에 반영됨.
    /// </summary>
    public void SetCarryRequested(bool requested)
    {
        _isCarryRequested = requested;
    }

    /// <summary>
    /// 양손 들기 목표 오프셋. 두 팔 모두 몸 안쪽(중앙)으로 모이도록 SideSign 기준 대칭 yaw를 준다.
    /// (기존 뻗기의 "양수=바깥쪽" 관례를 반대로 뒤집어 중앙으로 모으는 방향으로 사용)
    /// </summary>
    [Header("양손 잡기 너비 보정")]
    [Tooltip("어깨에서 손까지의 대략적인 팔 길이(m). 물건 너비에 따른 삼각함수 각도 계산에 사용됩니다.")]
    [SerializeField] private float _armLength = 0.6f;

    private Quaternion BuildCarryOffset(ArmPose arm)
    {
        float baseYaw = -_carryYaw * arm.SideSign; // 기본 안쪽 모임 각도
        float finalYaw = baseYaw;

        if (_driver.TryGetCarryHalfWidth(out float halfWidth) && halfWidth > 0.001f)
        {
            float targetWidth = halfWidth * 2f; // 물건의 전체 너비
            float widthDelta = targetWidth - _defaultCarryWidth; // 너비 차이

            // 절반 너비 변화량을 팔 길이로 나누어 추가 회전 각도(라디안 -> 도) 계산
            // Mathf.Atan2(반폭 변화량, 팔 길이)
            float additionalAngleDeg = Mathf.Atan2(widthDelta * 0.5f, _armLength) * Mathf.Rad2Deg;

            // 오른팔(+1)은 바깥쪽(+), 왼팔(-1)은 바깥쪽(-)으로 추가 회전
            float yawDelta = additionalAngleDeg * arm.SideSign;

            finalYaw = baseYaw + yawDelta;
        }

        float pitch = _carryPitch;
        if (_carryFollowViewPitch)
        {
            pitch += _driver.Pitch;
        }

        pitch = Mathf.Clamp(pitch, -REACH_MAX_PITCH, REACH_MAX_PITCH);
        finalYaw = Mathf.Clamp(finalYaw, -REACH_MAX_YAW, REACH_MAX_YAW);

        return BuildReachLocalOffset(arm.ReachBodyRest, finalYaw, pitch);
    }
    public void SetCarryTarget(CarryGripPoints target)
    {
        _driver.SetCarryTarget(target);
    }

    #endregion
}

