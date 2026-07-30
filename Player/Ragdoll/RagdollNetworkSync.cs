using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 서버 권위 래그돌 포즈를 압축해 클라로 동기화.
/// Player의 NetworkObject에 얹혀 Hips 월드 pose + 나머지 본 로컬 회전을 전송.
/// </summary>
public class RagdollNetworkSync : NetworkBehaviour
{
    // 인덱스 0 = Hips(월드 pose), 1~10 = 로컬 회전
    private static readonly string[] BONE_NAMES =
    {
        "Hips", "Spine", "Head",
        "Thigh.L", "Thigh.R", "Shin.L", "Shin.R",
        "UpperArm.L", "UpperArm.R", "Forearm.L", "Forearm.R"
    };
    private const int BONE_COUNT = 11;
    private const float INTERP_SPEED = 18f;

    // 본을 못 찾았을 때 쓰는 기본 회전값
    private static readonly uint IDENTITY_ROTATION =
        QuaternionCompression.Compress(Quaternion.identity);

    private readonly NetworkVariable<RagdollPose> _pose = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Transform[] _bones;
    private bool _isServerSim;
    private bool _hasPose;   // 클라: 첫 포즈 수신 여부
    private bool _applied;   // 클라: 최초 적용은 보간 없이 스냅
    private RagdollPoser _ragdollPoser;

    /// <summary>
    /// 이탈 전 미리 바인딩된 RagdollPoser 참조. Player 쪽(이 컴포넌트)은 계층에서 안 떨어지므로
    /// PickupItem 등 외부에서 홀더의 손 앵커를 찾을 때 이 프로퍼티를 통해야 안전함.
    /// </summary>
    public RagdollPoser Poser => _ragdollPoser;

    public void BindRagdoll(Transform ragdollRoot)
    {
        _bones = CollectBones(ragdollRoot);
        _ragdollPoser = ragdollRoot.GetComponent<RagdollPoser>();
    }

    public override void OnNetworkSpawn()
    {
        if (_bones == null)
        {
            Debug.LogError(
                "[RagdollNetworkSync] RagdollDriver.BindRagdoll이 호출되지 않았습니다.", this);
            enabled = false;
            return;
        }

        _isServerSim = IsServer;

        if (!_isServerSim)
        {
            _pose.OnValueChanged += OnPoseReceived;
        }
    }

    public override void OnNetworkDespawn()
    {
        _pose.OnValueChanged -= OnPoseReceived;
    }

    private void OnPoseReceived(RagdollPose previous, RagdollPose current)
    {
        _hasPose = true;
    }

    /// <summary>
    /// 서버 물리 결과를 매 물리 스텝 압축해 네트워크 변수에 반영
    /// </summary>
    private void FixedUpdate()
    {
        if (!_isServerSim || _bones == null || _bones[0] == null)
        {
            return;
        }

        RagdollPose pose = default;
        pose.HipsPosition = _bones[0].position;
        pose[0] = QuaternionCompression.Compress(_bones[0].rotation);
        for (int i = 1; i < BONE_COUNT; i++)
        {
            pose[i] = _bones[i] != null
                ? QuaternionCompression.Compress(_bones[i].localRotation)
                : IDENTITY_ROTATION;
        }

        _pose.Value = pose;
    }

    /// <summary>
    /// 클라에서 수신 포즈를 kinematic 본에 보간 적용
    /// </summary>
    private void Update()
    {
        if (_isServerSim || _bones == null || _bones[0] == null || !_hasPose)
        {
            return;
        }

        RagdollPose pose = _pose.Value;
        // 첫 적용은 스냅(원점에서 실제 슬라이드 방지), 이후 보간
        float t = _applied ? 1f - Mathf.Exp(-INTERP_SPEED * Time.deltaTime) : 1f;
        _applied = true;

        _bones[0].position = Vector3.Lerp(_bones[0].position, pose.HipsPosition, t);
        _bones[0].rotation = Quaternion.Slerp(
            _bones[0].rotation, QuaternionCompression.Decompress(pose[0]), t);

        for (int i = 1; i < BONE_COUNT; i++)
        {
            if (_bones[i] == null)
            {
                continue;
            }

            _bones[i].localRotation = Quaternion.Slerp(
                _bones[i].localRotation, QuaternionCompression.Decompress(pose[i]), t);
        }
    }

    private Transform[] CollectBones(Transform root)
    {
        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        Transform[] bones = new Transform[BONE_COUNT];
        for (int i = 0; i < BONE_COUNT; i++)
        {
            foreach (Transform bone in all)
            {
                if (bone.name == BONE_NAMES[i])
                {
                    bones[i] = bone;
                    break;
                }
            }
        }

        return bones;
    }
}

/// <summary>
/// 압축된 래그돌 포즈 (Hips 월드 위치 + 본 11개 회전)
/// </summary>
public struct RagdollPose : INetworkSerializable, IEquatable<RagdollPose>
{
    public Vector3 HipsPosition;
    private uint _r0, _r1, _r2, _r3, _r4, _r5, _r6, _r7, _r8, _r9, _r10;

    public uint this[int index]
    {
        get => index switch
        {
            0 => _r0,
            1 => _r1,
            2 => _r2,
            3 => _r3,
            4 => _r4,
            5 => _r5,
            6 => _r6,
            7 => _r7,
            8 => _r8,
            9 => _r9,
            _ => _r10
        };
        set
        {
            switch (index)
            {
                case 0: _r0 = value; break;
                case 1: _r1 = value; break;
                case 2: _r2 = value; break;
                case 3: _r3 = value; break;
                case 4: _r4 = value; break;
                case 5: _r5 = value; break;
                case 6: _r6 = value; break;
                case 7: _r7 = value; break;
                case 8: _r8 = value; break;
                case 9: _r9 = value; break;
                default: _r10 = value; break;
            }
        }
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref HipsPosition);
        serializer.SerializeValue(ref _r0);
        serializer.SerializeValue(ref _r1);
        serializer.SerializeValue(ref _r2);
        serializer.SerializeValue(ref _r3);
        serializer.SerializeValue(ref _r4);
        serializer.SerializeValue(ref _r5);
        serializer.SerializeValue(ref _r6);
        serializer.SerializeValue(ref _r7);
        serializer.SerializeValue(ref _r8);
        serializer.SerializeValue(ref _r9);
        serializer.SerializeValue(ref _r10);
    }

    // 1mm^2. 미세 물리 드리프트로 매 틱 전송되는 것을 막는 위치 데드밴드
    private const float POSITION_EPSILON_SQ = 1e-6f;

    public bool Equals(RagdollPose other)
    {
        return (HipsPosition - other.HipsPosition).sqrMagnitude < POSITION_EPSILON_SQ &&
               _r0 == other._r0 && _r1 == other._r1 && _r2 == other._r2 &&
               _r3 == other._r3 && _r4 == other._r4 && _r5 == other._r5 &&
               _r6 == other._r6 && _r7 == other._r7 && _r8 == other._r8 &&
               _r9 == other._r9 && _r10 == other._r10;
    }
}
