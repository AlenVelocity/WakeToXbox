using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Xml;

namespace WakeToXbox
{
    sealed class WakeEvent
    {
        public DateTime TimeUtc { get; set; }
        public string SourceText { get; set; }
    }

    // Reads wake events from the System log: Power-Troubleshooter event 1 on
    // classic S3 (WakeSourceText names the waking device), plus Kernel-Power
    // event 507 exit reasons on Modern Standby, where that provider never logs.
    static class WakeEvents
    {
        const string TroubleshooterXPath =
            "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]";
        const string KernelPowerXPath =
            "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=507]]";

        static readonly Dictionary<int, string> StandbyExitReasons = new Dictionary<int, string>
        {
            { 7,  "PoSetSystemState" },  // USB wake devices (controllers) land here
            { 8,  "SetThreadExecutionState" },
            { 31, "Input Keyboard" },
            { 32, "Input Mouse" },
            { 33, "Input Touchpad" },
        };

        public static WakeEvent GetLatest()
        {
            var list = GetRecent(1);
            return list.Count > 0 ? list[0] : null;
        }

        public static List<WakeEvent> GetRecent(int max)
        {
            var results = new List<WakeEvent>();
            Collect(TroubleshooterXPath, max, ParseTroubleshooter, results);
            Collect(KernelPowerXPath, max, ParseKernelPower, results);
            results.Sort((a, b) => b.TimeUtc.CompareTo(a.TimeUtc));
            if (results.Count > max)
                results.RemoveRange(max, results.Count - max);
            return results;
        }

        static void Collect(string xpath, int max, Func<EventRecord, WakeEvent> parse,
            List<WakeEvent> results)
        {
            try
            {
                var query = new EventLogQuery("System", PathType.LogName, xpath);
                query.ReverseDirection = true;

                using (var reader = new EventLogReader(query))
                {
                    int taken = 0;
                    EventRecord record;
                    while (taken < max && (record = reader.ReadEvent()) != null)
                    {
                        using (record)
                        {
                            var evt = parse(record);
                            if (evt != null)
                            {
                                results.Add(evt);
                                taken++;
                            }
                        }
                    }
                }
            }
            catch
            {
                // One provider failing (or missing entirely) shouldn't hide the other.
            }
        }

        static WakeEvent ParseTroubleshooter(EventRecord record)
        {
            if (record.TimeCreated == null)
                return null;

            var evt = new WakeEvent();
            evt.TimeUtc = record.TimeCreated.Value.ToUniversalTime();
            evt.SourceText = "";

            var node = SelectData(record, "WakeSourceText");
            if (node != null)
                evt.SourceText = node.InnerText.Trim();

            return evt;
        }

        static WakeEvent ParseKernelPower(EventRecord record)
        {
            if (record.TimeCreated == null)
                return null;

            var node = SelectData(record, "Reason");
            int code;
            if (node == null || !int.TryParse(node.InnerText, out code))
                return null;

            string name;
            if (!StandbyExitReasons.TryGetValue(code, out name))
                name = "reason code " + code;

            var evt = new WakeEvent();
            evt.TimeUtc = record.TimeCreated.Value.ToUniversalTime();
            evt.SourceText = "Modern Standby wake: " + name;
            return evt;
        }

        static XmlNode SelectData(EventRecord record, string dataName)
        {
            var doc = new XmlDocument();
            doc.LoadXml(record.ToXml());
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("e", "http://schemas.microsoft.com/win/2004/08/events/event");
            return doc.SelectSingleNode("//e:Data[@Name='" + dataName + "']", ns);
        }
    }
}
