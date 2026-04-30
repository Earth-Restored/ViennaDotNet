using System.Text.Json.Serialization;

namespace Solace.ApiServer;

public sealed record class Config(Config.LoginR Login, Config.XboxLiveR XboxLive, Config.PlayfabApiR PlayfabApi)
{
    public static readonly Config Default = new Config
    (
        new LoginR(
            SoapHeaderValidityMinutes: 1,
            UserTokenValidityMinutes: 7 * 24 * 60,
            DeviceTokenValidityMinutes: 1,
            XboxTokenValidityMinutes: 7 * 24 * 60,
            UserTokenSecret: "Mf5HWU566mwFuxxyXa2ACPvZVw9DTfzO4DREWk0aoxfkaVEhM6OfJRQ2MR1FhtPpVgkhEBBBG1PJvjy6LoO90A==",
            DeviceTokenSecret: "2MonNUihCGLzZRhMMkZ6GgFFnxj0Jk60Mvhoa2NVaOW51cDd4ZKD8L5RAbgcO1R9vfs4V/JZE6KmWW16I0OesQ==",
            XboxTokenSecret: "Q/cQFxZs/PahNgsNrvEOUAQ6RQ45MTAaRXH9LNpSrZpjQ99RBmyxuJwOcnkX6daCuVqdo8/eefpe1wUamn9YTA==",
            UserTokenSessionKey: "W1oCtEFI0XJjOW0c3oDJ/kWRR4Q7CSlE"
        ),
        new XboxLiveR(
            TokenValidityMinutes: 7 * 24 * 60,
            AuthTokenSecret: "zcGJXsfsHik4UJeK/usZPbMnVUhlUdH8vzo4JewgpyAbfxglXP9BGQrOYUKzPsa4SWnzC4E8j8EfCOm9hBTGGw==",
            XapiTokenSecret: "iDsUT5D6FP2K2h4IEwoquwBMc7Bdj8GuZbv3+1610EjEbBdoDo+4LJIKiUF+K6keBF+pWUsQIQpIYvdt0iCmBw==",
            PlayfabTokenSecret: "Iewc7mNZ4RzXUimHvPauBquzwTZq5K2dWLnnpUHGji3TAMq6PiazPyb/2igVNK9dLjMUpzqoUvxnM/niCKuWOA=="
        ),
        new PlayfabApiR(
            EntityTokenValidityMinutes: 24 * 60,
            SessionTicketValidityMinutes: 24 * 60,
            EntityTokenSecret: "/T7gV2UtfbN3OAsSFt1U73+DLocbc7HzBmywI4X2wFgr/yKcTo51UNJUAwiInJ08pWIqBUP9WoE/to4cBWUQlg==",
            SessionTicketSecret: "mKpXyjZkqnzCLKqjdnpXqAYyYHL07I+Tbqs8j9HKJAe3MvRNOtjb59vp/vREJFg6WPOJ4g8AYfsRr107CQKp4Q=="
        )
    );

    public sealed record LoginR(
        int SoapHeaderValidityMinutes,
        int UserTokenValidityMinutes,
        int DeviceTokenValidityMinutes,
        int XboxTokenValidityMinutes,
        string UserTokenSecret,
        string DeviceTokenSecret,
        string XboxTokenSecret,
        string UserTokenSessionKey
    )
    {
        private byte[]? _userTokenSecretBytes;
        private byte[]? _deviceTokenSecretBytes;
        private byte[]? _xboxTokenSecretBytes;
        private byte[]? _userTokenSessionKeyBytes;

        [JsonIgnore]
        public byte[] UserTokenSecretBytes => _userTokenSecretBytes ??= Convert.FromBase64String(UserTokenSecret);

        [JsonIgnore]
        public byte[] DeviceTokenSecretBytes => _deviceTokenSecretBytes ??= Convert.FromBase64String(DeviceTokenSecret);

        [JsonIgnore]
        public byte[] XboxTokenSecretBytes => _xboxTokenSecretBytes ??= Convert.FromBase64String(XboxTokenSecret);

        [JsonIgnore]
        public byte[] UserTokenSessionKeyBytes => _userTokenSessionKeyBytes ??= Convert.FromBase64String(UserTokenSessionKey);
    }

    public sealed record XboxLiveR(
        int TokenValidityMinutes,
        string AuthTokenSecret,
        string XapiTokenSecret,
        string PlayfabTokenSecret
    )
    {
        private byte[]? _authTokenSecretBytes;
        private byte[]? _xapiTokenSecretBytes;
        private byte[]? _playfabTokenSecretBytes;

        [JsonIgnore]
        public byte[] AuthTokenSecretBytes => _authTokenSecretBytes ??= Convert.FromBase64String(AuthTokenSecret);

        [JsonIgnore]
        public byte[] XapiTokenSecretBytes => _xapiTokenSecretBytes ??= Convert.FromBase64String(XapiTokenSecret);

        [JsonIgnore]
        public byte[] PlayfabTokenSecretBytes => _playfabTokenSecretBytes ??= Convert.FromBase64String(PlayfabTokenSecret);
    }

    public sealed record PlayfabApiR(
        int EntityTokenValidityMinutes,
        int SessionTicketValidityMinutes,
        string EntityTokenSecret,
        string SessionTicketSecret
    )
    {
        private byte[]? _entityTokenSecretBytes;
        private byte[]? _sessionTicketSecretBytes;

        [JsonIgnore]
        public byte[] EntityTokenSecretBytes => _entityTokenSecretBytes ??= Convert.FromBase64String(EntityTokenSecret);

        [JsonIgnore]
        public byte[] SessionTicketSecretBytes => _sessionTicketSecretBytes ??= Convert.FromBase64String(SessionTicketSecret);
    }
}
