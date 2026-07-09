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

    private void Awake()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
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

        Vector3 move = new Vector3(serverMoveInput.x, 0f, serverMoveInput.y);
        move = transform.right * move.x + transform.forward * move.z;
        move *= moveSpeed;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = move + Vector3.up * verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);
        if (animator != null)
        {
            // 입력 벡터의 크기를 계산 (정지: 0, 이동중: 1)
            float inputSpeed = serverMoveInput.magnitude;
            // Animator의 'Speed' 파라미터에 값을 세팅, 서버 권한형이므로 서버가 이 값을 바꾸면 NetworkAnimator가 전 클라이언트에 동기화
            SetAnimationSpeedClientRpc(inputSpeed);
        }
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
