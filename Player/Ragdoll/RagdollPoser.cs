using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ConfigurableJoint에 보행 목표 회전 적용
/// </summary>
[RequireComponent(typeof(RagdollDriver))]
public class RagdollPoser : MonoBehaviour
{
    public enum BeatMode
    {
        StepPerBeat,
        CyclePerBeat
    }

    private const string SPINE_BONE_NAME = "Spine";
    private const string HEAD_BONE_NAME = "Head";
    private const string RIGHT_UPPER_ARM_BONE_NAME = "UpperArm.R";
    private const string LEFT_UPPER_ARM_BONE_NAME = "UpperArm.L";
    private const string LEFT_THIGH_BONE_NAME = "Thigh.L";
    private const string RIGHT_THIGH_BONE_NAME = "Thigh.R";
    private const string LEFT_SHIN_BONE_NAME = "Shin.L";
    private const string RIGHT_SHIN_BONE_NAME = "Shin.R";

    [Tooltip("시선 각도 중 머리가 따라가는 비율")]
    [SerializeField]
    private float _headPitchShare = 0.6f;

    [Header("시선 (상하)")]
    [SerializeField]
    private bool _isLookEnabled = true;

    [Tooltip("시선 상하 회전을 적용할 상체 로컬 축")]
    [SerializeField]
    private Vector3 _lookPitchAxis = new Vector3(1f, 0f, 0f);

    [Tooltip("시선 각도 중 척추가 따라가는 비율 (부호 반대면 뒤집힘)")]
    [SerializeField]
    private float _spinePitchShare = 0.4f;

    [Tooltip("시선 각도 중 머리가 따라가는 비율")]
    [SerializeField]
    private float _headPitchShare = 0.6f;

    [Tooltip("위를 볼 때 상체가 따라 젖혀지는 최대 각도")]
    [SerializeField]
    private float _maxLookUpAngle = 35f;

    [Tooltip("아래를 볼 때 상체가 따라 숙이는 최대 각도")]
    [SerializeField]
    private float _maxLookDownAngle = 45f;

    [Tooltip("팔을 T자세에서 아래로 내리는 각도. 이 자세로 앞뒤로 스윙")]
    [SerializeField]
    private float _armDownAngle = 10f;

    [Tooltip("걷기 스윙이 또렷이 보이도록 팔다리 관절을 구동하는 스프링")]
    [SerializeField]
    private float _limbPoseSpring = 2500f;

    [Tooltip("팔다리 관절 구동 감쇠")]
    [SerializeField]
    private float _limbPoseDamper = 80f;

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

    [Tooltip("뻗은 팔을 장축 둘레로 비트는 각도. 손바닥이 아래를 보게 맞춤")]
    [SerializeField]
    [Range(-180f, 180f)]
    private float _reachRoll;

    // 뻗기 회전량이 반바퀴 특이점 근처에서 튀지 않도록 제한하는 안전각
    private const float REACH_MAX_YAW = 80f;
    private const float REACH_MAX_PITCH = 70f;

    [Tooltip("다리 스윙 회전축 (다리 로컬 기준 앞뒤 축)")]
    [FormerlySerializedAs("swingAxis")]
    [SerializeField]
    private float _reachRampSpeed = 10f;

    [Tooltip("팔을 T에서 아래로 내리는 각도 (0=T, 음수=T보다 위로). 이 자세를 유지한 채 앞뒤로 스윙")]
    [SerializeField]
    private float _armDownAngle = 10f;

    [Tooltip("걷기 스윙이 또렷이 보이도록 팔다리 관절을 구동하는 스프링 (기본 slerp 1000은 약해 흐물거림)")]
    [SerializeField]
    private float _limbPoseSpring = 2500f;

    [Tooltip("팔다리 관절 구동 감쇠")]
    [SerializeField]
    private float _limbPoseDamper = 80f;

    private readonly Dictionary<string, BoneData> _bones =
        new Dictionary<string, BoneData>();

    private readonly List<ArmPose> _armPoses = new List<ArmPose>();

    private RagdollDriver _driver;
    private float _phase;
    private float _walkWeight;

    /// <summary>
    /// 골격 관절 참조 구성
    /// </summary>
    private void Awake()
    {
        _driver = GetComponent<RagdollDriver>();

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

        StiffenLimbs();
        BuildArmPoses();
    }

    /// <summary>
    /// 팔 내림·앞뒤 스윙축을 팔 로컬 프레임으로 미리 환산
    /// </summary>
    private void BuildArmPoses()
    {
        _armPoses.Clear();

        // 좌우 팔은 손 위치가 반대(±X)라 같은 부호로 돌려야 번갈아 스윙됨
        Vector3 bodyUp = transform.rotation * Vector3.up;
        AddArmPose(LEFT_UPPER_ARM_BONE_NAME, 1f, bodyUp);
        AddArmPose(RIGHT_UPPER_ARM_BONE_NAME, 1f, bodyUp);
    }

    private void AddArmPose(string boneName, float sign, Vector3 bodyUp)
    {
        if (!_bones.TryGetValue(boneName, out BoneData bone) ||
            bone.Joint == null)
        {
            return;
        }

        Quaternion boneWorld = bone.Joint.transform.rotation;
        Quaternion inverseWorld = Quaternion.Inverse(boneWorld);

        Vector3 armDirection = boneWorld * Vector3.up;
        Vector3 lowerAxis = Vector3.Cross(armDirection, Vector3.down).normalized;
        Quaternion lowerLocal =
            inverseWorld * Quaternion.AngleAxis(_armDownAngle, lowerAxis) * boneWorld;

        // 옆으로 뻗은 팔은 수직축 둘레로 돌려야 손이 앞뒤로 감
        Vector3 swingLocalAxis = inverseWorld * bodyUp;

        _armPoses.Add(new ArmPose(
            bone.Joint, bone.RestRotation, lowerLocal, swingLocalAxis, sign));
    }

    /// <summary>
    /// 걷기 스윙을 따라가도록 팔다리 관절 구동력을 높임 (기본 slerp는 약해 흐물거림)
    /// </summary>
    private void StiffenLimbs()
    {
        foreach (string boneName in new[]
                 {
                     LEFT_THIGH_BONE_NAME, RIGHT_THIGH_BONE_NAME,
                     LEFT_SHIN_BONE_NAME, RIGHT_SHIN_BONE_NAME,
                     LEFT_UPPER_ARM_BONE_NAME, RIGHT_UPPER_ARM_BONE_NAME
                 })
        {
            if (!_bones.TryGetValue(boneName, out BoneData bone) ||
                bone.Joint == null)
            {
                continue;
            }

            // slerpDrive가 실제 구동원이 되도록 회전 드라이브 모드 고정
            bone.Joint.rotationDriveMode = RotationDriveMode.Slerp;

            JointDrive drive = bone.Joint.slerpDrive;
            drive.positionSpring = _limbPoseSpring;
            drive.positionDamper = _limbPoseDamper;
            bone.Joint.slerpDrive = drive;
        }
    }

    /// <summary>
    /// Play 모드의 Inspector 값 변경을 팔다리 구동에 반영
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying && _bones.Count > 0)
        {
            StiffenLimbs();
        }
    }

        // 좌우 팔은 손 위치가 반대라 같은 부호로 돌려야 번갈아 스윙됨
        Vector3 bodyUp = transform.rotation * Vector3.up;
        AddArmPose(LEFT_UPPER_ARM_BONE_NAME, 1f, bodyUp);
        AddArmPose(RIGHT_UPPER_ARM_BONE_NAME, 1f, bodyUp);
    }

    /// <summary>
    /// 보행 목표 자세를 물리 프레임에 적용
    /// </summary>
    private void FixedUpdate()
    {
        if (_driver == null || !_driver.IsServerAuthoritative)
        {
            return;
        }

        float deltaTime = Time.fixedDeltaTime;
        bool isMoving = _driver.IsMoving;

        _walkWeight = Mathf.MoveTowards(
            _walkWeight,
            _isWalkEnabled && isMoving ? 1f : 0f,
            4f * deltaTime);

        // 옆으로 뻗은 팔은 수직축 둘레로 돌려야 손이 앞뒤로 감
        Vector3 swingLocalAxis = inverseWorld * bodyUp;

        // 뻗기 계산용으로 팔 rest 회전을 몸통 프레임으로 환산해 저장
        Quaternion reachBodyRest = Quaternion.Inverse(transform.rotation) * boneWorld;

        ApplyWalkPose(swing);
        ApplyLookPose();
    }

    /// <summary>
    /// 시선 상하 각도를 척추와 머리에 분산 적용.
    /// 하체는 앵커가 세워두므로 상체만 숙이거나 젖힘.
    /// </summary>
    private void ApplyLookPose()
    {
        if (!_isLookEnabled || _driver == null)
        {
            return;
        }

        // 시야각은 그대로 두고 상체가 따라 젖혀지는 양만 제한 (양수=아래, 음수=위)
        float pitch = Mathf.Clamp(_driver.Pitch, -_maxLookUpAngle, _maxLookDownAngle);
        ApplyRotation(SPINE_BONE_NAME, _lookPitchAxis * (pitch * _spinePitchShare));
        ApplyRotation(HEAD_BONE_NAME, _lookPitchAxis * (pitch * _headPitchShare));
    }

    // 걷기 스윙을 따라가도록 팔다리 관절 구동력을 높임 (기본 slerp는 약해 흐물거림)
    private void StiffenLimbs()
    {
        foreach (string boneName in new[]
                 {
                     LEFT_THIGH_BONE_NAME, RIGHT_THIGH_BONE_NAME,
                     LEFT_SHIN_BONE_NAME, RIGHT_SHIN_BONE_NAME,
                     LEFT_UPPER_ARM_BONE_NAME, RIGHT_UPPER_ARM_BONE_NAME,
                     LEFT_FOREARM_BONE_NAME, RIGHT_FOREARM_BONE_NAME
                 })
        {
            return _stepFrequency;
        }

        return _beatMode == BeatMode.StepPerBeat
            ? _beatsPerMinute / 120f
            : _beatsPerMinute / 60f;
    }

    /// <summary>
    /// 다리와 양팔에 보행 자세 적용
    /// </summary>
    /// <param name="swing">현재 보행 주기의 회전 가중치</param>
    private void ApplyWalkPose(float swing)
    {
        ApplyRotation(LEFT_THIGH_BONE_NAME, _swingAxis * (_thighSwing * swing));
        ApplyRotation(RIGHT_THIGH_BONE_NAME, _swingAxis * (-_thighSwing * swing));
        ApplyRotation(
            LEFT_SHIN_BONE_NAME,
            _swingAxis * (_shinSwing * Mathf.Max(0f, -swing)));
        ApplyRotation(
            RIGHT_SHIN_BONE_NAME,
            _swingAxis * (_shinSwing * Mathf.Max(0f, swing)));

        ApplyArmPose(swing);
    }

    /// <summary>
    /// 양팔을 내린 자세에서 앞뒤로 스윙 (좌우 교차)
    /// </summary>
    /// <param name="swing">현재 보행 주기의 회전 가중치</param>
    private void ApplyArmPose(float swing)
    {
        foreach (ArmPose arm in _armPoses)
        {
            Quaternion offset =
                Quaternion.AngleAxis(
                    arm.Sign * _armSwing * swing, arm.SwingLocalAxis) *
                arm.LowerLocal;
            arm.Joint.SetTargetRotationLocal(
                arm.RestRotation * offset, arm.RestRotation);
        }
    }

    // 팔 장축을 몸통 기준 목표 방향에 맞추는 rest 대비 로컬 회전 델타. 롤은 몸통 up 기준 + roll 비틀림으로 고정
    private static Quaternion BuildReachLocalOffset(
        Quaternion reachBodyRest, float yaw, float pitch, float roll)
    {
        Vector3 armAxis =
            (Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward).normalized;

        // 롤 기준으로 몸통 up을 장축에 직교하도록 투영
        Vector3 upFace = Vector3.up - Vector3.Dot(Vector3.up, armAxis) * armAxis;
        if (upFace.sqrMagnitude < 1e-4f)
        {
            upFace = Vector3.forward - Vector3.Dot(Vector3.forward, armAxis) * armAxis;
        }

        upFace.Normalize();

        // 손바닥 방향을 맞추도록 장축 둘레로 추가 비틀기
        Quaternion targetBody =
            Quaternion.AngleAxis(roll, armAxis) * Quaternion.LookRotation(upFace, armAxis);
        return Quaternion.Inverse(reachBodyRest) * targetBody;
    }

    private sealed class BoneData
    {
        public ArmPose(
            ConfigurableJoint joint,
            Quaternion restRotation,
            Quaternion lowerLocal,
            Vector3 swingLocalAxis,
            float sign,
            Quaternion reachBodyRest)
        {
            Joint = joint;
            RestRotation = restRotation;
            LowerLocal = lowerLocal;
            SwingLocalAxis = swingLocalAxis;
            Sign = sign;
            ReachBodyRest = reachBodyRest;
        }

        public ConfigurableJoint Joint { get; }

        public Quaternion RestRotation { get; }

        public Quaternion LowerLocal { get; }

        public Vector3 SwingLocalAxis { get; }

        public float Sign { get; }

        public Quaternion ReachBodyRest { get; }
    }

    private sealed class ArmPose
    {
        public ArmPose(
            ConfigurableJoint joint,
            Quaternion restRotation,
            Quaternion lowerLocal,
            Vector3 swingLocalAxis,
            float sign)
        {
            Joint = joint;
            RestRotation = restRotation;
            LowerLocal = lowerLocal;
            SwingLocalAxis = swingLocalAxis;
            Sign = sign;
        }

        public ConfigurableJoint Joint { get; }

        public Quaternion RestRotation { get; }

        public Quaternion LowerLocal { get; }

        public Vector3 SwingLocalAxis { get; }

        public float Sign { get; }
    }
}
