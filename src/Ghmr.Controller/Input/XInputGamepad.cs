using System.Runtime.InteropServices;

namespace Ghmr.Controller.Input;

public readonly record struct GamepadFrame(
    bool Connected,
    int Slot,
    bool NavigateUpPressed,
    bool NavigateDownPressed,
    bool ConfirmPressed,
    bool BackPressed);

public sealed class XInputGamepad
{
    private const uint ErrorSuccess = 0;
    private const ushort DPadUp = 0x0001;
    private const ushort DPadDown = 0x0002;
    private const ushort ButtonA = 0x1000;
    private const ushort ButtonB = 0x2000;
    private const short StickThreshold = 16000;

    private ushort _previousButtons;
    private bool _previousStickUp;
    private bool _previousStickDown;
    private int _activeSlot = -1;
    private bool _useLegacyDll;

    public GamepadFrame Poll()
    {
        for (int slot = 0; slot < 4; slot++)
        {
            if (!TryGetState((uint)slot, out XInputState state))
            {
                continue;
            }

            if (_activeSlot != slot)
            {
                _activeSlot = slot;
                _previousButtons = state.Gamepad.Buttons;
                _previousStickUp = state.Gamepad.ThumbLY > StickThreshold;
                _previousStickDown = state.Gamepad.ThumbLY < -StickThreshold;
                return new GamepadFrame(true, slot, false, false, false, false);
            }

            ushort pressed = (ushort)(state.Gamepad.Buttons & ~_previousButtons);
            bool stickUp = state.Gamepad.ThumbLY > StickThreshold;
            bool stickDown = state.Gamepad.ThumbLY < -StickThreshold;
            bool upPressed = (pressed & DPadUp) != 0 || (stickUp && !_previousStickUp);
            bool downPressed = (pressed & DPadDown) != 0 || (stickDown && !_previousStickDown);

            _previousButtons = state.Gamepad.Buttons;
            _previousStickUp = stickUp;
            _previousStickDown = stickDown;

            return new GamepadFrame(
                true,
                slot,
                upPressed,
                downPressed,
                (pressed & ButtonA) != 0,
                (pressed & ButtonB) != 0);
        }

        _activeSlot = -1;
        _previousButtons = 0;
        _previousStickUp = false;
        _previousStickDown = false;
        return new GamepadFrame(false, -1, false, false, false, false);
    }

    private bool TryGetState(uint slot, out XInputState state)
    {
        try
        {
            uint result = _useLegacyDll
                ? XInputGetStateLegacy(slot, out state)
                : XInputGetState14(slot, out state);
            return result == ErrorSuccess;
        }
        catch (DllNotFoundException)
        {
            _useLegacyDll = true;
            return XInputGetStateLegacy(slot, out state) == ErrorSuccess;
        }
        catch (EntryPointNotFoundException)
        {
            _useLegacyDll = true;
            return XInputGetStateLegacy(slot, out state) == ErrorSuccess;
        }
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState14(uint userIndex, out XInputState state);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetStateLegacy(uint userIndex, out XInputState state);

#pragma warning disable CS0649
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepadState Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepadState
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }
#pragma warning restore CS0649
}
