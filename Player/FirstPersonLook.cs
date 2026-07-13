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
    [Tooltip("붙잡은 동안 좌우 시점을 제한할 좌우 각도")]
    [Min(0f)]
    [SerializeField] private float grabbedYawRange = 80f;
    public Transform PlayerCameraTransform => playerCamera != null ? playerCamera.transform : cameraPivot;
    private float pitch;
    private float yaw;
    private Vector2 lookInput;
    private bool _yawLimited;
    private float _yawCenter;

    /// <summary>좌우 시점 제한 on/off. 켤 때의 좌우각을 중심으로 ±grabbedYawRange로 제한</summary>
    public void SetLookYawLimited(bool limited)
    {
        if (limited && !_yawLimited)
        {
            _yawCenter = yaw;   // 제한 시작 시점의 좌우각을 중심으로
        }

        _yawLimited = limited;
    }

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
        if (!IsOwner || !enabled) return;
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

        if (_yawLimited)
        {
            yaw = Mathf.Clamp(yaw, _yawCenter - grabbedYawRange, _yawCenter + grabbedYawRange);
        }

        if (cameraPivot != null)
        {
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        SendLookRotationServerRpc(yaw, pitch);
    }

    [ServerRpc]
    private void SendLookRotationServerRpc(float serverYaw, float serverPitch)
    {
        // 서버에서도 해당 플레이어의 정체성과 카메라 각도를 똑같이 맞춰줍니다.
        transform.rotation = Quaternion.Euler(0f, serverYaw, 0f);

        if (cameraPivot != null)
        {
            cameraPivot.localRotation = Quaternion.Euler(serverPitch, 0f, 0f);
        }
    }
}