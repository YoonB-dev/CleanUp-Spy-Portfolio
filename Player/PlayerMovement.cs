using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using Unity.Netcode.Components;
/// <summary>
/// Player 움직임 관련 스크립트
/// </summary>
public class PlayerMovement : NetworkBehaviour
{
    private CharacterController characterController;
    private float moveSpeed = 8f;
    private float jumpForce = 2f;
    private float gravity = -9.81f * 2f;
    [SerializeField] private LayerMask groundLayer;
    private Vector2 _serverMoveInput;
    public Vector2 MoveInput => _serverMoveInput;

    /// <summary>
    /// 발을 디딜 수 있는 레이어(Ground/Wall/Item). 래그돌 기상 시 지면 높이 재판정에 사용
    /// </summary>
    public LayerMask GroundLayer => groundLayer;

    /// <summary>
    /// 서 있을 때 루트 원점이 캡슐 바닥보다 위에 있는 높이(월드 기준). <br/>
    /// </summary>
    public float StandingGroundOffset =>
        (characterController.height * 0.5f - characterController.center.y)
        * transform.lossyScale.y;
    private float verticalVelocity;

    private const float GRAB_DRAG_SPEED = 20f;             // 붙잡혔을 때 끌려오는 최대 속도
    private const float GRAB_VERTICAL_FOLLOW = 0.5f;       // 붙잡은 사람 높이 따라가는 비율(점프 시 위로 딸려옴)
    private const float GRAB_HOLD_MOVE_MULTIPLIER = 0.6f;  // 붙잡고 있을 때 이동속도 배율
    private PlayerGrab _playerGrab;
    private PlayerActionGate _gate;
    private GameObject _ownedRagdoll;   // 월드 공간으로 분리된 액티브 래그돌 (Player 소유)

    private void Awake()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        _playerGrab = GetComponent<PlayerGrab>();
        _gate = PlayerActionGate.GetOrAdd(gameObject);
    }

    /// <summary>월드 공간 래그돌의 소유권 등록. Player 파괴 시 함께 정리된다.</summary>
    public void RegisterOwnedRagdoll(GameObject ragdoll)
    {
        _ownedRagdoll = ragdoll;
    }

    /// <summary>이 플레이어 몸을 이루는 모든 콜라이더(캡슐 + 분리된 래그돌). 붙잡기 충돌 무시용.</summary>
    /// <param name="result">결과를 채울 리스트(먼저 비워짐)</param>
    public void CollectBodyColliders(List<Collider> result)
    {
        result.Clear();

        if (characterController != null)
        {
            result.Add(characterController);
        }

        if (_ownedRagdoll != null)
        {
            result.AddRange(_ownedRagdoll.GetComponentsInChildren<Collider>(true));
        }
    }

    private void OnDestroy()
    {
        if (_ownedRagdoll != null)
        {
            Destroy(_ownedRagdoll);
        }
    }

    private void Update()
    {
        // 마피아 돌진중이면 스킵

        if (!IsServer)
        {
            return;
        }

        bool grounded = characterController.isGrounded;

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        Vector3 velocity;
        if (TryGetGrabbedVelocity(out Vector3 grabbedVelocity))
        {
            velocity = grabbedVelocity;   // 붙잡힘: 드래그로 끌려감(입력 무시)
        }
        else
        {
            Vector3 move = new Vector3(_serverMoveInput.x, 0f, _serverMoveInput.y);
            move = transform.right * move.x + transform.forward * move.z;
            move *= moveSpeed;

            // 붙잡고 있으면 이동 둔화
            if (_playerGrab != null && _playerGrab.IsGrabbing)
            {
                move *= GRAB_HOLD_MOVE_MULTIPLIER;
            }

            verticalVelocity += gravity * Time.deltaTime;
            velocity = move + Vector3.up * verticalVelocity;
        }

        characterController.Move(velocity * Time.deltaTime);
    }

    /// <summary>붙잡혔으면 홀드 지점으로 끌려가는 속도(수평 드래그 + 수직 따라오기)를 낸다.</summary>
    private bool TryGetGrabbedVelocity(out Vector3 velocity)
    {
        velocity = Vector3.zero;

        if (_playerGrab == null || !_playerGrab.IsGrabbed)
        {
            return false;
        }

        if (!_playerGrab.TryGetHoldPoint(out Vector3 holdPoint))
        {
            return false;
        }

        Vector3 toHold = holdPoint - transform.position;

        // 수평: 목표로 이동(최대속도 제한).
        Vector3 horizontal = new Vector3(toHold.x, 0f, toHold.z);
        Vector3 horizontalVelocity = Vector3.ClampMagnitude(horizontal / Time.deltaTime, GRAB_DRAG_SPEED);

        // 수직: 중력은 항상 작용(서로 붙잡아도 무한 상승하지 않게). verticalVelocity에 누적되므로 놓을 때도 자연스럽게 낙하.
        verticalVelocity += gravity * Time.deltaTime;
        float verticalOut = verticalVelocity;

        // 붙잡은 사람이 위에 있으면(점프, 공중에서 붙잡힘 등) 그쪽으로 딸려 올라간다.
        if (toHold.y > 0f)
        {
            float lift = Mathf.Min(toHold.y * GRAB_VERTICAL_FOLLOW / Time.deltaTime, GRAB_DRAG_SPEED);
            if (lift > verticalOut)
            {
                // 매달려 있는 동안엔 낙하 속도를 쌓지 않는다. 쌓으면 1초쯤 뒤부터 리프트 상한
                // (GRAB_DRAG_SPEED)을 넘겨 캡슐이 래그돌을 두고 떨어지고, 놓는 순간 그 속도로 땅에 박힌다.
                verticalVelocity = 0f;
                verticalOut = lift;
            }
        }

        velocity = horizontalVelocity + Vector3.up * verticalOut;
        return true;
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!IsOwner)
        {
            return;
        }

        Vector2 input = context.ReadValue<Vector2>();
        _serverMoveInput = input;
        SubmitMoveServerRpc(input);
    }

    // 입력이 끊길 때 호출. 안 지우면 마지막 입력으로 계속 걸어간다
    public void ClearMoveInput()
    {
        if (!IsOwner)
        {
            return;
        }

        _serverMoveInput = Vector2.zero;
        SubmitMoveServerRpc(Vector2.zero);
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (!IsOwner)
        {
            return;
        }

        if (context.started && _gate.CanDo(PlayerAction.Jump))
        {
            JumpServerRpc();
        }
    }

    [ServerRpc]
    private void SubmitMoveServerRpc(Vector2 input)
    {
        _serverMoveInput = input;
    }

    [ServerRpc]
    private void JumpServerRpc()
    {
        if (characterController.isGrounded && _gate.CanDo(PlayerAction.Jump))
        {
            verticalVelocity = Mathf.Sqrt(jumpForce * -2f * gravity);
        }
    }
}
