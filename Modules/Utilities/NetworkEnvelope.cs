#nullable enable

namespace DeathHeadHopperFix.Modules.Utilities
{
    internal static class NetworkProtocol
    {
        internal const string ModId = "DeathHeadHopperFix";
        internal const int ProtocolVersion = 1;
        internal const string RoomKeyPrefix = ModId + ".Room.";

        internal static string BuildRoomKey(string localKey)
        {
            return string.IsNullOrWhiteSpace(localKey)
                ? RoomKeyPrefix
                : RoomKeyPrefix + localKey.Trim();
        }
    }

    internal readonly struct NetworkEnvelope
    {
        internal NetworkEnvelope(string modId, int protocolVersion, string messageType, int messageSeq, object? payload)
        {
            ModId = string.IsNullOrWhiteSpace(modId) ? string.Empty : modId.Trim();
            ProtocolVersion = protocolVersion;
            MessageType = string.IsNullOrWhiteSpace(messageType) ? string.Empty : messageType.Trim();
            MessageSeq = messageSeq;
            Payload = payload;
        }

        internal string ModId { get; }
        internal int ProtocolVersion { get; }
        internal string MessageType { get; }
        internal int MessageSeq { get; }
        internal object? Payload { get; }

        internal object?[] ToEventPayload()
        {
            return new object?[]
            {
                ModId,
                ProtocolVersion,
                MessageType,
                MessageSeq,
                Payload
            };
        }

        internal bool IsExpectedSource()
        {
            return ModId == NetworkProtocol.ModId &&
                   ProtocolVersion == NetworkProtocol.ProtocolVersion &&
                   !string.IsNullOrWhiteSpace(MessageType);
        }

        internal static bool TryParse(object? customData, out NetworkEnvelope envelope)
        {
            envelope = default;
            if (customData is not object[] payload || payload.Length < 5)
            {
                return false;
            }

            if (payload[0] is not string modId ||
                payload[1] is not int protocolVersion ||
                payload[2] is not string messageType ||
                payload[3] is not int messageSeq)
            {
                return false;
            }

            envelope = new NetworkEnvelope(modId, protocolVersion, messageType, messageSeq, payload[4]);
            return true;
        }
    }
}
