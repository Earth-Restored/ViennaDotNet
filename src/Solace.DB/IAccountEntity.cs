using Solace.DB.Models;

namespace Solace.DB;

public interface IEntityWithId<TId>
    where TId : notnull
{
    TId Id { get; set; }
}