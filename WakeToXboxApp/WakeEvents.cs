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

    // Reads Power-Troubleshooter wake events (System log, event ID 1), whose
    // WakeSourceText names the device that woke the PC.
    static class WakeEvents
    {
        const string QueryXPath =
            "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]";

        public static WakeEvent GetLatest()
        {
            var list = GetRecent(1);
            return list.Count > 0 ? list[0] : null;
        }

        public static List<WakeEvent> GetRecent(int max)
        {
            var results = new List<WakeEvent>();
            var query = new EventLogQuery("System", PathType.LogName, QueryXPath);
            query.ReverseDirection = true;

            using (var reader = new EventLogReader(query))
            {
                EventRecord record;
                while (results.Count < max && (record = reader.ReadEvent()) != null)
                {
                    using (record)
                    {
                        var evt = Parse(record);
                        if (evt != null)
                            results.Add(evt);
                    }
                }
            }
            return results;
        }

        static WakeEvent Parse(EventRecord record)
        {
            if (record.TimeCreated == null)
                return null;

            var evt = new WakeEvent();
            evt.TimeUtc = record.TimeCreated.Value.ToUniversalTime();
            evt.SourceText = "";

            var doc = new XmlDocument();
            doc.LoadXml(record.ToXml());
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("e", "http://schemas.microsoft.com/win/2004/08/events/event");
            var node = doc.SelectSingleNode("//e:Data[@Name='WakeSourceText']", ns);
            if (node != null)
                evt.SourceText = node.InnerText.Trim();

            return evt;
        }
    }
}
