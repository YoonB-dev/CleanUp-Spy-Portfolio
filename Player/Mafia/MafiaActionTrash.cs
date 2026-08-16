using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(RoleManager))]
public class MafiaActionTrash : NetworkBehaviour
{
    [Header("Mafia Trash Actoin Settings")]
    [SerializeField] private GameObject trashPrefab;
    // 마피아가 생성할 쓰레기 목록 -> 여기에 SO등록
    [SerializeField] private TrashData[] trashDatabase;
    private const float abilityCoolTime = 2f; // 마피아 능력 쿨타임
    private float _currentCoolTime = 0f;
    private float _serverCoolTime = 0f; // 서버용 실제 쿨타임 타이머
    private RoleManager _roleManager;
    private PlayerInteraction _playerInteraction;
    private PlayerActionGate _gate;
    [SerializeField] private Transform _playerCameraTransform;
    private void Awake() {
        _roleManager = GetComponent<RoleManager>();
        _playerInteraction = GetComponent<PlayerInteraction>();
        _gate = PlayerActionGate.GetOrAdd(gameObject);
    }

    private void Update()
    {
        // 서버는 무조건 서버 쿨타임 관리
        if (IsServer && _serverCoolTime > 0)
        {
            _serverCoolTime -= Time.deltaTime;
        }

        // 로컬 플레이어는 로컬 쿨타임 관리 (UI용임)
        if (IsOwner && _currentCoolTime > 0)
        {
            _currentCoolTime -= Time.deltaTime;
        }
    }
    public void OnMafiaAbility(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started) return;
        if (_roleManager.CurrentRole != PlayerRole.Mafia) return;
        if (!_gate.CanDo(PlayerAction.SpawnTrash)) return;

        // 로컬에서 1차적으로 쿨타임 중인지 확인해서 무분별한 RPC 연사를 방지.
        if (_currentCoolTime > 0) return;

        // 서버에게 "쓰레기 소환 요청" RPC를 보냄. (서버에서 진짜로 마피아인지 확인 후 승인)
        RequestSpawnTrashServerRpc();
    }

    [ServerRpc]
    private void RequestSpawnTrashServerRpc(ServerRpcParams rpcParams = default)
    {
        if (_roleManager.CurrentRole != PlayerRole.Mafia || _serverCoolTime > 0 || _playerInteraction == null) return;
        if (trashDatabase == null || trashDatabase.Length == 0)
        {
            Debug.LogError("[MafiaActionTrash] trashDatabase가 설정되지 않았습니다!");
            return;
        }
        // 상호 배타 규칙 서버 재검증(치트 방어)
        if (!_gate.CanDo(PlayerAction.SpawnTrash)) return;

        // --- 쓰레기 생성 흐름 ---
        Vector3 spawnPosition = _playerCameraTransform.position + (_playerCameraTransform.forward * 1.5f) + (Vector3.up * -0.3f);
        GameObject trashMafia = Instantiate(trashPrefab, spawnPosition, Quaternion.identity);
        NetworkObject trashNetObj = trashMafia.GetComponent<NetworkObject>();

        if (trashNetObj != null)
        {
            trashNetObj.Spawn(); // 1. 네트워크 스폰 -> 클라이언트들에게 전파함

            // 2. 스폰 직후 서버 초기화 호출 (NetworkVariable 변경 전파)
            if (trashMafia.TryGetComponent<TrashObject>(out var trashObject))
            {
                TrashData selectedData = trashDatabase[Random.Range(0, trashDatabase.Length)];
                trashObject.ServerInitialize(selectedData);
            }

            NetworkObjectReference netObjRef = new NetworkObjectReference(trashNetObj);
            _playerInteraction.PickupLogicalServer(netObjRef, true);

            if (IsItemSuccessfullyPickedUp(trashNetObj))
            {
                _serverCoolTime = abilityCoolTime;
                StartLocalCooldownClientRpc(abilityCoolTime);
            }
            else
            {
                // 인벤토리가 꽉 찼거나 검증 실패로 못 주웠다면 깔끔하게 스폰 롤백
                trashNetObj.Despawn();
                Destroy(trashMafia);
                Debug.LogWarning($"[Server] {gameObject.name} 인벤토리가 부족하거나 주울 수 없어 생성 롤백.");
            }
        }
    }

    /// <summary>
    /// 방금 생성한 아이템이 플레이어의 인벤토리 3개 슬롯 중 하나에 정상적으로 들어갔는지 체크
    /// </summary>
    private bool IsItemSuccessfullyPickedUp(NetworkObject targetNetObj)
    {
        var inv = _playerInteraction.Inventory;
        if (inv == null) return false;

        bool inSlot1 = inv.Slot1.Value.TryGet(out var o1) && o1 == targetNetObj;
        bool inSlot2 = inv.Slot2.Value.TryGet(out var o2) && o2 == targetNetObj;
        bool inSlot3 = inv.Slot3.Value.TryGet(out var o3) && o3 == targetNetObj;

        return inSlot1 || inSlot2 || inSlot3;
    }

    // 서버가 승인했을 때 클라이언트(Owner)에게 쿨타임 UI를 돌리라고 신호를 줌
    [ClientRpc]
    private void StartLocalCooldownClientRpc(float duration)
    {
        // 오직 이 캐릭터를 조종하는 로컬 플레이어만 UI 타이머를 작동시킴
        if (IsOwner)
        {
            _currentCoolTime = duration;
            Debug.Log($"[로컬] 서버 승인 완료! {duration}초 쿨타임 UI 시작.");
        }
    }
}
