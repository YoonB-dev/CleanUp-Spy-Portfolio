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

    private static readonly Vector3 LOOK_PITCH_AXIS = new Vector3(1f, 0f, 0f);

    private static readonly string[] LEG_BONE_NAMES =
    {
        LEFT_THIGH_BONE_NAME, RIGHT_THIGH_BONE_NAME,
        LEFT_SHIN_BONE_NAME, RIGHT_SHIN_BONE_NAME
    };

    private static readonly string[] TORSO_BONE_NAMES =
    {
        SPINE_BONE_NAME, CHEST_BONE_NAME, HEAD_BONE_NAME
    };

    [Header("관절")]
    [SerializeField]
    private bool _freeJointLimitsOnPlay = true;

    [Tooltip("걷기 스윙이 또렷이 보이도록 팔다리 관절을 구동하는 스프링")]
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

    [Header("시선 (상하)")]
    [SerializeField]
    private bool _isLookEnabled = true;

    [Tooltip("시선 각도 중 척추가 따라가는 비율. 부호 반대면 뒤집힘")]
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

    [Header("걷기")]
    [SerializeField]
    private bool _isWalkEnabled = true;

    [SerializeField]
    private float _stepFrequency = 2.7f;

    [SerializeField]
    [Range(-180f, 180f)]
    private float _phaseOffsetDegrees;

    [SerializeField]
    private float _thighSwing = 22f;

    [SerializeField]
    private float _shinSwing = 25f;

    [SerializeField]
    private float _armSwing = 25f;

    [Tooltip("다리 스윙 회전축. 다리 로컬 기준 앞뒤 축")]
    [SerializeField]
    private Vector3 _swingAxis = new Vector3(1f, 0f, 0f);

    [Tooltip("팔을 T자세에서 아래로 내리는 각도. 이 자세로 앞뒤로 스윙")]
    [SerializeField]
    private float _armDownAngle = 10f;

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

    [Tooltip("펀치 중 팔을 장축 둘레로 비트는 각도")]
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
    private float _phase;
    private float _walkWeight;
    private float _reachWeight;
    private bool _isPunching;
    private bool _isLeftPunch;
    private float _punchWeight;
    private float _punchExtension;

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

        ApplyBaseDrives();
        BuildArmPoses();
    }

    private void BuildArmPoses()
    {
        _armPoses.Clear();

        // 좌우 팔은 손 위치가 반대라 같은 부호로 돌려야 번갈아 스윙됨
        Vector3 bodyUp = transform.rotation * Vector3.up;
        AddArmPose(
            LEFT_UPPER_ARM_BONE_NAME, LEFT_FOREARM_BONE_NAME, 1f, -1f, bodyUp);
        AddArmPose(
            RIGHT_UPPER_ARM_BONE_NAME, RIGHT_FOREARM_BONE_NAME, 1f, 1f, bodyUp);
    }

    private void AddArmPose(
        string boneName,
        string forearmBoneName,
        float swingSign,
        float sideSign,
        Vector3 bodyUp)
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

        // 뻗기 계산용으로 팔 rest 회전을 몸통 프레임으로 환산해 저장
        Quaternion reachBodyRest = Quaternion.Inverse(transform.rotation) * boneWorld;

        _bones.TryGetValue(forearmBoneName, out BoneData forearm);
        ConfigurableJoint forearmJoint = forearm?.Joint;
        Vector3 elbowLocalAxis = forearmJoint != null
            ? Quaternion.Inverse(forearmJoint.transform.rotation) * bodyUp
            : Vector3.up;

        _armPoses.Add(new ArmPose(
            bone.Joint, bone.RestRotation, lowerLocal, swingLocalAxis,
            swingSign, sideSign, reachBodyRest,
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
    /// 본 관절 구동력을 쓰는 유일한 경로. maximumForce는 프리팹 값을 유지한다.
    /// </summary>
    /// <param name="joint">대상 관절. null이면 무시</param>
    /// <param name="spring">목표 회전을 따라가는 힘</param>
    /// <param name="damper">구동 감쇠</param>
    private static void SetJointDrive(
        ConfigurableJoint joint, float spring, float damper)
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
    }

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

        _phase += _stepFrequency * Mathf.PI * 2f * deltaTime;

        float swing =
            Mathf.Sin(_phase + _phaseOffsetDegrees * Mathf.Deg2Rad) *
            _walkWeight;

        _reachWeight = Mathf.MoveTowards(
            _reachWeight,
            _driver.IsReachRequested ? 1f : 0f,
            _reachRampSpeed * deltaTime);

        _isPunching = _driver.TryGetPunchPose(
            out _isLeftPunch, out _punchWeight, out _punchExtension);

        ApplyWalkPose(swing);
        ApplyTorsoPose();
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

        return BuildReachLocalOffset(arm.ReachBodyRest, yaw, pitch, _reachRoll);
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
        return BuildReachLocalOffset(
            arm.ReachBodyRest, yaw, pitch, _punchRoll * arm.SideSign);
    }

    // 팔꿈치는 곧게 펴두되, 펀치 때만 접었다가 지르면서 편다
    private void ApplyElbowPose(ArmPose arm, bool isPunchArm)
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
            Quaternion.AngleAxis(-arm.SideSign * bend, arm.ElbowLocalAxis),
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

    private void ApplyTorsoPose()
    {
        float pitch = _isLookEnabled
            ? Mathf.Clamp(_driver.Pitch, -_maxLookUpAngle, _maxLookDownAngle)
            : 0f;

        ApplyRotation(
            SPINE_BONE_NAME,
            new Vector3(pitch * _spinePitchShare, GetPunchSpineTwist(), 0f));
        ApplyRotation(HEAD_BONE_NAME, LOOK_PITCH_AXIS * (pitch * _headPitchShare));
    }

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

    private void ApplyArmPose(float swing)
    {
        for (int i = 0; i < _armPoses.Count; i++)
        {
            ArmPose arm = _armPoses[i];
            Quaternion offset =
                Quaternion.AngleAxis(
                    arm.SwingSign * _armSwing * swing, arm.SwingLocalAxis) *
                arm.LowerLocal;

            // 오른팔은 뻗기 목표와 가중치로 블렌드해 스윙에서 뻗기로 매끄럽게 전환
            if (i == RIGHT_ARM_INDEX && _reachWeight > 0.001f)
            {
                offset = Quaternion.Slerp(offset, BuildReachOffset(arm), _reachWeight);
            }

            // 이번에 휘두르는 손만 펀치 자세로. 뻗기보다 뒤에 섞어 펀치를 우선
            bool isLeftArm = i != RIGHT_ARM_INDEX;
            bool isPunchArm = _isPunching && isLeftArm == _isLeftPunch;
            if (isPunchArm)
            {
                offset = Quaternion.Slerp(
                    offset, BuildPunchOffset(arm, _punchExtension), _punchWeight);
            }

            // 감을 땐 또렷하게 당기고, 휘두르는 구간에서만 풀어 팔이 관성으로 날아가게
            float springWeight = isPunchArm ? _punchWeight * _punchExtension : 0f;
            ApplyArmDrive(arm.Joint, springWeight);
            ApplyArmDrive(arm.ForearmJoint, springWeight);
            ApplyElbowPose(arm, isPunchArm);

            arm.Joint.SetTargetRotationLocal(
                arm.RestRotation * offset, arm.RestRotation);
        }
    }

    private void ApplyRotation(string boneName, Vector3 eulerOffset)
    {
        if (!_bones.TryGetValue(boneName, out BoneData bone) ||
            bone.Joint == null)
        {
            return;
        }

        bone.Joint.SetTargetRotationLocal(
            bone.RestRotation * Quaternion.Euler(eulerOffset),
            bone.RestRotation);
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
            Quaternion lowerLocal,
            Vector3 swingLocalAxis,
            float swingSign,
            float sideSign,
            Quaternion reachBodyRest,
            ConfigurableJoint forearmJoint,
            Quaternion forearmRest,
            Vector3 elbowLocalAxis)
        {
            Joint = joint;
            RestRotation = restRotation;
            LowerLocal = lowerLocal;
            SwingLocalAxis = swingLocalAxis;
            SwingSign = swingSign;
            SideSign = sideSign;
            ReachBodyRest = reachBodyRest;
            ForearmJoint = forearmJoint;
            ForearmRest = forearmRest;
            ElbowLocalAxis = elbowLocalAxis;
        }

        public ConfigurableJoint Joint { get; }

        public Quaternion RestRotation { get; }

        public Quaternion LowerLocal { get; }

        public Vector3 SwingLocalAxis { get; }

        public float SwingSign { get; }

        // 몸통 기준 팔의 좌우 방향. 왼팔 -1, 오른팔 1
        public float SideSign { get; }

        public ConfigurableJoint ForearmJoint { get; }

        public Quaternion ForearmRest { get; }

        // 위팔 기준 팔꿈치 접힘 축
        public Vector3 ElbowLocalAxis { get; }

        public Quaternion ReachBodyRest { get; }
    }
}

