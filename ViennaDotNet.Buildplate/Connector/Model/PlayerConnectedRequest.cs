using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ViennaDotNet.Buildplate.Connector.Model
{
    public record PlayerConnectedRequest(
        string uuid,
        string joinCode
    )
    {
    }
}
