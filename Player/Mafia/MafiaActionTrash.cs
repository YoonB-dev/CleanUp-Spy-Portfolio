using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(RoleManager))]
public class MafiaActionTrash : NetworkBehaviour
{
    private float _currentCoolTime = 0f;
    private RoleManager _roleManager;
    private void Awake() => _roleManager = GetComponent<RoleManager>();

    private void Update()
    {
        if (!IsOwner) return;

        // 로컬 쿨타임은 오직 화면 UI 게이지를 부드럽게 줄이기 위한 용도로만 사용.
        if (_currentCoolTime > 0)
        {
            _currentCoolTime -= Time.deltaTime;
        }
    }
    public void OnMafiaAbility(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started) return;
        if (_roleManager.CurrentRole != PlayerRole.Mafia) return;

        // 로컬에서 1차적으로 쿨타임 중인지 확인해서 무분별한 RPC 연사를 방지.
        if (_currentCoolTime > 0) return;

        // 서버에게 "쓰레기 소환 요청" RPC를 보냄. (서버에서 진짜로 마피아인지 확인 후 승인)
        transform.GetComponent<PlayerInteraction>().RequestSpawnTrashServerRpc();
    }

    // 서버가 승인해 주었을 때 호출되어 진짜로 로컬 타이머를 돌리는 함수
    public void StartLocalCooldown(float duration)
    {
        _currentCoolTime = duration;
        Debug.Log($"[로컬] 서버 승인 완료! {duration}초 쿨타임 UI 시작.");
    }
}
