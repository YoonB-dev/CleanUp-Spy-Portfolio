using System;
using Unity.Collections;
using Unity.Netcode;

public struct RoomPlayerInfo : INetworkSerializable, IEquatable<RoomPlayerInfo>
{
    public ulong ClientId;
    public FixedString32Bytes PlayerName; // string 대신 네트워크 전송에 최적화된 고정 문자열 사용
    public bool IsReady;

    // 네트워크 직렬화 (패킷 전송용)
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref PlayerName);
        serializer.SerializeValue(ref IsReady);
    }

    // NetworkList 내부 비교 및 갱신 판단을 위한 Equatable 구현
    public bool Equals(RoomPlayerInfo other)
    {
        return ClientId == other.ClientId &&
               PlayerName.Equals(other.PlayerName) &&
               IsReady == other.IsReady;
    }
}