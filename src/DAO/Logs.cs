using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace webdiff.DAO
{
    [DataContract]
    public class Logs
    {
        [DataMember]public List<LogRecord> Items { get; set; }
        [DataMember(EmitDefaultValue = false)]public DateTime LastRecordTms { get; set; }
    }
}
