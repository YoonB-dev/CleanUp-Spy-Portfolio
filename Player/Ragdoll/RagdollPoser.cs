using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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

    [Header("관절")]
    [FormerlySerializedAs("freeJointLimitsOnPlay")]
    [SerializeField]
    private bool _freeJointLimitsOnPlay = true;

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

    [Header("걷기")]
    [FormerlySerializedAs("walk")]
    [SerializeField]
    private bool _isWalkEnabled = true;

    [FormerlySerializedAs("bpm")]
    [SerializeField]
    private float _beatsPerMinute;

    [FormerlySerializedAs("beatMode")]
    [SerializeField]
    private BeatMode _beatMode = BeatMode.StepPerBeat;

    [FormerlySerializedAs("stepFrequency")]
    [SerializeField]
    private float _stepFrequency = 2.7f;

    [FormerlySerializedAs("phaseOffsetDeg")]
    [SerializeField]
    [Range(-180f, 180f)]
    private float _phaseOffsetDegrees;

    [FormerlySerializedAs("thighSwing")]
    [SerializeField]
    private float _thighSwing = 30f;

    [FormerlySerializedAs("shinSwing")]
    [SerializeField]
    private float _shinSwing = 30f;

    [FormerlySerializedAs("armSwing")]
    [SerializeField]
    private float _armSwing = 25f;

    [Tooltip("다리 스윙 회전축 (다리 로컬 기준 앞뒤 축)")]
    [FormerlySerializedAs("swingAxis")]
    [SerializeField]
    private Vector3 _swingAxis = new Vector3(1f, 0f, 0f);

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

    /// <summary>
    /// 관절 각도 제한 해제
    /// </summary>
    private void ReleaseJointLimits()
    {
        foreach (BoneData bone in _bones.Values)
        {
            bone.Joint.angularXMotion = ConfigurableJointMotion.Free;
            bone.Joint.angularYMotion = ConfigurableJointMotion.Free;
            bone.Joint.angularZMotion = ConfigurableJointMotion.Free;
        }
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

        _phase += GetStepFrequency() * Mathf.PI * 2f * deltaTime;

        float swing =
            Mathf.Sin(_phase + _phaseOffsetDegrees * Mathf.Deg2Rad) *
            _walkWeight;

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

    /// <summary>
    /// 초당 보행 주기 수 계산
    /// </summary>
    /// <returns>보행 주파수</returns>
    private float GetStepFrequency()
    {
        if (_beatsPerMinute <= 0f)
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

    /// <summary>
    /// 지정 관절에 초기 자세 기준 회전 오프셋 적용
    /// </summary>
    /// <param name="boneName">대상 골격 이름</param>
    /// <param name="eulerOffset">초기 자세 기준 오일러 각도</param>
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
        /// <summary>
        /// 골격의 관절 및 초기 회전 저장
        /// </summary>
        /// <param name="joint">골격의 ConfigurableJoint</param>
        /// <param name="restRotation">골격의 초기 로컬 회전</param>
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
