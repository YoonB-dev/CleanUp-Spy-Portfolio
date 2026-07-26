using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ConfigurableJoint에 보행, 시선, 잡기 목표 회전을 적용 (서버 권위)
/// </summary>
[RequireComponent(typeof(RagdollDriver))]
public class RagdollPoser : MonoBehaviour
{
    private const string SPINE_BONE_NAME = "Spine";
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

    [Header("관절")]
    [SerializeField]
    private bool _freeJointLimitsOnPlay = true;

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

    // BuildArmPoses가 왼팔 다음 오른팔을 넣으므로 오른팔은 인덱스 1
    private const int RIGHT_ARM_INDEX = 1;

    [Tooltip("뻗기 자세 전환 속도")]
    [SerializeField]
    private float _reachRampSpeed = 10f;

    private readonly Dictionary<string, BoneData> _bones =
        new Dictionary<string, BoneData>();

    private readonly List<ArmPose> _armPoses = new List<ArmPose>();

    private RagdollDriver _driver;
    private float _phase;
    private float _walkWeight;
    private float _reachWeight;

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

    private void BuildArmPoses()
    {
        _armPoses.Clear();

        // 좌우 팔은 손 위치가 반대라 같은 부호로 돌려야 번갈아 스윙됨
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

        // 뻗기 계산용으로 팔 rest 회전을 몸통 프레임으로 환산해 저장
        Quaternion reachBodyRest = Quaternion.Inverse(transform.rotation) * boneWorld;

        _armPoses.Add(new ArmPose(
            bone.Joint, bone.RestRotation, lowerLocal, swingLocalAxis, sign,
            reachBodyRest));
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
            if (!_bones.TryGetValue(boneName, out BoneData bone) ||
                bone.Joint == null)
            {
                continue;
            }

            bone.Joint.rotationDriveMode = RotationDriveMode.Slerp;

            JointDrive drive = bone.Joint.slerpDrive;
            drive.positionSpring = _limbPoseSpring;
            drive.positionDamper = _limbPoseDamper;
            bone.Joint.slerpDrive = drive;
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying && _bones.Count > 0)
        {
            StiffenLimbs();
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

        ApplyWalkPose(swing);
        ApplyLookPose();
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

    // 시선 상하 각도를 척추와 머리에 나눠 적용. 하체는 앵커가 세워두므로 상체만 젖힘
    private void ApplyLookPose()
    {
        if (!_isLookEnabled || _driver == null)
        {
            return;
        }

        float pitch = Mathf.Clamp(_driver.Pitch, -_maxLookUpAngle, _maxLookDownAngle);
        ApplyRotation(SPINE_BONE_NAME, LOOK_PITCH_AXIS * (pitch * _spinePitchShare));
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
                    arm.Sign * _armSwing * swing, arm.SwingLocalAxis) *
                arm.LowerLocal;

            // 오른팔은 뻗기 목표와 가중치로 블렌드해 스윙에서 뻗기로 매끄럽게 전환
            if (i == RIGHT_ARM_INDEX && _reachWeight > 0.001f)
            {
                offset = Quaternion.Slerp(offset, BuildReachOffset(arm), _reachWeight);
            }

            arm.Joint.SetTargetRotationLocal(
                arm.RestRotation * offset, arm.RestRotation);
        }

        // 아랫팔은 곧게 유지해 팔꿈치가 꺾이지 않게
        ApplyRotation(LEFT_FOREARM_BONE_NAME, Vector3.zero);
        ApplyRotation(RIGHT_FOREARM_BONE_NAME, Vector3.zero);
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
}

