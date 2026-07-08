using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class MafiaDashSkill : NetworkBehaviour
{
    [SerializeField] private Camera playerCamera;
    private float dashSpeed = 10.0f;
    private float dashDuration = 0.5f;
    private float dashCooldown = 1.0f;
    private float thirdPersonOffset = 6.0f;
    [SerializeField] private Animator animator;
    public static bool gIsPlayerDashing { get; private set; } = false;

    private Rigidbody _rb;
    private PlayerMovement _movementScript;
    private CharacterController _cc;
    private FirstPersonLook _fpLook;
    private PlayerInput _playerInput;
    private bool _isCooldown = false;
    private Vector3 _originalCamLocalPos;
    private Quaternion _originalCamLocalRot;
    private RoleManager _playerRole;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _fpLook = GetComponent<FirstPersonLook>();
        _movementScript = GetComponent<PlayerMovement>();
        _cc = GetComponent<CharacterController>();
        _playerInput = GetComponent<PlayerInput>();
        _playerRole = GetComponent<RoleManager>();

        if (playerCamera != null)
        {
            _originalCamLocalPos = playerCamera.transform.localPosition;
            _originalCamLocalRot = playerCamera.transform.localRotation;
        }
    }

    public void OnDashSkill(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started || gIsPlayerDashing || _isCooldown) return;

        // [로컬] 이동 및 카메라 제어권을 즉시 회수 (반응성)
        SetPlayerControl(false);
        SetCameraModeClientRpc(true);

        // [서버] 서버에 돌진 루틴 시작 요청
        RequestDashServerRpc();
        RequestUpdateCameraServerRpc(true);
    }

    [ServerRpc]
    private void RequestDashServerRpc()
    {
        if (gIsPlayerDashing || _isCooldown) return;
        StartCoroutine(DashRoutine());
        PlayDashAnimationClientRpc();
    }

    private IEnumerator DashRoutine()
    {
        //if (_playerRole.CurrentRole != PlayerRole.Mafia) yield break;
        gIsPlayerDashing = true;
        _rb.isKinematic = false;

        // 1. 제어권 회수
        if (_movementScript != null) _movementScript.enabled = false;
        if (_cc != null) _cc.enabled = false;
        if (_playerInput != null) _playerInput.DeactivateInput();
        if (_fpLook != null) _fpLook.enabled = false;

        Vector3 dashDirection = transform.forward;
        dashDirection.y = 0;
        dashDirection.Normalize();

        float elapsedTime = 0f;
        while (elapsedTime < dashDuration)
        {
            _rb.linearVelocity = dashDirection * dashSpeed;

            // 서버가 직접 주변 상자를 찾도록 요청
            if (IsServer)
            {
                CheckAndDemolishBoxes(transform.position);
            }
            else
            {
                // 클라이언트가 돌진 중임을 서버에 알리고 서버가 체크하게 함
                RequestCheckDemolishServerRpc(transform.position);
            }

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // 3. 복구 로직 (가장 중요)
        _rb.linearVelocity = Vector3.zero;
        _rb.isKinematic = true;

        // 4. 위치 동기화 강제 (Teleport)
        GetComponent<NetworkTransform>().Teleport(transform.position, transform.rotation, transform.localScale);

        // 5. 컴포넌트 재활성화 (캐릭터 컨트롤러 튀는 현상 방지)
        if (_cc != null) _cc.enabled = true;
        if (_movementScript != null) _movementScript.enabled = true;
        if (_fpLook != null) _fpLook.enabled = true;
        if (_playerInput != null) _playerInput.ActivateInput();

        gIsPlayerDashing = false;
        FinishedDashClientRpc();
        StartCoroutine(CooldownRoutine());
    }

    private void SetPlayerControl(bool isEnabled)
    {
        Debug.Log($"SetPlayerControl: {isEnabled}");
        if (_movementScript != null) _movementScript.enabled = isEnabled;
        if (_cc != null) _cc.enabled = isEnabled;
        if (_fpLook != null) _fpLook.enabled = isEnabled;
        if (_playerInput != null)
        {
            if (isEnabled) _playerInput.ActivateInput();
            else _playerInput.DeactivateInput();
        }
    }
    [ClientRpc]
    private void FinishedDashClientRpc()
    {
        SetPlayerControl(true);
        SetCameraModeClientRpc(false);
    }


    private IEnumerator CooldownRoutine()
    {
        _isCooldown = true;
        yield return new WaitForSeconds(dashCooldown);
        _isCooldown = false;
    }

    [ServerRpc]
    private void RequestCheckDemolishServerRpc(Vector3 playerPos)
    {
        CheckAndDemolishBoxes(playerPos);
    }
    [ServerRpc]
    private void RequestUpdateCameraServerRpc(bool isThirdPerson)
    {
        // 서버가 모든 클라이언트에게 카메라 업데이트 명령을 보냄
        SetCameraModeClientRpc(isThirdPerson);
    }

    private void CheckAndDemolishBoxes(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(pos, 2.0f);
        foreach (var col in hits)
        {
            if (col.CompareTag("PlacedBox") && col.TryGetComponent<PlaceableBox>(out var box))
            {
                box.RequestDemolish();
            }
        }
    }

    // 대쉬 에니메이션
    [ClientRpc]
    private void PlayDashAnimationClientRpc()
    {
        animator.SetTrigger("Dive");
    }

    // 카메라 모드 전환 (1인칭 <-> 3인칭) // 동기화 이슈로 빼둠.
    [ClientRpc]
    private void SetCameraModeClientRpc(bool isThirdPerson)
    {
        Debug.Log($"SetCameraModeClientRpc: {isThirdPerson}");
        if (playerCamera == null) return;

        if (isThirdPerson)
        {
            // 3인칭 전환
            Quaternion targetRotation = Quaternion.Euler(5f, 0f, 0f);
            playerCamera.transform.localRotation = targetRotation;
            playerCamera.transform.localPosition = _originalCamLocalPos - (Vector3.forward * thirdPersonOffset) + (Vector3.up * 1.5f);
        }
        else
        {
            // 1인칭 복구
            playerCamera.transform.localPosition = _originalCamLocalPos;
            playerCamera.transform.localRotation = _originalCamLocalRot;
        }
    }
}