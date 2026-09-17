using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace USBofon
{
    [DataContract]
    public sealed class SavedDevice
    {
        [DataMember(Order = 1)] public string InstanceId;
        [DataMember(Order = 2)] public string Name;
        [DataMember(Order = 3)] public string Note;
        [DataMember(Order = 4)] public bool Hidden;
        [DataMember(Order = 5)] public string LastDescription;
        [DataMember(Order = 6)] public string LastSeen;
    }

    [DataContract]
    internal sealed class StoreFile
    {
        [DataMember(Order = 1)] public List<SavedDevice> Devices = new List<SavedDevice>();
    }

    /// <summary>
    /// Имена, заметки и скрытые устройства. Хранятся в ProgramData, общие для всех пользователей ПК.
    /// </summary>
    public sealed class DeviceStore
    {
        private readonly Dictionary<string, SavedDevice> _devices =
            new Dictionary<string, SavedDevice>(StringComparer.OrdinalIgnoreCase);

        public string FilePath { get; }

        public DeviceStore()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "USB-of_on");
            FilePath = Path.Combine(dir, "devices.json");
        }

        public IEnumerable<SavedDevice> All => _devices.Values;

        public SavedDevice Find(string instanceId)
        {
            _devices.TryGetValue(instanceId, out var d);
            return d;
        }

        public SavedDevice GetOrAdd(string instanceId)
        {
            if (!_devices.TryGetValue(instanceId, out var d))
            {
                d = new SavedDevice { InstanceId = instanceId };
                _devices[instanceId] = d;
            }
            return d;
        }

        public void RemoveIfEmpty(string instanceId)
        {
            var d = Find(instanceId);
            if (d != null && string.IsNullOrWhiteSpace(d.Name) && string.IsNullOrWhiteSpace(d.Note) && !d.Hidden)
                _devices.Remove(instanceId);
        }

        public void Load()
        {
            _devices.Clear();
            if (!File.Exists(FilePath)) return;
            using (var fs = File.OpenRead(FilePath))
            {
                var file = (StoreFile)new DataContractJsonSerializer(typeof(StoreFile)).ReadObject(fs);
                foreach (var d in file.Devices ?? new List<SavedDevice>())
                    if (!string.IsNullOrEmpty(d.InstanceId))
                        _devices[d.InstanceId] = d;
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var file = new StoreFile { Devices = new List<SavedDevice>(_devices.Values) };
            file.Devices.Sort((a, b) => string.Compare(a.InstanceId, b.InstanceId, StringComparison.OrdinalIgnoreCase));
            var tmp = FilePath + ".tmp";
            using (var fs = File.Create(tmp))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(fs, Encoding.UTF8, true, true))
            {
                new DataContractJsonSerializer(typeof(StoreFile)).WriteObject(writer, file);
            }
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
    }
}
