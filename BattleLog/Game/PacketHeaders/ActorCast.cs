using System.Runtime.InteropServices;

namespace BattleLog.Game.PacketHeaders;

[StructLayout(LayoutKind.Explicit, Pack = 1)]
public struct ActorCast
{
    [FieldOffset(0)]
    public ushort actionId;

    [FieldOffset(8)]
    public float castTime;

    [FieldOffset(12)]
    public uint targetId;

    [FieldOffset(16)]
    public float rotation;
}
