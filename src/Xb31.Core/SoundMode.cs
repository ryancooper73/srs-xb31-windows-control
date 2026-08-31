namespace Xb31.Core;

public enum SoundMode : byte
{
    Standard = 0x00,
    ExtraBass = 0x01,
    LiveSound = 0x02
}

public sealed record SoundOption(string Name, SoundMode Mode);
