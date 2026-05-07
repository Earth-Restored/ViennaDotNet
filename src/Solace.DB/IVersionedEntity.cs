namespace Solace.DB;

public interface IVersionedEntity
{
    int Version { get; set; }
}