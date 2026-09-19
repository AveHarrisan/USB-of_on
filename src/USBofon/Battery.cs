using System;
using System.Runtime.InteropServices;
using static USBofon.NativeMethods;

namespace USBofon
{
    /// <summary>
    /// Заряд Bluetooth-устройств. Значение ведёт сама Windows — то же, что в «Параметрах»;
    /// программа только читает готовое свойство и ничего у устройства не спрашивает,
    /// поэтому уснувшая мышь или клавиатура не просыпается.
    /// </summary>
    internal static class Battery
    {
        // DEVPKEY_Bluetooth_Battery — Windows 10 1809 и новее.
        private static readonly DEVPROPKEY BluetoothBattery =
            new DEVPROPKEY("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);

        public static int? ReadBluetooth(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA data)
        {
            var buffer = new byte[16];
            var key = BluetoothBattery;
            if (!SetupDiGetDeviceProperty(deviceInfoSet, ref data, ref key, out _, buffer, (uint)buffer.Length, out var size, 0)
                || size < 1)
                return null;
            var value = buffer[0];
            return value <= 100 ? (int?)value : null;
        }
    }
}
