using System.Text.Json.Serialization;
namespace Solace.ApiServer.Models;

internal sealed record Token<TData>(
    DateTimeOffset Issued,
    DateTimeOffset Expires,
    bool? Expired,
    TData Data
) where TData : ITokenData<TData>;

internal static class Tokens
{
    internal static class Live
    {
        internal sealed record UserToken(
            string UserId,
            string Username,
            string PasswordSalt,
            string PasswordHash
        ) : ITokenData<UserToken>;

        internal sealed record DeviceToken()
            : ITokenData<DeviceToken>;
    }

    internal static class Xbox
    {
        [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
        [JsonDerivedType(typeof(DeviceToken), "device")]
        [JsonDerivedType(typeof(TitleToken), "title")]
        [JsonDerivedType(typeof(UserToken), "user")]
        internal abstract class AuthToken : ITokenData<AuthToken>
        {
        }

        internal sealed class DeviceToken : AuthToken, ITokenData<DeviceToken>
        {
            public required string Did { get; init; }
        }

        internal sealed class TitleToken : AuthToken, ITokenData<TitleToken>
        {
            public required string Tid { get; init; }
        }

        internal sealed class UserToken : AuthToken, ITokenData<UserToken>
        {
            public required string Xid { get; init; }

            public required string Uhs { get; init; }

            public required string UserId { get; init; }

            public required string Username { get; init; }
        }

        internal sealed record XapiToken(
            string UserId,
            string Username
        ) : ITokenData<XapiToken>;
    }

    internal static class Playfab
    {
        internal sealed record EntityToken(
            string Id,
            string Type
        ) : ITokenData<EntityToken>;
    }

#pragma warning disable CA1716 // Identifiers should not match keywords
    internal static class Shared
#pragma warning restore CA1716 // Identifiers should not match keywords
    {
        internal sealed record XboxTicketToken(
            string UserId,
            string Username
        ) : ITokenData<XboxTicketToken>;

        internal sealed record PlayfabXboxToken(
            string UserId
        ) : ITokenData<PlayfabXboxToken>;

        internal sealed record PlayfabSessionTicket(
            string UserId
        ) : ITokenData<PlayfabSessionTicket>;
    }
}

internal interface ITokenData<TSelf> where TSelf : ITokenData<TSelf>
{
}