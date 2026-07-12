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
    private Vector2 serverMoveInput;
    private float verticalVelocity;

    private const float GRAB_DRAG_SPEED = 20f;             // 붙잡혔을 때 끌려오는 최대 속도
    private const float GRAB_VERTICAL_FOLLOW = 0.5f;       // 붙잡은 사람 높이 따라가는 비율(점프 시 위로 딸려옴)
    private const float GRAB_HOLD_MOVE_MULTIPLIER = 0.6f;  // 붙잡고 있을 때 이동속도 배율
    private PlayerGrab _playerGrab;

    private void Awake()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        _playerGrab = GetComponent<PlayerGrab>();
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
            Vector3 move = new Vector3(serverMoveInput.x, 0f, serverMoveInput.y);
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
            float inputSpeed = serverMoveInput.magnitude;
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

        // 수직: 매 프레임 새로 계산(누적 안 함 → 놓을 때 안 튐).
        float verticalDrag = Mathf.Clamp(toHold.y * GRAB_VERTICAL_FOLLOW / Time.deltaTime, -GRAB_DRAG_SPEED, GRAB_DRAG_SPEED);
        verticalVelocity = 0f;

        velocity = horizontalVelocity + Vector3.up * verticalDrag;
        return true;
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!IsOwner)
        {
            return;
        }

        Vector2 input = context.ReadValue<Vector2>();
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
        serverMoveInput = input;
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
