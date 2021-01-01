using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


//originally based on https://github.com/InvisibleUp/twinpeaks/tree/master/TwinPeaks/Protocols
namespace SmolNetSharp.Protocols
{
    public interface IResponse
    {
        List<byte> bytes { get; }
        string mime { get; }
        Uri uri { get; }
        string encoding { get; }
    }
}
