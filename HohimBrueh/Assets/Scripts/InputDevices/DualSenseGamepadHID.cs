#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace FrogSmashers.InputDevices
{
    // Not "FrogSmashers.Input": that name hides UnityEngine.Input for all code in the
    // FrogSmashers namespaces.
    /// <summary>
    /// The USB input report of a PS5 DualSense controller.
    ///
    /// The project uses Input System 1.0.0-preview.3, which knows the PS4 DualShock but not
    /// the DualSense. DualSense support starts in Input System 1.1, which needs Unity 2019.4,
    /// and this fork stays on 2019.2.12f1. Without a layout, Unity lists the controller as an
    /// unsupported HID device, and <c>Gamepad.all</c> does not include it.
    ///
    /// The offsets come from the controller's own HID descriptor, read in the Input Debugger.
    /// They follow the package's DualShock 4 report, but the triggers and buttons are at
    /// different bytes. Byte 0 is the report ID.
    ///
    /// Only USB is supported. Over Bluetooth the controller sends a different report.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct DualSenseHIDInputReport : IInputStateTypeInfo
    {
        [FieldOffset(0)] public byte reportId;

        [InputControl(name = "leftStick", layout = "Stick", format = "VC2B")]
        [InputControl(name = "leftStick/x", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5")]
        [InputControl(name = "leftStick/left", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
        [InputControl(name = "leftStick/right", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1")]
        [InputControl(name = "leftStick/y", offset = 1, format = "BYTE", parameters = "invert,normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5")]
        [InputControl(name = "leftStick/up", offset = 1, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
        [InputControl(name = "leftStick/down", offset = 1, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1,invert=false")]
        [FieldOffset(1)] public byte leftStickX;
        [FieldOffset(2)] public byte leftStickY;

        [InputControl(name = "rightStick", layout = "Stick", format = "VC2B")]
        [InputControl(name = "rightStick/x", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5")]
        [InputControl(name = "rightStick/left", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
        [InputControl(name = "rightStick/right", offset = 0, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1")]
        [InputControl(name = "rightStick/y", offset = 1, format = "BYTE", parameters = "invert,normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5")]
        [InputControl(name = "rightStick/up", offset = 1, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0,clampMax=0.5,invert")]
        [InputControl(name = "rightStick/down", offset = 1, format = "BYTE", parameters = "normalize,normalizeMin=0,normalizeMax=1,normalizeZero=0.5,clamp=1,clampMin=0.5,clampMax=1,invert=false")]
        [FieldOffset(3)] public byte rightStickX;
        [FieldOffset(4)] public byte rightStickY;

        [InputControl(name = "leftTrigger", format = "BYTE")]
        [FieldOffset(5)] public byte leftTrigger;
        [InputControl(name = "rightTrigger", format = "BYTE")]
        [FieldOffset(6)] public byte rightTrigger;

        // Byte 7 is a report counter.

        [InputControl(name = "dpad", format = "BIT", layout = "Dpad", sizeInBits = 4, defaultState = 8)]
        [InputControl(name = "dpad/up", format = "BIT", layout = "DiscreteButton", parameters = "minValue=7,maxValue=1,nullValue=8,wrapAtValue=7", bit = 0, sizeInBits = 4)]
        [InputControl(name = "dpad/right", format = "BIT", layout = "DiscreteButton", parameters = "minValue=1,maxValue=3", bit = 0, sizeInBits = 4)]
        [InputControl(name = "dpad/down", format = "BIT", layout = "DiscreteButton", parameters = "minValue=3,maxValue=5", bit = 0, sizeInBits = 4)]
        [InputControl(name = "dpad/left", format = "BIT", layout = "DiscreteButton", parameters = "minValue=5, maxValue=7", bit = 0, sizeInBits = 4)]
        [InputControl(name = "buttonWest", displayName = "Square", bit = 4)]
        [InputControl(name = "buttonSouth", displayName = "Cross", bit = 5)]
        [InputControl(name = "buttonEast", displayName = "Circle", bit = 6)]
        [InputControl(name = "buttonNorth", displayName = "Triangle", bit = 7)]
        [FieldOffset(8)] public byte buttons1;
        [InputControl(name = "leftShoulder", bit = 0)]
        [InputControl(name = "rightShoulder", bit = 1)]
        [InputControl(name = "leftTriggerButton", layout = "Button", bit = 2)]
        [InputControl(name = "rightTriggerButton", layout = "Button", bit = 3)]
        [InputControl(name = "select", displayName = "Create", bit = 4)]
        [InputControl(name = "start", displayName = "Options", bit = 5)]
        [InputControl(name = "leftStickPress", bit = 6)]
        [InputControl(name = "rightStickPress", bit = 7)]
        [FieldOffset(9)] public byte buttons2;
        [InputControl(name = "systemButton", layout = "Button", displayName = "PS", bit = 0)]
        [InputControl(name = "touchpadButton", layout = "Button", displayName = "Touchpad Press", bit = 1)]
        [FieldOffset(10)] public byte buttons3;

        public FourCC format => new FourCC('H', 'I', 'D');
    }

    /// <summary>
    /// A PS5 DualSense controller on USB, read as a raw HID device. It derives from
    /// <see cref="DualShockGamepad"/>, so the game sees an ordinary <see cref="Gamepad"/>:
    /// Cross is the south button (jump), Square is west (attack and join), Circle is east
    /// (tongue). Haptics and the light bar are not supported.
    /// </summary>
    [InputControlLayout(stateType = typeof(DualSenseHIDInputReport), displayName = "PS5 Controller (USB)")]
    [UnityEngine.Scripting.Preserve]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoad]
#endif
    public class DualSenseGamepadHID : DualShockGamepad
    {
        static DualSenseGamepadHID()
        {
            Register();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            // Registering a layout also creates devices for matching hardware that is
            // already connected, so the controller does not need to be plugged in again.
            InputSystem.RegisterLayout<DualSenseGamepadHID>(
                matches: new InputDeviceMatcher()
                    .WithInterface("HID")
                    .WithCapability("vendorId", 0x54C)    // Sony Interactive Entertainment
                    .WithCapability("productId", 0xCE6)   // DualSense Wireless Controller
                    // 64 is the USB report. Over Bluetooth the IDs are the same but the report
                    // is different, and this layout would read the wrong bytes as buttons.
                    .WithCapability("inputReportSize", 64));
        }
    }
}
#endif
