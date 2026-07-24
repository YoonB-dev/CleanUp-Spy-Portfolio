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
    private float _pitch;
    private float _yaw;
    public float Pitch => _pitch;
    public float Yaw => _yaw;
    private Vector2 _lookInput;
    private bool _yawLimited;
    private float _yawCenter;

    // 클리핑 확대, 축소용 2개
    private const float ORIGIN_CLIP = 0.3f; // 원래 세팅값 백업용
    private const float NEAR_CLIP = 0.01f; // 카메라가 플레이어 몸체에 너무 가까이 붙었을 때, 카메라가 몸체를 뚫고 들어가는 현상을 방지하기 위해 Near Clip을 최소값으로 설정

    /// <summary>좌우 시점 제한 on/off. 켤 때의 좌우각을 중심으로 ±grabbedYawRange로 제한</summary>
    public void SetLookYawLimited(bool limited)
    {
        if (limited && !_yawLimited)
        {
            _yawCenter = _yaw;   // 제한 시작 시점의 좌우각을 중심으로
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

        _yaw = transform.eulerAngles.y;
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (!IsOwner || !enabled) return;
        _lookInput = context.ReadValue<Vector2>();
    }

    private void LateUpdate()
    {
        if (!IsOwner) return;

        float yawDelta = _lookInput.x * mouseSensitivity;
        float pitchDelta = _lookInput.y * mouseSensitivity;

        _yaw += yawDelta;
        _pitch -= pitchDelta;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        if (_yawLimited)
        {
            _yaw = Mathf.Clamp(_yaw, _yawCenter - grabbedYawRange, _yawCenter + grabbedYawRange);
        }

        if (cameraPivot != null)
        {
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        SendLookRotationServerRpc(_yaw, _pitch);
    }

    [ServerRpc]
    private void SendLookRotationServerRpc(float serverYaw, float serverPitch)
    {
        _yaw = serverYaw;
        _pitch = serverPitch;

        // 서버에서도 해당 플레이어의 정체성과 카메라 각도를 똑같이 맞춰줍니다.
        transform.rotation = Quaternion.Euler(0f, serverYaw, 0f);

        if (cameraPivot != null)
        {
            cameraPivot.localRotation = Quaternion.Euler(serverPitch, 0f, 0f);
        }
    }

    /// <summary>지정 레이어를 이 카메라 렌더링에서 제외 (1인칭 자기 몸 가리기용)</summary>
    /// <param name="layer">숨길 레이어 인덱스</param>
    public void ExcludeLayerFromCamera(int layer)
    {
        if (playerCamera == null) return;

        playerCamera.cullingMask &= ~(1 << layer);
    }

    public void SetClipOrigin()
    {
        if (!IsOwner || playerCamera == null) return;

        playerCamera.nearClipPlane = ORIGIN_CLIP;
    }

    public void SetClipNear()
    {
        if (!IsOwner || playerCamera == null) return;

        playerCamera.nearClipPlane = NEAR_CLIP;
    }
}
