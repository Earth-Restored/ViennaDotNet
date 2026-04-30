using System.Text.Json.Serialization;
using Solace.Common.Utils;
using Solace.DB.Models.Common;

namespace Solace.DB.Models.Player;

public sealed class Inventory
{
    [JsonInclude, JsonPropertyName("stackableItems")]
    public Dictionary<string, int?> _stackableItems;
    [JsonInclude, JsonPropertyName("nonStackableItems")]
    public Dictionary<string, Dictionary<string, NonStackableItemInstance>> _nonStackableItems;

    public Inventory()
    {
        _stackableItems = [];
        _nonStackableItems = [];
    }

    [JsonIgnore]
    public IEnumerable<StackableItem> StackableItems => _stackableItems.Select(item => new StackableItem(item.Key, item.Value));

    [JsonIgnore]
    public IEnumerable<NonStackableItem> NonStackableItems => _nonStackableItems.Select(item => new NonStackableItem(item.Key, [.. item.Value.Values]));

    public Inventory Copy()
    {
        Inventory inventory = new Inventory();
        inventory._stackableItems.AddRange(_stackableItems);
        Dictionary<string, Dictionary<string, NonStackableItemInstance>> nonStackableItems = [];
        _nonStackableItems.ForEach((id, instances) => nonStackableItems.Add(id, new Dictionary<string, NonStackableItemInstance>(instances)));
        inventory._nonStackableItems.AddRange(nonStackableItems);
        return inventory;
    }

    public sealed record StackableItem(
        string Id,
        int? Count
    );

    public sealed record NonStackableItem(
        string Id,
        NonStackableItemInstance[] Instances
    );

    public int GetItemCount(string id)
    {
        int? count = _stackableItems.GetOrDefault(id, null);
        if (count is not null)
        {
            return count.Value;
        }

        Dictionary<string, NonStackableItemInstance>? instances = _nonStackableItems!.GetOrDefault(id, null);

        return instances is not null
            ? instances.Count
            : 0;
    }

    public NonStackableItemInstance[] GetItemInstances(string id)
    {
        Dictionary<string, NonStackableItemInstance>? instances = _nonStackableItems!.GetOrDefault(id, null);
        return instances is not null
            ? [.. instances.Values]
            : [];
    }

    public NonStackableItemInstance? GetItemInstance(string id, string instanceId)
    {
        Dictionary<string, NonStackableItemInstance>? instances = _nonStackableItems!.GetOrDefault(id, null);
        return instances?.GetOrDefault(instanceId, null);
    }

    public void AddItems(string id, int count)
    {
        if (count < 0)
        {
            throw new ArgumentException(nameof(count));
        }

        _stackableItems[id] = _stackableItems.GetOrDefault(id, 0) + count;
    }

    public void AddItems(string id, NonStackableItemInstance[] instances)
    {
        Dictionary<string, NonStackableItemInstance> instancesMap = _nonStackableItems.ComputeIfAbsent(id, id1 => [])!;

        foreach (NonStackableItemInstance instance in instances)
        {
            instancesMap.Add(instance.InstanceId, instance);
        }
    }

    public bool TakeItems(string id, int count)
    {
        if (count < 0)
        {
            throw new ArgumentException(nameof(count));
        }

        int currentCount = _stackableItems.GetOrDefault(id, 0)!.Value;
        if (currentCount < count)
        {
            return false;
        }

        _stackableItems[id] = currentCount - count;
        return true;
    }

    public NonStackableItemInstance[]? TakeItems(string id, string[] instanceIds)
    {
        Dictionary<string, NonStackableItemInstance>? instanceMap = _nonStackableItems.GetValueOrDefault(id);
        if (instanceMap is null)
        {
            return null;
        }

        LinkedList<NonStackableItemInstance> instances = new();
        foreach (string instanceId in instanceIds)
        {
            NonStackableItemInstance? instance = instanceMap.JavaRemove(instanceId);
            if (instance is null)
            {
                return null;
            }

            instances.AddLast(instance);
        }

        return [.. instances];
    }
}
