using DustyBytes.Core.Model;

namespace DustyBytes.Units;

public interface IUnitExtractor
{
    IEnumerable<Unit> Extract(UnitContext ctx);
}
