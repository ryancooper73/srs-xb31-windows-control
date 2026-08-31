using System.Text;

namespace Xb31.Core.Protocol;

public static class Xb31ResponseParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static LightingMode ParseLightingMode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 8 ||
            payload[0] != 0xF3 ||
            payload[1] != 0x11 ||
            payload[3] != 0xFF ||
            payload[4] != 0x00 ||
            payload[5] != 0x00 ||
            payload[6] != 0x00 ||
            payload[7] != 0x00)
        {
            throw new FormatException("Lighting Mode response envelope is invalid.");
        }

        LightingMode mode = (LightingMode)payload[2];
        if (!Enum.IsDefined(mode))
            throw new FormatException("Lighting Mode response value is invalid.");

        return mode;
    }

    public static SoundMode ParseSoundMode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4 ||
            payload[0] != 0x92 ||
            payload[1] != 0x10 ||
            payload[3] != 0xFF)
        {
            throw new FormatException("Sound Mode response header is invalid.");
        }

        SoundMode mode = (SoundMode)payload[2];
        if (!Enum.IsDefined(mode))
            throw new FormatException("Sound Mode response value is invalid.");

        return mode;
    }

    public static bool ParseAutoStandby(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != 9 ||
            payload[0] != 0xF3 ||
            payload[1] != 0x12 ||
            payload[2] != 0x1F ||
            payload[3] != 0xFF ||
            payload[6] != 0x01 ||
            payload[7] != 0x01)
        {
            throw new FormatException("Auto Standby response envelope is invalid.");
        }

        return payload[8] switch
        {
            0x00 => false,
            0x01 => true,
            _ => throw new FormatException("Auto Standby response value is invalid.")
        };
    }

    public static string ParseBatteryLabel(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8 ||
            payload[0] != 0xF3 ||
            payload[1] != 0x12 ||
            payload[2] != 0x3F ||
            payload[3] != 0xFF)
        {
            throw new FormatException("Battery label response envelope is invalid.");
        }

        int declaredLength = payload[7];
        if (declaredLength > payload.Length - 8)
            throw new FormatException("Battery label response data is truncated.");

        string label;
        try
        {
            label = StrictUtf8.GetString(payload.Slice(8, declaredLength)).TrimEnd('\0');
        }
        catch (DecoderFallbackException exception)
        {
            throw new FormatException("Battery label response is not valid UTF-8.", exception);
        }

        if (label.Length == 0)
            throw new FormatException("Battery label response is empty.");

        return label;
    }
}
