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

    // Reads wake events from the System log. Two providers cover the two sleep
    // architectures:
    //  - Classic S3: Power-Troubleshooter event 1, whose WakeSourceText names the
    //    device that woke the PC.
    //  - Modern Standby (S0 Low Power Idle): Power-Troubleshooter never logs, so we
    //    read Kernel-Power event 507 ("exiting Modern Standby") instead. It only
    //    carries a numeric exit reason — USB wake devices such as controllers land
    //    in the generic software-resume bucket (PoSetSystemState) rather than being
    //    attributed as HID input, but that still separates them from mouse/keyboard
    //    wakes, which is what matters.
    // Both kinds are merged into one list; SourceText is what the settings picker
    // shows and what Config.WakeSource is matched against, so no other code cares
    // which provider an event came from.
    static class WakeEvents
    {
        const string TroubleshooterXPath =
            "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]";
        const string KernelPowerXPath =
            "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=507]]";

        // Known STANDBY_EXIT_REASON codes, confirmed empirically; anything else is
        // shown as "reason code N" and can still be picked and matched as-is.
        static readonly Dictionary<int, string> StandbyExitReasons = new Dictionary<int, string>
        {
            { 7,  "PoSetSystemState" },
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
