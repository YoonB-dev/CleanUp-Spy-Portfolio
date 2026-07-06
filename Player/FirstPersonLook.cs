using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

public class FirstPersonLook : NetworkBehaviour
{
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private AudioListener audioListener;
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;

    private float pitch;
    private float yaw;
    private Vector2 lookInput;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            if (playerCamera != null) playerCamera.enabled = false;
            if (audioListener != null) audioListener.enabled = false;
            enabled = false;
            audioListener.enabled = false;
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (playerCamera != null) playerCamera.enabled = true;
        if (audioListener != null) audioListener.enabled = true;

        yaw = transform.eulerAngles.y;
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        lookInput = context.ReadValue<Vector2>();
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;

        float yawDelta = lookInput.x * mouseSensitivity;
        float pitchDelta = lookInput.y * mouseSensitivity;

        yaw += yawDelta;
        pitch -= pitchDelta;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        if (cameraPivot != null)
        {
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        SendYawServerRpc(yaw);
    }

    [ServerRpc]
    private void SendYawServerRpc(float serverYaw)
    {
        transform.rotation = Quaternion.Euler(0f, serverYaw, 0f);
    }
}