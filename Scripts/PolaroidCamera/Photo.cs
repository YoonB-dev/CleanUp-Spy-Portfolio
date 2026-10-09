using Unity.Netcode;
using UnityEngine;

/// <summary>
/// PolaroidCamera가 스폰하는 사진 오브젝트. 촬영된 JPG 바이트를 받아서
/// 자신의 렌더러 머테리얼에 텍스처로 적용한다.
/// Rigidbody + Collider를 붙여서 물리적으로 바닥에 떨어지는 오브젝트로 사용.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Photo : NetworkBehaviour
{
    [Tooltip("사진 이미지가 보일 렌더러 (Quad 등)")]
    [SerializeField] private Renderer photoRenderer;
    [Tooltip("URP Lit/Unlit 기준 기본 텍스처 프로퍼티 이름")]
    [SerializeField] private string texturePropertyName = "_BaseMap";

    private Texture2D _photoTexture;

    /// <summary>
    /// 서버가 스폰 직후 전체 클라이언트에게 브로드캐스트해서 호출.
    /// 각 클라이언트가 자기 로컬에서 텍스처를 디코딩해 적용한다 (텍스처 자체는 딱 한 번만 전송됨).
    /// </summary>
    [ClientRpc]
    public void ApplyPhotoTextureClientRpc(byte[] jpgBytes)
    {
        if (photoRenderer == null)
        {
            Debug.LogWarning("Photo: photoRenderer가 할당되지 않았습니다.", this);
            return;
        }

        if (_photoTexture != null) return; // 이미 로드되었다면 스킵

        _photoTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        _photoTexture.LoadImage(jpgBytes); // 이미지 크기에 맞게 자동으로 리사이즈됨

        // 다른 사진 인스턴스와 텍스처가 공유되지 않도록 머테리얼 인스턴스화 후 적용
        photoRenderer.material.SetTexture(texturePropertyName, _photoTexture);
    }

    public override void OnNetworkDespawn()
    {
        if (_photoTexture != null)
        {
            Destroy(_photoTexture);
        }
    }
}