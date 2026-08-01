using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 피격당한 플레이어를 래그돌 상태로 날려보내고 잠시 뒤 일으켜 세운다 (서버 권위). <br/>
/// 이동은 서버에서만 계산되므로 SetLimp로 충분하지만, 시점은 Owner가 돌리므로 상태를 동기화한다.
/// </summary>
public class PlayerKnockdown : NetworkBehaviour
{
    private const float DOWN_DURATION = 1.8f;   // 쓰러진 뒤 일어나기까지

    // 쓰러짐 상태. 서버가 기록하고 Owner가 시점 조작을 막는 데 쓴다
    private readonly NetworkVariable<bool> _isDownNet = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private RagdollDriver _driver;
    private FirstPersonLook _firstPersonLook;
    private PlayerInteraction _playerInteraction;
    private float _getUpTime;   // [서버] 일어날 시각

    /// <summary>쓰러져 있는지</summary>
    public bool IsDown => _isDownNet.Value;

    /// <summary>
    /// RagdollDriver가 Player에서 분리(SetParent)되기 전에 자신을 넘긴다.
    /// </summary>
    /// <param name="driver">이 플레이어의 래그돌 드라이버</param>
    public void BindRagdoll(RagdollDriver driver)
    {
        _driver = driver;
    }

    private void Awake()
    {
        _firstPersonLook = GetComponent<FirstPersonLook>();
        _playerInteraction = GetComponent<PlayerInteraction>();
    }

    public override void OnNetworkSpawn()
    {
        _isDownNet.OnValueChanged += OnDownChanged;
        ApplyOwnerLook(_isDownNet.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isDownNet.OnValueChanged -= OnDownChanged;
    }

    /// <summary>
    /// 피격. 래그돌을 흐물흐물하게 풀고 통째로 날려보낸다. [서버 전용] <br/>
    /// 쓰러진 상태에서 또 맞으면 기상 시각이 밀리고 속도가 추가된다.
    /// </summary>
    /// <param name="velocity">날아갈 속도 변화량 (m/s)</param>
    /// <param name="angularVelocity">날아가며 도는 각속도 (rad/s, 월드)</param>
    public void ServerKnockdown(Vector3 velocity, Vector3 angularVelocity)
    {
        if (!IsServer || _driver == null)
        {
            return;
        }

        EnterDown(dropItem: true, diving: false);
        _driver.ApplyKnockbackVelocity(velocity);
        _driver.ApplyKnockbackSpin(angularVelocity);
    }

    /// <summary>
    /// 스스로 앞으로 몸을 던지는 다이빙. 넉다운과 같은 흐름이되 아이템은 유지. [서버 전용]
    /// </summary>
    /// <param name="velocity">날아갈 속도 변화량 (m/s)</param>
    /// <param name="angularVelocity">몸을 앞으로 눕히는 각속도 (rad/s, 월드)</param>
    public void ServerDive(Vector3 velocity, Vector3 angularVelocity)
    {
        if (!IsServer || _driver == null)
        {
            return;
        }

        EnterDown(dropItem: false, diving: true);
        _driver.SetBonesVelocity(velocity);
        _driver.ApplyKnockbackSpin(angularVelocity);
    }

    // 다운 상태 진입. 이미 다운이면 기상 시각만 미룬다.
    private void EnterDown(bool dropItem, bool diving)
    {
        _getUpTime = Time.time + DOWN_DURATION;

        if (_isDownNet.Value)
        {
            return;
        }

        _isDownNet.Value = true;
        _driver.SetLimp(true, diving);

        if (dropItem && _playerInteraction != null)
        {
            _playerInteraction.ServerDropHeldItem();
        }
    }

    private void Update()
    {
        if (!IsServer || !_isDownNet.Value || Time.time < _getUpTime)
        {
            return;
        }

        // 앵커가 다시 몸을 세우면서 일어난다
        _isDownNet.Value = false;
        _driver.SetLimp(false);
    }

    private void OnDownChanged(bool previous, bool current)
    {
        ApplyOwnerLook(current);
    }

    // 쓰러진 동안 Owner의 시점 조작을 막는다. 카메라는 몸을 따라 같이 굴러간다
    private void ApplyOwnerLook(bool isDown)
    {
        if (!IsOwner || _firstPersonLook == null)
        {
            return;
        }

        _firstPersonLook.enabled = !isDown;
    }
}
