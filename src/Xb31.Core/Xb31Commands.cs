namespace Xb31.Core;

public static class Xb31Commands
{
    public static byte[] LightingModeReadFrame() =>
        Xb31FrameBuilder.Build([0xF2, 0x11, 0x1F, 0xFF]);

    public static byte[] PowerOffFrame() =>
        Xb31FrameBuilder.Build([0x30, 0x00, 0x00, 0x0F, 0x00]);

    public static byte[] SoundModeReadFrame() =>
        Xb31FrameBuilder.Build([0x91, 0x10, 0x0F, 0xFF, 0x00]);

    public static byte[] SoundModeFrame(SoundMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        return Xb31FrameBuilder.Build([0x93, 0x10, (byte)mode, 0xFF, 0x00, 0x00]);
    }

    public static byte[] AutoStandbyReadFrame() =>
        Xb31FrameBuilder.Build([0xF2, 0x12, 0x1F, 0xFF]);

    public static byte[] AutoStandbyFrame(bool isOn) =>
        Xb31FrameBuilder.Build([0xF4, 0x12, 0x1F, 0xFF, 0x01, 0x01, isOn ? (byte)0x01 : (byte)0x00]);

    public static byte[] BatteryLabelReadFrame() =>
        Xb31FrameBuilder.Build([0xF2, 0x12, 0x3F, 0xFF]);

    public static byte[] LightingFrame(LightingMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));
        return Xb31FrameBuilder.Build([0xF4, 0x11, (byte)mode, 0xFF, 0x00, 0x00]);
    }
}
