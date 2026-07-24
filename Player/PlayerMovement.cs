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
    [SerializeField] private Animator animator;
    private float moveSpeed = 8f;
    private float jumpForce = 2f;
    private float gravity = -9.81f * 2f;
    [SerializeField] private LayerMask groundLayer;
    private Vector2 _serverMoveInput;
    public Vector2 MoveInput => _serverMoveInput;
    private float verticalVelocity;

    private const float GRAB_DRAG_SPEED = 20f;             // 붙잡혔을 때 끌려오는 최대 속도
    private const float GRAB_VERTICAL_FOLLOW = 0.5f;       // 붙잡은 사람 높이 따라가는 비율(점프 시 위로 딸려옴)
    private const float GRAB_HOLD_MOVE_MULTIPLIER = 0.6f;  // 붙잡고 있을 때 이동속도 배율
    private PlayerGrab _playerGrab;
    private GameObject _ownedRagdoll;   // 월드 공간으로 분리된 액티브 래그돌 (Player 소유)

    private void Awake()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        _playerGrab = GetComponent<PlayerGrab>();
    }

    /// <summary>월드 공간 래그돌의 소유권 등록. Player 파괴 시 함께 정리된다.</summary>
    public void RegisterOwnedRagdoll(GameObject ragdoll)
    {
        _ownedRagdoll = ragdoll;
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
        if (animator != null)
        {
            // 입력 벡터의 크기를 계산 (정지: 0, 이동중: 1)
            float inputSpeed = _serverMoveInput.magnitude;
            // Animator의 'Speed' 파라미터에 값을 세팅, 서버 권한형이므로 서버가 이 값을 바꾸면 NetworkAnimator가 전 클라이언트에 동기화
            SetAnimationSpeedClientRpc(inputSpeed);
        }
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

        // 붙잡은 사람이 위에 있을 때만(점프 등) 그 프레임 한정으로 따라 올라간다(누적 안 하므로 놓을 때 안 튐).
        if (toHold.y > 0f)
        {
            float lift = Mathf.Min(toHold.y * GRAB_VERTICAL_FOLLOW / Time.deltaTime, GRAB_DRAG_SPEED);
            verticalOut = Mathf.Max(verticalOut, lift);
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

    public void OnJump(InputAction.CallbackContext context)
    {
        if (!IsOwner)
        {
            return;
        }

        if (context.started)
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
        if (characterController.isGrounded)
        {
            verticalVelocity = Mathf.Sqrt(jumpForce * -2f * gravity);
            TriggerJumpAnimationClientRpc();
        }
    }
    [ClientRpc]
    private void SetAnimationSpeedClientRpc(float speed)
    {
        if (animator != null)
        {
            animator.SetFloat("Speed", speed);
        }
    }
    [ClientRpc]
    private void TriggerJumpAnimationClientRpc()
    {
        if (animator != null)
        {
            animator.SetTrigger("Jump");
        }
    }
}
