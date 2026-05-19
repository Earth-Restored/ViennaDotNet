using System.Numerics;
using System.Text;

namespace Solace.Common.Utils;

public abstract class ValueMerger
{
    public string? CurrentUserId { protected get; set; } = "Unknown";

    public string? CurrentUsername { protected get; set; }

    public abstract Task<MergeAction> PromptMergeConflictAsync(string context, string currentValue, string incomingValue, bool
allowAutomatic);

    public virtual string CreateContextForPropertyName(string propertyName)
    {
        var conflictContext = new StringBuilder()
           .Append(propertyName);

        if (CurrentUserId is not null)
        {
            conflictContext.Append(" (user id: ");
            conflictContext.Append(CurrentUserId);

            if (CurrentUsername is null)
            {
                conflictContext.Append(')');
            }
        }

        if (CurrentUsername is not null)
        {
            conflictContext.Append(CurrentUserId is null ? " (username:" : ", username: ");
            conflictContext.Append(CurrentUsername);
            conflictContext.Append(')');
        }

        return conflictContext.ToString();
    }

    public virtual async Task<TValue> AutoMergeMax<TValue>(TValue currentValue, TValue importValue, string propertyName)
        where TValue : INumber<TValue>
        => await AutoMerge(currentValue, importValue, propertyName, TValue.Max);

    public virtual async Task<TValue> AutoMergeMin<TValue>(TValue currentValue, TValue importValue, string propertyName)
        where TValue : INumber<TValue>
        => await AutoMerge(currentValue, importValue, propertyName, TValue.Min);

    public virtual async Task<TValue> AutoMerge<TValue>(TValue? currentValue, TValue? importValue, string propertyName, Func<TValue, TValue, TValue>? getAuto)
        where TValue : IEquatable<TValue>
    {
        if (currentValue?.Equals(importValue) ?? (importValue is null))
        {
            return currentValue!;
        }

        var mergeAction = await PromptMergeConflictAsync(CreateContextForPropertyName(propertyName), currentValue?.ToString() ?? "[null]", importValue?.ToString() ?? "[null]", getAuto is not null);

        return mergeAction switch
        {
            MergeAction.KeepCurrent => currentValue!,
            MergeAction.KeepIncoming => importValue!,
            MergeAction.Auto => getAuto!(currentValue!, importValue!),
            _ => currentValue!,
        };
    }
}

public sealed class DelegateValueMerger : ValueMerger
{
    private readonly Func<string, string, string, bool, Task<MergeAction>> _promptMergeConflictAsync;

    public DelegateValueMerger(Func<string, string, string, bool, Task<MergeAction>> promptMergeConflictAsync)
    {
        _promptMergeConflictAsync = promptMergeConflictAsync;
    }

    public override Task<MergeAction> PromptMergeConflictAsync(string context, string currentValue, string incomingValue, bool allowAutomatic)
        => _promptMergeConflictAsync(context, currentValue, incomingValue, allowAutomatic);
}