using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// 보이지 않는 모델에 걷기 클립을 섞어 재생하고, 래그돌 관절이 따라갈 뼈 회전을 제공한다.
/// </summary>
public class RagdollAnimationRig
{
    private const float WALK_BLEND_SPEED = 1f;
    private const int IDLE_INPUT = 0;
    private const int BACKWARD_INDEX = 1;
    private static readonly string[] THIGH_BONE_NAMES = { "Thigh.L", "Thigh.R" };

    private readonly GameObject _rig;
    private readonly PlayableGraph _graph;
    private readonly AnimationMixerPlayable _mixer;
    private readonly AnimationClipPlayable[] _walks;
    private readonly float[] _walkWeights = new float[4];
    private readonly Dictionary<string, Transform> _bones = new();
    private readonly Transform[] _thighs = new Transform[THIGH_BONE_NAMES.Length];
    private readonly Quaternion[] _thighRestRotations = new Quaternion[THIGH_BONE_NAMES.Length];
    private readonly Transform _hips;
    private readonly Transform _leftFoot;
    private readonly Transform _rightFoot;
    private readonly float _restLegReach;
    private float _phase;

    /// <summary>기본 자세 대비 지금 골반에서 발까지의 높이 비율. 무릎을 굽히면 1보다 작아진다</summary>
    public float LegReachRatio => LegReach() / _restLegReach;

    // 래그돌 밖에 따로 둔다. 같은 이름의 뼈가 래그돌 아래에 생기면 이름으로 뼈를 찾는 코드가 헷갈린다
    public RagdollAnimationRig(
        GameObject model, bool visible,
        AnimationClip idle, AnimationClip forward, AnimationClip left, AnimationClip right)
    {
        _rig = Object.Instantiate(model);
        _rig.name = nameof(RagdollAnimationRig);

        foreach (Renderer renderer in _rig.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible;
        }

        foreach (Transform bone in _rig.GetComponentsInChildren<Transform>(true))
        {
            _bones[bone.name] = bone;
        }

        for (int i = 0; i < THIGH_BONE_NAMES.Length; i++)
        {
            _thighs[i] = _bones[THIGH_BONE_NAMES[i]];
            _thighRestRotations[i] = _thighs[i].localRotation;
        }

        _hips = _bones["Hips"];
        _leftFoot = _bones["Foot.L"];
        _rightFoot = _bones["Foot.R"];
        _restLegReach = LegReach();

        Animator animator = _rig.GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        _graph = PlayableGraph.Create(nameof(RagdollAnimationRig));
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _mixer = AnimationMixerPlayable.Create(_graph, 5);
        AnimationPlayableOutput.Create(_graph, "Pose", animator).SetSourcePlayable(_mixer);

        Connect(idle, IDLE_INPUT, 1f);

        // 앞, 뒤, 왼쪽, 오른쪽 순서. 뒤는 앞 클립을 거꾸로 재생한다
        _walks = new[]
        {
            Connect(forward, 1, 0f),
            Connect(forward, 2, 0f),
            Connect(left, 3, 0f),
            Connect(right, 4, 0f)
        };

        _graph.Play();
    }

    // 걸음 위상은 실제 이동 거리로 진행시켜 발이 미끄러지지 않게 하고, 방향별 클립은 같은 위상으로 맞춰 섞는다
    public void Evaluate(Vector3 localVelocity, float strideLength, float strideScale, float deltaTime)
    {
        Vector2 move = new Vector2(localVelocity.x, localVelocity.z);
        float speed = move.magnitude;
        Vector2 direction = speed > 0.001f ? move / speed : Vector2.zero;

        _phase = Mathf.Repeat(_phase + speed / strideLength * deltaTime, 1f);

        _walkWeights[0] = Mathf.Max(0f, direction.y);
        _walkWeights[1] = Mathf.Max(0f, -direction.y);
        _walkWeights[2] = Mathf.Max(0f, -direction.x);
        _walkWeights[3] = Mathf.Max(0f, direction.x);

        float sum = _walkWeights[0] + _walkWeights[1] + _walkWeights[2] + _walkWeights[3];
        float walk = sum > 0f ? Mathf.Clamp01(speed / WALK_BLEND_SPEED) : 0f;

        _mixer.SetInputWeight(IDLE_INPUT, 1f - walk);

        for (int i = 0; i < _walks.Length; i++)
        {
            _mixer.SetInputWeight(i + 1, sum > 0f ? _walkWeights[i] / sum * walk : 0f);

            float phase = i == BACKWARD_INDEX ? 1f - _phase : _phase;
            _walks[i].SetTime(phase * _walks[i].GetAnimationClip().length);
        }

        _graph.Evaluate(deltaTime);

        // 무릎까지 키우면 주저앉은 걸음이 되어 허벅지만 벌린다. 골반 높이 계산에도 반영되게 골격에 직접 적용
        for (int i = 0; i < _thighs.Length; i++)
        {
            _thighs[i].localRotation =
                Quaternion.SlerpUnclamped(_thighRestRotations[i], _thighs[i].localRotation, strideScale);
        }
    }

    public bool TryGetLocalRotation(string boneName, out Quaternion rotation)
    {
        bool found = _bones.TryGetValue(boneName, out Transform bone);
        rotation = found ? bone.localRotation : Quaternion.identity;
        return found;
    }

    public void Destroy()
    {
        _graph.Destroy();
        Object.Destroy(_rig);
    }

    private float LegReach()
    {
        return _hips.position.y - Mathf.Min(_leftFoot.position.y, _rightFoot.position.y);
    }

    private AnimationClipPlayable Connect(AnimationClip clip, int input, float speed)
    {
        AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, clip);
        playable.SetSpeed(speed);
        _graph.Connect(playable, 0, _mixer, input);
        return playable;
    }
}
