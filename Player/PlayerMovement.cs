using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class PlayerMovement : NetworkBehaviour
{
    public float moveSpeed = 5f;
    private Vector2 _serverMoveInput;

    public override void OnNetworkSpawn()
    {
        // 처음 스폰될 때 위치 분산 (서버 권한이므로 서버에서만 위치를 지정하면 자동으로 싱크됨)
        if (IsServer)
        {
            transform.position = new Vector3(Random.Range(-3f, 3f), 0.5f, Random.Range(-3f, 3f));
        }
    }

    void Update()
    {
        // 위치 연산은 오직 '서버(Host)'만 수행
        if (!IsServer) return;
        // 서버 컴퓨터 내에서 입력값에 따라 캐릭터를 이동.
        // 서버가 이동시키는 순간, 붙어있는 NetworkTransform 컴포넌트가 알아서 모든 클라이언트에게 브로드캐스트를 진행.
        if (_serverMoveInput != Vector2.zero)
        {
            Vector3 moveDir = new Vector3(_serverMoveInput.x, 0, _serverMoveInput.y).normalized;
            transform.position += moveDir * moveSpeed * Time.deltaTime;
        }
    }

    // 인스펙터에 연결된 입력 함수 (Host/Client 각자 자기 컴퓨터에서 실행됨)
    public void OnMove(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        Vector2 currentInput = context.ReadValue<Vector2>();
        // 내 키보드 입력값을 서버(Host)에게 전달하라고 RPC 요청을 보냅니다.
        MoveServerRpc(currentInput);
    }

    /// <summary>
    /// [ServerRpc]가 붙은 함수는 클라이언트가 호출하지만, 실제 실행은 '서버(Host)'에서 됨.
    /// </summary>
    [ServerRpc]
    private void MoveServerRpc(Vector2 input)
    {
        _serverMoveInput = input;
    }
}
