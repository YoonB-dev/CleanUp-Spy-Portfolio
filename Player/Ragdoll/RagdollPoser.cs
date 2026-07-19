// 액티브 래그돌의 조준 및 보행 자세 제어

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// ConfigurableJoint에 조준 및 보행 목표 회전 적용
/// </summary>
[RequireComponent(typeof(RagdollDriver))]
public class RagdollPoser : MonoBehaviour
{
    public enum AimMode
    {
        TargetPosition,
        Angle
    }

    public enum BeatMode
    {
        StepPerBeat,
        CyclePerBeat
    }

    private const string CHEST_BONE_NAME = "Chest";
    private const string SPINE_BONE_NAME = "Spine";
    private const string RIGHT_SHOULDER_BONE_NAME = "Shoulder.R";
    private const string RIGHT_UPPER_ARM_BONE_NAME = "UpperArm.R";
    private const string LEFT_UPPER_ARM_BONE_NAME = "UpperArm.L";
    private const string RIGHT_FOREARM_BONE_NAME = "Forearm.R";
    private const string RIGHT_HAND_BONE_NAME = "Hand.R";
    private const string LEFT_THIGH_BONE_NAME = "Thigh.L";
    private const string RIGHT_THIGH_BONE_NAME = "Thigh.R";
    private const string LEFT_SHIN_BONE_NAME = "Shin.L";
    private const string RIGHT_SHIN_BONE_NAME = "Shin.R";
    private const float MINIMUM_VECTOR_SQR_MAGNITUDE = 0.000001f;

    [Header("조준")]
    [FormerlySerializedAs("aim")]
    [SerializeField]
    private bool _isAimEnabled = true;

    [FormerlySerializedAs("aimMode")]
    [SerializeField]
    private AimMode _aimMode = AimMode.TargetPosition;

    [FormerlySerializedAs("aimBlendSpeed")]
    [SerializeField]
    private float _aimBlendSpeed = 6f;

    [FormerlySerializedAs("aimFollowsView")]
    [SerializeField]
    private bool _doesAimFollowView;

    [Header("관절")]
    [FormerlySerializedAs("freeJointLimitsOnPlay")]
    [SerializeField]
    private bool _freeJointLimitsOnPlay = true;

    [Header("목표점 조준")]
    [FormerlySerializedAs("handTarget")]
    [SerializeField]
    [Tooltip("조준 기준 방향의 오른쪽, 위, 앞을 기준으로 한 손 목표 위치")]
    private Vector3 _handTarget = new Vector3(0.25f, 0f, 0.7f);

    [FormerlySerializedAs("reachForce")]
    [SerializeField]
    private float _reachForce = 60f;

    [FormerlySerializedAs("reachDamping")]
    [SerializeField]
    private float _reachDamping = 8f;

    [FormerlySerializedAs("gripRoll")]
    [SerializeField]
    private Vector3 _gripRoll = new Vector3(8.44f, 13f, 0f);

    [FormerlySerializedAs("gripSpring")]
    [SerializeField]
    private float _gripSpring = 1500f;

    [FormerlySerializedAs("armSpringWhileAiming")]
    [SerializeField]
    private float _armSpringWhileAiming = 80f;

    [Header("각도 조준")]
    [FormerlySerializedAs("upperArmRightAim")]
    [SerializeField]
    private Vector3 _rightUpperArmAim = new Vector3(19.9f, 59.1f, -43.2f);

    [FormerlySerializedAs("forearmRightAim")]
    [SerializeField]
    private Vector3 _rightForearmAim = new Vector3(-61.7f, -71.6f, -99.6f);

    [Header("어깨 분산")]
    [FormerlySerializedAs("shoulderShare")]
    [SerializeField]
    [Range(0f, 1f)]
    private float _shoulderShare = 0.4f;

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

    [FormerlySerializedAs("swingAxis")]
    [SerializeField]
    private Vector3 _swingAxis = new Vector3(1f, 0f, 0f);

    private readonly Dictionary<string, BoneData> _bones =
        new Dictionary<string, BoneData>();

    private RagdollDriver _driver;
    private FixedJoint _propJoint;
    private Transform _chest;
    private Transform _rightShoulder;
    private Transform _rightHand;
    private Transform _rightForearm;
    private Rigidbody _rightForearmRigidbody;
    private Quaternion _shoulderRestRotation;
    private Quaternion _upperArmRelativeRestRotation;
    private float _phase;
    private float _walkWeight;
    private float _aimWeight;

    /// <summary>
    /// 골격 및 관절의 초기 참조 구성
    /// </summary>
    private void Awake()
    {
        _driver = GetComponent<RagdollDriver>();

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

        _chest = FindBone(CHEST_BONE_NAME);
        _rightShoulder = FindBone(RIGHT_SHOULDER_BONE_NAME);
        _rightHand = FindBone(RIGHT_HAND_BONE_NAME);
        _rightForearm = FindBone(RIGHT_FOREARM_BONE_NAME);

        if (_rightForearm != null)
        {
            _rightForearmRigidbody = _rightForearm.GetComponent<Rigidbody>();
        }

        CacheShoulderRestRotation();
    }

    /// <summary>
    /// 현재 소품의 FixedJoint 참조 초기화
    /// </summary>
    private void Start()
    {
        _propJoint = FindPropJoint();
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
    /// 어깨 분산용 초기 회전 저장
    /// </summary>
    private void CacheShoulderRestRotation()
    {
        if (_rightShoulder != null)
        {
            _shoulderRestRotation = _rightShoulder.localRotation;
        }

        if (_chest == null || _rightForearm == null)
        {
            return;
        }

        Transform rightUpperArm = FindBone(RIGHT_UPPER_ARM_BONE_NAME);

        if (rightUpperArm != null)
        {
            _upperArmRelativeRestRotation =
                Quaternion.Inverse(_chest.rotation) * rightUpperArm.rotation;
        }
    }

    /// <summary>
    /// 조준 및 보행 목표 자세를 물리 프레임에 적용
    /// </summary>
    private void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;
        bool isHoldingProp = _propJoint != null;
        bool isMoving = _driver != null && _driver.IsMoving;

        _aimWeight = Mathf.MoveTowards(
            _aimWeight,
            _isAimEnabled && isHoldingProp ? 1f : 0f,
            _aimBlendSpeed * deltaTime);
        _walkWeight = Mathf.MoveTowards(
            _walkWeight,
            _isWalkEnabled && isMoving ? 1f : 0f,
            4f * deltaTime);

        _phase += GetStepFrequency() * Mathf.PI * 2f * deltaTime;

        float swing =
            Mathf.Sin(_phase + _phaseOffsetDegrees * Mathf.Deg2Rad) *
            _walkWeight;

        ApplyWalkPose(swing);
        ApplyAimPose(swing);
        ShareShoulderRotation();
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
    /// 다리 및 왼팔에 보행 자세 적용
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
        ApplyRotation(
            LEFT_UPPER_ARM_BONE_NAME,
            _swingAxis * (-_armSwing * swing));
    }

    /// <summary>
    /// 오른팔에 현재 조준 방식의 자세 적용
    /// </summary>
    /// <param name="swing">현재 보행 주기의 회전 가중치</param>
    private void ApplyAimPose(float swing)
    {
        if (_aimMode == AimMode.TargetPosition)
        {
            ApplyTargetPositionAim(swing);
            return;
        }

        Vector3 walkRotation = _swingAxis * (_armSwing * swing);
        ApplyRotation(
            RIGHT_UPPER_ARM_BONE_NAME,
            Vector3.Lerp(walkRotation, _rightUpperArmAim, _aimWeight));
        ApplyRotation(
            RIGHT_FOREARM_BONE_NAME,
            Vector3.Lerp(Vector3.zero, _rightForearmAim, _aimWeight));
    }

    /// <summary>
    /// 목표점 기반 조준 자세를 오른팔에 적용
    /// </summary>
    /// <param name="swing">현재 보행 주기의 회전 가중치</param>
    private void ApplyTargetPositionAim(float swing)
    {
        float baseSpring = GetDefaultSpring();
        SetJointSpring(
            RIGHT_UPPER_ARM_BONE_NAME,
            Mathf.Lerp(baseSpring, _armSpringWhileAiming, _aimWeight));
        SetJointSpring(
            RIGHT_FOREARM_BONE_NAME,
            Mathf.Lerp(baseSpring, _gripSpring, _aimWeight));

        if (_aimWeight < 0.01f)
        {
            ApplyRotation(
                RIGHT_UPPER_ARM_BONE_NAME,
                _swingAxis * (_armSwing * swing));
            ApplyRotation(RIGHT_FOREARM_BONE_NAME, Vector3.zero);
            return;
        }

        ReachTargetPosition();
    }

    /// <summary>
    /// 현재 조준 목표의 월드 위치 계산
    /// </summary>
    /// <returns>조준 목표의 월드 위치</returns>
    private Vector3 GetTargetPosition()
    {
        if (_chest == null)
        {
            return transform.position;
        }

        return _chest.position + GetAimRotation() * _handTarget;
    }

    /// <summary>
    /// 현재 조준 기준 회전 계산
    /// </summary>
    /// <returns>조준 기준 회전</returns>
    private Quaternion GetAimRotation()
    {
        if (_driver == null)
        {
            return transform.rotation;
        }

        return _doesAimFollowView
            ? _driver.AimRotation
            : _driver.BodyRotation;
    }

    /// <summary>
    /// 오른팔 목표 위치 이동 및 조준 회전 설정
    /// </summary>
    private void ReachTargetPosition()
    {
        if (_chest == null || _rightForearmRigidbody == null)
        {
            return;
        }

        Vector3 targetPosition = GetTargetPosition();
        Vector3 handPosition = _rightHand != null
            ? _rightHand.position
            : _rightForearmRigidbody.worldCenterOfMass;
        Vector3 handVelocity =
            _rightForearmRigidbody.GetPointVelocity(handPosition);
        Vector3 force =
            (targetPosition - handPosition) * _reachForce -
            handVelocity * _reachDamping;

        _rightForearmRigidbody.AddForceAtPosition(
            force * _aimWeight,
            handPosition,
            ForceMode.Acceleration);

        if (!_bones.TryGetValue(
                RIGHT_FOREARM_BONE_NAME,
                out BoneData forearmBone) ||
            forearmBone.Joint == null ||
            _rightForearm.parent == null)
        {
            return;
        }

        Vector3 targetDirection =
            (targetPosition - _rightForearm.position).normalized;

        if (targetDirection.sqrMagnitude < MINIMUM_VECTOR_SQR_MAGNITUDE)
        {
            return;
        }

        Quaternion aimRotation = GetAimRotation();
        Vector3 referenceUp = aimRotation * Vector3.up;
        Vector3 side = Vector3.Cross(referenceUp, targetDirection);

        if (side.sqrMagnitude < MINIMUM_VECTOR_SQR_MAGNITUDE)
        {
            side = Vector3.Cross(
                aimRotation * Vector3.forward,
                targetDirection);
        }

        // 뼈의 로컬 Y축을 진행 방향으로 사용하므로 LookRotation의 up으로 지정
        Quaternion targetWorldRotation =
            Quaternion.LookRotation(side.normalized, targetDirection) *
            Quaternion.Euler(_gripRoll);
        Quaternion targetLocalRotation =
            Quaternion.Inverse(_rightForearm.parent.rotation) *
            targetWorldRotation;

        forearmBone.Joint.SetTargetRotationLocal(
            targetLocalRotation,
            forearmBone.RestRotation);
    }

    /// <summary>
    /// 위팔 회전 일부를 어깨에 분배
    /// </summary>
    private void ShareShoulderRotation()
    {
        if (_shoulderShare <= 0.001f ||
            _rightShoulder == null ||
            _chest == null)
        {
            return;
        }

        if (!_bones.TryGetValue(
                RIGHT_UPPER_ARM_BONE_NAME,
                out BoneData upperArmBone) ||
            upperArmBone.Joint == null)
        {
            return;
        }

        Quaternion currentRelativeRotation =
            Quaternion.Inverse(_chest.rotation) *
            upperArmBone.Joint.transform.rotation;
        Quaternion rotationDelta =
            currentRelativeRotation *
            Quaternion.Inverse(_upperArmRelativeRestRotation);

        _rightShoulder.localRotation =
            Quaternion.Slerp(
                Quaternion.identity,
                rotationDelta,
                _shoulderShare) *
            _shoulderRestRotation;
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

    /// <summary>
    /// 지정 관절의 Slerp Spring 값 설정
    /// </summary>
    /// <param name="boneName">대상 골격 이름</param>
    /// <param name="spring">적용할 Spring 값</param>
    private void SetJointSpring(string boneName, float spring)
    {
        if (!_bones.TryGetValue(boneName, out BoneData bone) ||
            bone.Joint == null)
        {
            return;
        }

        JointDrive drive = bone.Joint.slerpDrive;
        drive.positionSpring = spring;
        bone.Joint.slerpDrive = drive;
    }

    /// <summary>
    /// 기준 관절의 Spring 값 조회
    /// </summary>
    /// <returns>기준 Spring 값</returns>
    private float GetDefaultSpring()
    {
        if (_bones.TryGetValue(SPINE_BONE_NAME, out BoneData spineBone) &&
            spineBone.Joint != null)
        {
            return spineBone.Joint.slerpDrive.positionSpring;
        }

        return 500f;
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

    /// <summary>
    /// 오른팔에 연결된 소품 FixedJoint 검색
    /// </summary>
    /// <returns>연결된 FixedJoint. 없으면 null</returns>
    private FixedJoint FindPropJoint()
    {
        if (_rightForearmRigidbody == null)
        {
            return null;
        }

        foreach (FixedJoint fixedJoint in
                 Object.FindObjectsByType<FixedJoint>(
                     FindObjectsInactive.Include))
        {
            if (fixedJoint.connectedBody == _rightForearmRigidbody)
            {
                return fixedJoint;
            }
        }

        return null;
    }

    /// <summary>
    /// 조준 목표 및 몸체 방향을 Scene 뷰에 표시
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || _chest == null)
        {
            return;
        }

        Vector3 targetPosition = GetTargetPosition();

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(targetPosition, 0.07f);
        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawLine(_chest.position, targetPosition);

        if (_rightHand != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(_rightHand.position, targetPosition);
            Gizmos.DrawWireSphere(_rightHand.position, 0.04f);
        }

        if (_driver == null)
        {
            return;
        }

        Quaternion bodyRotation = _driver.BodyRotation;
        Vector3 origin = _chest.position;

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(
            origin,
            origin + bodyRotation * Vector3.forward * 0.6f);
        Gizmos.color = Color.red;
        Gizmos.DrawLine(
            origin,
            origin + bodyRotation * Vector3.right * 0.3f);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(
            origin,
            origin + bodyRotation * Vector3.up * 0.3f);
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
}
