using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class PlayerMovement : NetworkBehaviour
{
    private CharacterController characterController;
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

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
        {
            return;
        }

        PlayerSpawnManager.Instance?.RegisterPlayer(this);
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer)
        {
            return;
        }

        PlayerSpawnManager.Instance?.UnregisterPlayer(this);
    }

    public void SetServerSpawnPosition(Vector3 spawnPosition)
    {
        if (!IsServer)
        {
            return;
        }

        characterController.enabled = false;
        transform.position = spawnPosition;
        characterController.enabled = true;
        characterController.Move(Vector3.down * 0.01f);
    }

    private void Update()
    {
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
        }
    }
}
