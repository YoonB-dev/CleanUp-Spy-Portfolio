using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// 펀치 입력(H키)의 좌우 교대, 타이밍, 타격 판정을 관리 (서버 권위). <br/>
/// 실제 팔 자세는 서버 물리에서 RagdollPoser가 이 상태를 읽어 적용한다.
/// </summary>
public class PlayerPunch : NetworkBehaviour
{
    private const float WINDUP_DURATION = 0.32f;    // 팔을 크게 젖혀 감는 시간
    private const float STRIKE_DURATION = 0.16f;    // 휘두르는 시간. 무거운 팔이라 스냅되지 않는다
    private const float HOLD_DURATION = 0.06f;      // 다 휘두른 자세 유지(따라나감)
    private const float RECOVER_DURATION = 0.45f;   // 팔이 흐물흐물 돌아오는 시간
    private const float PUNCH_INTERVAL = 0.75f;     // 다음 펀치까지 최소 간격(홀드 연타 주기)

    private const float WINDUP_END = WINDUP_DURATION;
    private const float STRIKE_END = WINDUP_END + STRIKE_DURATION;
    private const float HOLD_END = STRIKE_END + HOLD_DURATION;
    private const float PUNCH_DURATION = HOLD_END + RECOVER_DURATION;

    [Header("타격")]
    [Tooltip("주먹 판정 반경. 팔이 늦게 따라오므로 넉넉하게 잡는다")]
    [SerializeField]
    private float _hitRadius = 0.35f;

    [Tooltip("맞은 플레이어가 날아가는 속도 (m/s)")]
    [SerializeField]
    private float _knockbackSpeed = 14f;

    [Tooltip("맞은 물체를 밀어내는 충격량. 무거운 물체일수록 덜 날아간다")]
    [SerializeField]
    private float _objectImpulse = 26f;

    [Tooltip("날아가는 방향에 섞는 위쪽 비율. 클수록 붕 뜬다")]
    [SerializeField]
    [Range(0f, 1.5f)]
    private float _launchUpward = 0.85f;

    [Tooltip("맞은 플레이어가 날아가며 도는 회전 속도(도/초). 스윙 손 방향으로 돈다")]
    [SerializeField]
    private float _knockbackSpin = 360f;

    [Header("사운드")]
    [Tooltip("팔을 휘두를 때 바람 소리. 감는 동작이 끝나고 주먹이 나가는 순간 재생 (헛방이어도 재생)")]
    [SerializeField]
    private SoundData _swingSound;

    [Tooltip("플레이어를 때렸을 때 소리")]
    [SerializeField]
    private SoundData _playerHitSound;

    [Tooltip("물체를 때렸을 때 기본 소리. 물체에 ImpactSound나 ItemData.hitSound가 있으면 그게 우선 (비워두면 설정 안 된 물체는 무음)")]
    [SerializeField]
    [FormerlySerializedAs("_objectHitSound")]
    private SoundData _defaultObjectHitSound;

    // 넉백 옆방향 각도
    private const float LAUNCH_SIDE_ANGLE = 70f;

    private const int HIT_BUFFER_SIZE = 16;

    private float _punchStartTime = float.NegativeInfinity;   // [서버] 현재 펀치 시작 시각
    private bool _isLeftHand;                                 // [서버] 이번에 휘두르는 손
    private int _punchId;                                     // [서버] 펀치마다 증가. 같은 대상은 한 펀치에 한 번만
    private int _hitPunchId = -1;                             // [서버] 이미 타격이 들어간 펀치
    private bool _punchHeld;                                  // [Owner] 키 홀드 상태
    private float _nextPunchRequestTime;                      // [Owner] 다음 연타 요청 시각

    private RagdollDriver _driver;
    private PlayerActionGate _gate;
    private readonly Collider[] _hitBuffer = new Collider[HIT_BUFFER_SIZE];
    // 물체 중복 타격 방지 (한 물체의 콜라이더 여러 개가 동시에 걸린다)
    private readonly HashSet<Component> _hitTargets = new HashSet<Component>();

    // 주먹이 나가는 구간. 이때만 타격 판정을 돌린다
    private bool IsStriking
    {
        get
        {
            float elapsed = Time.time - _punchStartTime;
            return elapsed >= WINDUP_END && elapsed <= HOLD_END;
        }
    }

    /// <summary>
    /// RagdollDriver가 Player에서 분리되기 전에 자신을 넘긴다. 주먹 위치 조회용.
    /// </summary>
    /// <param name="driver">이 플레이어의 래그돌 드라이버</param>
    public void BindRagdoll(RagdollDriver driver)
    {
        _driver = driver;
    }

    private void Awake()
    {
        _gate = PlayerActionGate.GetOrAdd(gameObject);
    }

    /// <summary>
    /// 현재 펀치 자세 상태. 펀치 중이 아니면 false. [서버 전용]
    /// </summary>
    /// <param name="isLeftHand">휘두르는 손이 왼손인지</param>
    /// <param name="weight">펀치 자세 가중치 (0=평상 자세, 1=펀치 자세)</param>
    /// <param name="extension">팔 뻗음 정도 (0=당김, 1=최대로 뻗음)</param>
    public bool TryGetPunchPose(out bool isLeftHand, out float weight, out float extension)
    {
        isLeftHand = _isLeftHand;
        weight = 0f;
        extension = 0f;

        float elapsed = Time.time - _punchStartTime;
        if (elapsed < 0f || elapsed > PUNCH_DURATION)
        {
            return false;
        }

        if (elapsed < WINDUP_END)
        {
            // 당기면서 펀치 자세로 진입
            weight = elapsed / WINDUP_DURATION;
        }
        else if (elapsed < HOLD_END)
        {
            weight = 1f;
            extension = Mathf.Clamp01((elapsed - WINDUP_END) / STRIKE_DURATION);
        }
        else
        {
            // 뻗은 채로 가중치만 낮춰 평상 자세로 복귀
            weight = 1f - (elapsed - HOLD_END) / RECOVER_DURATION;
            extension = 1f;
        }

        return true;
    }

    /// <summary>펀치 입력(H키). 누르는 동안 양손을 번갈아 반복한다.</summary>
    public void OnPunch(InputAction.CallbackContext context)
    {
        if (!IsOwner)
        {
            return;
        }

        if (context.started)
        {
            _punchHeld = true;
            _nextPunchRequestTime = 0f;   // 누른 즉시 첫 펀치
        }
        else if (context.canceled)
        {
            _punchHeld = false;
        }
    }

    private void Update()
    {
        if (!_punchHeld || !IsOwner || Time.time < _nextPunchRequestTime)
        {
            return;
        }

        // 홀드 중에도 매번 검사해 조건이 풀리는 순간 다시 나가게 한다
        if (!_gate.CanDo(PlayerAction.Punch))
        {
            return;
        }

        _nextPunchRequestTime = Time.time + PUNCH_INTERVAL;

        RequestPunchServerRpc();
    }

    [ServerRpc]
    private void RequestPunchServerRpc()
    {
        ServerStartPunch();
    }

    /// <summary>반대 손으로 새 펀치를 시작. 연타 주기는 서버에서도 검증. [서버 전용]</summary>
    private void ServerStartPunch()
    {
        if (Time.time - _punchStartTime < PUNCH_INTERVAL)
        {
            return;
        }

        // 쓰러짐/손 점유 등 상호 배타 규칙 서버 재검증(치트 방어)
        if (!_gate.CanDo(PlayerAction.Punch))
        {
            return;
        }

        _isLeftHand = !_isLeftHand;
        _punchStartTime = Time.time;
        _punchId++;

        PlaySwingSoundClientRpc();
    }

    /// <summary>
    /// 펀치 시작을 알리면 각 클라이언트가 감는 시간만큼 기다렸다가 휘두르는 소리를 낸다.
    /// (팔 자세도 서버의 펀치 시작 시각 기준이라 소리와 동작 타이밍이 맞는다)
    /// </summary>
    [ClientRpc]
    private void PlaySwingSoundClientRpc()
    {
        if (_swingSound == null)
        {
            return;
        }

        StartCoroutine(PlaySwingSoundAfterWindup());
    }

    private IEnumerator PlaySwingSoundAfterWindup()
    {
        yield return new WaitForSeconds(WINDUP_DURATION);

        // 기다리는 동안 이동했을 수 있으므로 재생 시점의 위치 사용
        SoundManager.Instance?.PlaySFXAt(_swingSound, transform.position);
    }

    /// <summary>
    /// 주먹이 나가는 동안 매 물리 스텝 타격을 판정한다. 플레이어는 조준한 한 명
    /// (주먹에 가장 가까운)만 다운시키고, 물체는 범위에 든 만큼 모두 밀어낸다. [서버 전용]
    /// </summary>
    private void FixedUpdate()
    {
        if (!IsServer || !IsStriking || _punchId == _hitPunchId)
        {
            return;
        }

        if (_driver == null || !_driver.TryGetFistPoint(_isLeftHand, out Vector3 fist))
        {
            return;
        }

        int count = Physics.OverlapSphereNonAlloc(
            fist, _hitRadius, _hitBuffer, ~0, QueryTriggerInteraction.Ignore);

        _hitTargets.Clear();
        bool didHit = false;

        // 플레이어는 조준한 한 명(주먹에 가장 가까운)만 다운, 물체는 걸린 대로 모두 밀어낸다
        PlayerKnockdown target = null;
        float targetSqr = float.PositiveInfinity;

        // 소리는 주먹에 가장 가까운 물체 하나만 (여러 개가 동시에 울리면 시끄러움)
        Rigidbody closestObject = null;
        float closestObjectSqr = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Collider hit = _hitBuffer[i];
            Transform victim = ResolveVictimRoot(hit);

            if (victim != null)
            {
                // 자기 몸(캡슐, 자기 래그돌 뼈)은 제외
                if (victim == transform || !victim.TryGetComponent(out PlayerKnockdown knockdown))
                {
                    continue;
                }

                // 걸린 뼈가 주먹에 가장 가까운 플레이어를 조준 대상으로
                float sqr = hit.bounds.SqrDistance(fist);
                if (sqr < targetSqr)
                {
                    targetSqr = sqr;
                    target = knockdown;
                }
                continue;
            }

            if (!TryPushObject(hit))
            {
                continue;
            }
            didHit = true;

            float objectSqr = hit.bounds.SqrDistance(fist);
            if (objectSqr < closestObjectSqr)
            {
                closestObjectSqr = objectSqr;
                closestObject = hit.attachedRigidbody;
            }
        }

        if (target != null)
        {
            Vector3 spin = Vector3.up * (SwingSign * _knockbackSpin * Mathf.Deg2Rad);
            target.ServerKnockdown(SwingLaunchDirection() * _knockbackSpeed, spin);
            didHit = true;
        }

        if (didHit)
        {
            _hitPunchId = _punchId;
            ServerNotifyHitSound(fist, target != null, closestObject);
        }
    }

    /// <summary>
    /// 타격 판정은 서버에서만 하므로 모든 클라이언트에 소리를 알린다 (한 펀치에 한 번). [서버 전용] <br/>
    /// 플레이어를 맞혔으면 플레이어 타격음, 아니면 맞은 물체의 소리(물체 쪽 설정)를 재생한다.
    /// </summary>
    private void ServerNotifyHitSound(Vector3 fist, bool hitPlayer, Rigidbody closestObject)
    {
        if (hitPlayer)
        {
            PlayPlayerHitSoundClientRpc(fist);
            return;
        }

        // 물체 소리는 클라이언트가 그 물체에서 직접 찾도록 참조만 보낸다 (NetworkObject가 없으면 기본 소리)
        NetworkObject hitNetworkObject = closestObject != null ? closestObject.GetComponentInParent<NetworkObject>() : null;
        bool hasObject = hitNetworkObject != null && hitNetworkObject.IsSpawned;
        PlayObjectHitSoundClientRpc(fist, hasObject ? hitNetworkObject : default(NetworkObjectReference), hasObject);
    }

    [ClientRpc]
    private void PlayPlayerHitSoundClientRpc(Vector3 position)
    {
        SoundManager.Instance?.PlaySFXAt(_playerHitSound, position);
    }

    [ClientRpc]
    private void PlayObjectHitSoundClientRpc(Vector3 position, NetworkObjectReference hitObject, bool hasObject)
    {
        GameObject target = hasObject && hitObject.TryGet(out NetworkObject networkObject) ? networkObject.gameObject : null;
        SoundManager.Instance?.PlaySFXAt(ImpactSound.Resolve(target, _defaultObjectHitSound), position);
    }

    // 물체 하나를 밀어낸다. 이미 민 물체(콜라이더 여러 개)는 건너뛴다
    private bool TryPushObject(Collider hit)
    {
        Rigidbody body = hit.attachedRigidbody;
        if (body == null || body.isKinematic || !_hitTargets.Add(body))
        {
            return false;
        }

        body.AddForce(
            GetLaunchDirection(body.position) * _objectImpulse, ForceMode.Impulse);
        return true;
    }

    /// <summary>
    /// 맞은 콜라이더가 속한 Player를 찾는다. 래그돌은 Player에서 분리돼 있어
    /// 계층으로는 못 찾으므로 RagdollDriver가 주인을 알려준다.
    /// </summary>
    /// <param name="hit">판정에 걸린 콜라이더</param>
    /// <returns>Player 루트. 플레이어가 아니면 null</returns>
    private static Transform ResolveVictimRoot(Collider hit)
    {
        RagdollDriver driver = hit.GetComponentInParent<RagdollDriver>();
        if (driver != null)
        {
            return driver.PlayerRoot;
        }

        PlayerKnockdown knockdown = hit.GetComponentInParent<PlayerKnockdown>();
        return knockdown != null ? knockdown.transform : null;
    }

    // 스윙 방향 부호. 왼손은 오른쪽, 오른손은 왼쪽
    private float SwingSign => _isLeftHand ? 1f : -1f;

    // 플레이어 넉백 방향. 정면을 스윙 쪽으로 각도만큼 튼 뒤 위를 섞는다
    private Vector3 SwingLaunchDirection()
    {
        Vector3 horizontal = Quaternion.AngleAxis(SwingSign * LAUNCH_SIDE_ANGLE, Vector3.up)
                             * transform.forward;
        return (horizontal + Vector3.up * _launchUpward).normalized;
    }

    // 물체를 밀어낼 방향. 나에게서 멀어지는 수평 + 위
    private Vector3 GetLaunchDirection(Vector3 targetPosition)
    {
        Vector3 flat = targetPosition - transform.position;
        flat.y = 0f;

        Vector3 forward = flat.sqrMagnitude > 1e-4f
            ? flat.normalized
            : transform.forward;

        return (forward + Vector3.up * _launchUpward).normalized;
    }
}
