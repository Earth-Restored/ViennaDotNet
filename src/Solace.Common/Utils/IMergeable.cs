namespace Solace.Common.Utils;

public interface IMergeable<TSelf>
    where TSelf : IMergeable<TSelf>
{
    Task MergeWith(TSelf other, ValueMerger merger);
}