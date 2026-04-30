using System.Text.Json.Serialization;
namespace ViennaDotNet.ApiServer.Models;

public sealed record Token<TData>(
    DateTimeOffset Issued,
    DateTimeOffset Expires,
    bool? Expired,
    TData Data
) where TData : ITokenData<TData>;

public static class Tokens
{
    public static class Live
    {
        public sealed record UserToken(
            string UserId,
            string Username,
            string PasswordSalt,
            string PasswordHash
        ) : ITokenData<UserToken>;

        public sealed record DeviceToken()
            : ITokenData<DeviceToken>;
    }

    public static class Xbox
    {
        [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
        [JsonDerivedType(typeof(DeviceToken), "device")]
        [JsonDerivedType(typeof(TitleToken), "title")]
        [JsonDerivedType(typeof(UserToken), "user")]
        public abstract class AuthToken : ITokenData<AuthToken>
        {
        }

        public sealed class DeviceToken : AuthToken, ITokenData<DeviceToken>
        {
            public required string Did { get; init; }
        }

        public sealed class TitleToken : AuthToken, ITokenData<TitleToken>
        {
            public required string Tid { get; init; }
        }

        public sealed class UserToken : AuthToken, ITokenData<UserToken>
        {
            public required string Xid { get; init; }

            public required string Uhs { get; init; }

            public required string UserId { get; init; }

            public required string Username { get; init; }
        }

        public sealed record XapiToken(
            string UserId,
            string Username
        ) : ITokenData<XapiToken>;
    }

    public static class Playfab
    {
        public sealed record EntityToken(
            string Id,
            string Type
        ) : ITokenData<EntityToken>;
    }

    public static class Shared
    {
        public sealed record XboxTicketToken(
            string UserId,
            string Username
        ) : ITokenData<XboxTicketToken>;

        public sealed record PlayfabXboxToken(
            string UserId
        ) : ITokenData<PlayfabXboxToken>;

        public sealed record PlayfabSessionTicket(
            string UserId
        ) : ITokenData<PlayfabSessionTicket>;
    }
}

public interface ITokenData<TSelf> where TSelf : ITokenData<TSelf>
{
}