namespace WowFishbot.Memory;

internal static class ClientOffsets
{
    internal const uint PreferredImageBase = 0x00400000;
    internal const uint ClientConnectionRva = 0x00879CE0;
    internal const uint CurrentManager = 0x2ED0;
    internal const uint FirstObject = 0xAC;
    internal const uint LocalGuid = 0xC0;
    internal const uint NextObject = 0x3C;
    internal const uint ObjectType = 0x14;
    internal const uint ObjectGuid = 0x30;
    internal const uint Descriptors = 0x08;
    internal const uint BobberRenderModel = 0x1A0;
    internal const uint BobberWorldY = 0xE8;
    internal const uint BobberWorldX = 0xEC;
    internal const uint BobberWorldZ = 0xF0;
    internal const uint CameraManagerRva = 0x0077436C;
    internal const uint MouseoverGuidRva = 0x007D07A0;
    internal const uint BobberAnimationMethodRva = 0x0030C480;
}
