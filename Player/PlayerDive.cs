using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// 키를 누르면 래그돌로 앞으로 몸을 던진다. 물리로 날아가 벽/장애물에 막힌다. 모든 플레이어 사용.
public class PlayerDive : NetworkBehaviour
{
    [SerializeField] private Transform playerCameraPivot;
    [SerializeField] private float diveSpeed = 9f;
    [SerializeField] private float diveLift = 0.25f;        // 앞으로 나가며 섞는 위쪽 비율
    [SerializeField] private float diveForwardSpin = 200f;  // 몸을 앞으로 눕히는 회전(도/초)
    [SerializeField] private float cooldown = 1f;           // 기상 후 추가 대기
    [SerializeField] private float thirdPersonOffset = 2.5f;
    [SerializeField] private float boxSweepRadius = 2f;

    private PlayerKnockdown _knockdown;
    private FirstPersonLook _fpLook;
    private bool _canDive = true;   // [서버]
    private Vector3 _originalCamLocalPos;
    private Quaternion _originalCamLocalRot;

    private void Awake()
    {
        _knockdown = GetComponent<PlayerKnockdown>();
        _fpLook = GetComponent<FirstPersonLook>();

        if (playerCameraPivot != null)
        {
            _originalCamLocalPos = playerCameraPivot.localPosition;
            _originalCamLocalRot = playerCameraPivot.localRotation;
        }
    }

    public void OnDive(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.started)
        {
            return;
        }

        RequestDiveServerRpc();
    }

    [ServerRpc]
    private void RequestDiveServerRpc()
    {
        if (!_canDive || _knockdown == null || _knockdown.IsDown)
        {
            return;
        }

        _canDive = false;
        Vector3 dir = (transform.forward + Vector3.up * diveLift).normalized;
        Vector3 spin = transform.right * (diveForwardSpin * Mathf.Deg2Rad);
        _knockdown.ServerDive(dir * diveSpeed, spin);
        StartCoroutine(DiveRoutine());
    }

    private IEnumerator DiveRoutine()
    {
        SetCameraModeClientRpc(true);

        // 날아가는 동안 경로의 상자를 부순다
        while (_knockdown.IsDown)
        {
            DemolishBoxesNear(transform.position);
            yield return null;
        }

        // 기상 후 쿨다운
        SetCameraModeClientRpc(false);
        yield return new WaitForSeconds(cooldown);
        _canDive = true;
    }

    private void DemolishBoxesNear(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(pos, boxSweepRadius);
        foreach (Collider col in hits)
        {
            if (col.CompareTag("PlacedBox") && col.TryGetComponent(out PlaceableBox box))
            {
                box.RequestDemolish();
            }
        }
    }

    // 다이빙 동안 3인칭으로 빼서 내 몸이 날아가는 걸 보이게 한다
    [ClientRpc]
    private void SetCameraModeClientRpc(bool thirdPerson)
    {
        if (playerCameraPivot == null)
        {
            return;
        }

        if (thirdPerson)
        {
            playerCameraPivot.localRotation = Quaternion.Euler(15f, 0f, 0f);
            playerCameraPivot.localPosition =
                _originalCamLocalPos - Vector3.forward * thirdPersonOffset + Vector3.up * 1.5f;
        }
        else
        {
            playerCameraPivot.localPosition = _originalCamLocalPos;
            playerCameraPivot.localRotation = _originalCamLocalRot;
        }

        // 3인칭에선 내 몸을 보여주고, 1인칭 복귀 시 다시 숨긴다
        if (IsOwner && _fpLook != null)
        {
            if (thirdPerson)
            {
                _fpLook.IncludeLayerInCamera(RagdollDriver.LOCAL_HIDDEN_LAYER);
            }
            else
            {
                _fpLook.ExcludeLayerFromCamera(RagdollDriver.LOCAL_HIDDEN_LAYER);
            }
        }
    }
}
