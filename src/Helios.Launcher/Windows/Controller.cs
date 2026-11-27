using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Helios_Launcher.Windows;

internal enum ControllerAction { Up, Down, Left, Right, Confirm, Back, PreviousTab, NextTab, Search, Favorite, Menu, PreviousFilter, NextFilter, View }

internal sealed class Controller
{
    private const uint LeftTriggerBit = 0x10000;
    private const uint RightTriggerBit = 0x20000;
    private readonly DispatcherQueueTimer _timer;
    private uint? _connected;
    private ulong _nextDiscovery;
    private uint _previous;
    private uint _direction;
    private ulong _nextRepeat;
    private bool _needsNeutral;

    internal event Action<ControllerAction>? ActionReceived;

    internal Controller(DispatcherQueue queue)
    {
        _timer = queue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) => Poll();
    }

    internal void SetActive(bool active)
    {
        if (!active)
        {
            _timer.Stop();
            return;
        }
        _needsNeutral = true;
        _previous = _direction = 0;
        _nextDiscovery = 0;
        _timer.Start();
    }

    private void Poll()
    {
        ulong now = (ulong)Environment.TickCount64;
        if (!ReadState(now, out var input))
            return;

        // D-pad, Menu, View, A, B, X, Y, LB, RB. Triggers are folded in as virtual bits above the XInput range.
        uint buttons = (uint)(input.Gamepad.Buttons & 0xF33F);
        const byte triggerThreshold = 140;
        if (input.Gamepad.LeftTrigger >= triggerThreshold) buttons |= LeftTriggerBit;
        if (input.Gamepad.RightTrigger >= triggerThreshold) buttons |= RightTriggerBit;
        uint direction = buttons & 15;
        if (direction == 0)
        {
            int x = input.Gamepad.ThumbX;
            int y = input.Gamepad.ThumbY;
            const int deadZone = 18000;
            if (Math.Max(Math.Abs(x), Math.Abs(y)) >= deadZone)
                direction = Math.Abs(x) > Math.Abs(y) ? (x < 0 ? 4u : 8u) : (y < 0 ? 2u : 1u);
        }

        if (_needsNeutral)
        {
            _needsNeutral = buttons != 0 || direction != 0;
            return;
        }

        uint pressed = buttons & ~_previous;
        _previous = buttons;
        if (direction != _direction || (direction != 0 && now >= _nextRepeat))
        {
            _nextRepeat = now + (direction == _direction ? 115UL : 330UL);
            _direction = direction;
            if ((direction & 1) != 0) Emit(ControllerAction.Up);
            else if ((direction & 2) != 0) Emit(ControllerAction.Down);
            else if ((direction & 4) != 0) Emit(ControllerAction.Left);
            else if ((direction & 8) != 0) Emit(ControllerAction.Right);
        }
        if ((pressed & 0x1000) != 0) Emit(ControllerAction.Confirm);
        else if ((pressed & 0x2000) != 0) Emit(ControllerAction.Back);
        else if ((pressed & 0x0100) != 0) Emit(ControllerAction.PreviousTab);
        else if ((pressed & 0x0200) != 0) Emit(ControllerAction.NextTab);
        else if ((pressed & 0x4000) != 0) Emit(ControllerAction.Search);
        else if ((pressed & 0x8000) != 0) Emit(ControllerAction.Favorite);
        else if ((pressed & 0x0010) != 0) Emit(ControllerAction.Menu);
        else if ((pressed & 0x0020) != 0) Emit(ControllerAction.View);
        else if ((pressed & LeftTriggerBit) != 0) Emit(ControllerAction.PreviousFilter);
        else if ((pressed & RightTriggerBit) != 0) Emit(ControllerAction.NextFilter);
    }

    private bool ReadState(ulong now, out XInputState state)
    {
        state = default;
        if (_connected is { } port)
        {
            if (XInputGetState(port, out state) == 0)
                return true;
            _connected = null;
            _needsNeutral = true;
            _previous = _direction = 0;
        }
        if (now < _nextDiscovery)
            return false;

        _nextDiscovery = now + 650;
        for (uint slot = 0; slot < 4; slot++)
        {
            if (XInputGetState(slot, out state) != 0)
                continue;
            _connected = slot;
            return true;
        }
        return false;
    }

    private void Emit(ControllerAction action) => ActionReceived?.Invoke(action);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint Packet;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short ThumbX, ThumbY, RightThumbX, RightThumbY;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint slot, out XInputState state);
}
