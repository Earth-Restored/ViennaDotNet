using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.DB.Models.Common;

namespace ViennaDotNet.DB.Models.Player.Workshop
{
    public record InputItem(
         string id,
         int count,
         NonStackableItemInstance[] instances
    )
    {
    }
}
