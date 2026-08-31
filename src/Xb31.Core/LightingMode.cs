namespace Xb31.Core;

public enum LightingMode : byte
{
    LightOff = 0x10,
    Rave = 0x11,
    Chill = 0x12,
    RandomFlashOff = 0x13,
    Hot = 0x14,
    Cool = 0x15,
    Strobe = 0x16,
    CalmMagenta = 0x17,
    CalmCyan = 0x18,
    CalmLime = 0x19,
    CalmCinnabar = 0x1A,
    CalmDaylight = 0x1B,
    CalmLightBulb = 0x1C
}

public sealed record LightingOption(string Name, LightingMode Mode);
